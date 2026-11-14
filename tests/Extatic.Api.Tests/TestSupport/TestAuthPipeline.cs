using System.Text;
using Extatic.Api.Auth;
using Extatic.Api.Auth.Requirements;
using Extatic.Api.Auth.Schemes;
using Extatic.Api.Data;
using Extatic.Api.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace Extatic.Api.Tests.TestSupport;

/// <summary>
/// The real authentication schemes + authorization policies (mirroring Program.cs), backed by the
/// fixture database. Use it to test that a policy accepts/rejects a request without hosting the API.
/// </summary>
public sealed class TestAuthPipeline : IDisposable
{
    private readonly ServiceProvider services;

    public TestAuthPipeline(ApiTestFixture fixture, IConfiguration? configuration = null)
    {
        Configuration = configuration ?? TestServices.Configuration();
        var collection = new ServiceCollection();
        collection.AddLogging();
        collection.AddSingleton(Configuration);
        collection.AddScoped<AppDbContext>(_ => fixture.CreateContext());
        collection.AddScoped<UserService>();
        collection.AddHttpContextAccessor();
        collection.AddAuthentication(options =>
            {
                options.DefaultScheme = AuthSchemes.SmartSelector;
                options.DefaultChallengeScheme = AuthSchemes.SmartSelector;
            })
            .AddPolicyScheme(AuthSchemes.SmartSelector, "Smart Selector", options =>
            {
                options.ForwardDefaultSelector = ctx =>
                {
                    var segments = ctx.Request.Path.Value?.Split('/', StringSplitOptions.RemoveEmptyEntries);
                    var second = segments?.ElementAtOrDefault(1) ?? "";
                    return second.StartsWith("app_") ? AuthSchemes.ApiKey : AuthSchemes.OAuthProxy;
                };
            })
            .AddScheme<AuthenticationSchemeOptions, OAuthProxyAuthenticationHandler>(AuthSchemes.OAuthProxy, _ => { })
            .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(AuthSchemes.ApiKey, _ => { })
            .AddScheme<AuthenticationSchemeOptions, ServerKeyAuthenticationHandler>(AuthSchemes.ServerKey, _ => { })
            .AddJwtBearer(AuthSchemes.AppUser, options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = Configuration["Jwt:Issuer"],
                    ValidAudience = Configuration["Jwt:Audience"],
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Configuration["Jwt:Key"]!))
                };
            });
        collection.AddAuthorization(options =>
        {
            options.AddPolicy(PolicyNames.AuthenticatedAppUser, policy =>
                policy.RequireAuthenticatedUser()
                      .AddAuthenticationSchemes(AuthSchemes.AppUser)
                      .AddRequirements(new AppUserAppRequirement()));
            options.AddFeatureFoundationPolicies();
        });
        collection.AddSingleton<IPlatformAdminService, PlatformAdminService>();
        collection.AddSingleton<IAuthorizationHandler, PlatformAdminHandler>();
        collection.AddSingleton<IAuthorizationHandler, AppUserAppHandler>();
        services = collection.BuildServiceProvider();
    }

    public IConfiguration Configuration { get; }

    public HttpContext CreateContext(string path, IDictionary<string, string>? headers = null)
    {
        var context = new DefaultHttpContext { RequestServices = services.CreateScope().ServiceProvider };
        context.Request.Path = path;
        context.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("203.0.113.10");
        if (headers is not null)
            foreach (var (k, v) in headers) context.Request.Headers[k] = v;
        return context;
    }

    /// <summary>Runs default authentication (as UseAuthentication does), then the named policy.</summary>
    public async Task<PolicyAuthorizationResult> AuthorizeAsync(HttpContext context, string policyName)
    {
        var defaultResult = await context.AuthenticateAsync();
        if (defaultResult.Succeeded) context.User = defaultResult.Principal!;

        var policy = await services.GetRequiredService<IAuthorizationPolicyProvider>().GetPolicyAsync(policyName)
            ?? throw new InvalidOperationException($"Unknown policy {policyName}");
        var evaluator = context.RequestServices.GetRequiredService<IPolicyEvaluator>();
        var authentication = await evaluator.AuthenticateAsync(policy, context);
        return await evaluator.AuthorizeAsync(policy, authentication, context, context);
    }

    public void Dispose() => services.Dispose();
}
