using System.Text;
using System.Text.Json;
using Extatic.Api.Auth;
using Extatic.Api.Auth.Handlers;
using Extatic.Api.Auth.Requirements;
using Extatic.Api.Auth.Schemes;
using Extatic.Api.Data;
using Extatic.Api.Domain.Enums;
using Extatic.Api.Middleware;
using Extatic.Api.Services;
using Extatic.Api.Storage;
using Extatic.Api.Validation;
using Extatic.Api.Webhooks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

// Database
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

// Auth
var jwtKey = builder.Configuration["Jwt:Key"]
    ?? throw new InvalidOperationException("Jwt:Key is required");

builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = AuthSchemes.SmartSelector;
    options.DefaultChallengeScheme = AuthSchemes.SmartSelector;
})
.AddPolicyScheme(AuthSchemes.SmartSelector, "Smart Selector", options =>
{
    options.ForwardDefaultSelector = ctx =>
        ctx.Request.Path.StartsWithSegments("/api/client")
            ? AuthSchemes.ApiKey
            : AuthSchemes.OAuthProxy;
})
.AddScheme<AuthenticationSchemeOptions, OAuthProxyAuthenticationHandler>(AuthSchemes.OAuthProxy, _ => { })
.AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(AuthSchemes.ApiKey, _ => { })
.AddJwtBearer(AuthSchemes.AppUser, options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidIssuer = builder.Configuration["Jwt:Issuer"],
        ValidateAudience = true,
        ValidAudience = builder.Configuration["Jwt:Audience"],
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
        ValidateLifetime = true
    };
});

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(PolicyNames.PlatformUser, policy =>
        policy.RequireAuthenticatedUser()
              .AddAuthenticationSchemes(AuthSchemes.OAuthProxy));

    options.AddPolicy(PolicyNames.AppAnyAccess, policy =>
        policy.RequireAuthenticatedUser()
              .AddAuthenticationSchemes(AuthSchemes.OAuthProxy)
              .AddRequirements(new AppRoleRequirement(CollaboratorRole.Viewer)));

    options.AddPolicy(PolicyNames.AppOwnerOrAdmin, policy =>
        policy.RequireAuthenticatedUser()
              .AddAuthenticationSchemes(AuthSchemes.OAuthProxy)
              .AddRequirements(new AppRoleRequirement(CollaboratorRole.Admin)));

    options.AddPolicy(PolicyNames.AppOwnerOrEditor, policy =>
        policy.RequireAuthenticatedUser()
              .AddAuthenticationSchemes(AuthSchemes.OAuthProxy)
              .AddRequirements(new AppRoleRequirement(CollaboratorRole.Editor)));

    options.AddPolicy(PolicyNames.AppOwnerOnly, policy =>
        policy.RequireAuthenticatedUser()
              .AddAuthenticationSchemes(AuthSchemes.OAuthProxy)
              .AddRequirements(new AppRoleRequirement(null, ownerOnly: true)));

    options.AddPolicy(PolicyNames.AuthenticatedAppUser, policy =>
        policy.RequireAuthenticatedUser()
              .AddAuthenticationSchemes(AuthSchemes.AppUser));
});

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IAuthorizationHandler, AppRoleHandler>();

// Services
builder.Services.AddScoped<UserService>();
builder.Services.AddScoped<AppService>();
builder.Services.AddScoped<CollectionService>();
builder.Services.AddScoped<ItemService>();
builder.Services.AddScoped<AppUserService>();
builder.Services.AddScoped<CollaboratorService>();
builder.Services.AddScoped<WebhookService>();
builder.Services.AddScoped<AttachmentService>();
builder.Services.AddScoped<DevTokenService>();
builder.Services.AddScoped<JsonSchemaValidator>();

// Storage
builder.Services.AddSingleton<IBlobStorageService, AzureBlobStorageService>();

// Webhooks
builder.Services.AddSingleton<WebhookPayloadBuilder>();
builder.Services.AddSingleton<WebhookDispatcher>();
builder.Services.AddHostedService<WebhookDeliveryWorker>();
builder.Services.AddHostedService<WebhookRetryWorker>();
builder.Services.AddHostedService<WebhookLogCleanupWorker>();

// HTTP client for webhook delivery
builder.Services.AddHttpClient("webhook", client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
});

// Rate limiting (Client API: 100 req/min per IP)
builder.Services.AddRateLimiter(options =>
{
    options.AddFixedWindowLimiter("client-api", limiterOptions =>
    {
        limiterOptions.PermitLimit = 100;
        limiterOptions.Window = TimeSpan.FromMinutes(1);
        limiterOptions.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        limiterOptions.QueueLimit = 0;
    });
});

// CORS
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
});

// Exception handling
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

// Controllers with snake_case JSON
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
    });

// Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Auto-migrate in development
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
}

app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors();
app.UseRateLimiter();
app.UseAuthentication();
app.UseMiddleware<AppContextMiddleware>();
app.UseAuthorization();
app.MapControllers();

app.Run();
