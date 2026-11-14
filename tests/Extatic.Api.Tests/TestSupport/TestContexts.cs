using System.Net;
using System.Security.Claims;
using Extatic.Api.Auth;
using Extatic.Api.Domain.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Extatic.Api.Tests.TestSupport;

/// <summary>Builds ControllerContexts that look like what the real auth pipeline produces.</summary>
public static class TestContexts
{
    /// <summary>
    /// Platform API request by a dashboard user (OAuthProxy identity). <paramref name="app"/> is
    /// placed in HttpContext.Items["CurrentApp"] as AppContextMiddleware would.
    /// </summary>
    public static ControllerContext Platform(App? app, Guid userId, string? email = null,
        IServiceProvider? services = null)
    {
        var claims = new List<Claim> { new(AppClaimTypes.UserId, userId.ToString()) };
        if (email is not null) claims.Add(new Claim(AppClaimTypes.UserEmail, email));
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, AuthSchemes.OAuthProxy)),
            RequestServices = services ?? DefaultServices()
        };
        if (app is not null) context.Items["CurrentApp"] = app;
        return new ControllerContext { HttpContext = context };
    }

    /// <summary>
    /// Client API request: an ApiKey identity for <paramref name="app"/>, plus (when
    /// <paramref name="appUserId"/> is set) an AppUser JWT identity. <paramref name="tokenAppId"/>
    /// overrides the token's app id to simulate a token issued for another tenant.
    /// </summary>
    public static ControllerContext Client(App app, Guid? appUserId = null, IPAddress? remoteIp = null,
        Guid? tokenAppId = null, IServiceProvider? services = null)
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(AppClaimTypes.AppId, app.Id.ToString())], AuthSchemes.ApiKey));
        if (appUserId is not null)
        {
            principal.AddIdentity(new ClaimsIdentity(
            [
                new Claim(AppClaimTypes.AppUserId, appUserId.Value.ToString()),
                new Claim(AppClaimTypes.AppId, (tokenAppId ?? app.Id).ToString()),
                new Claim(AppClaimTypes.Provider, "test")
            ], AuthSchemes.AppUser));
        }
        var context = new DefaultHttpContext
        {
            User = principal,
            RequestServices = services ?? DefaultServices()
        };
        context.Connection.RemoteIpAddress = remoteIp ?? IPAddress.Parse("203.0.113.10");
        context.Items["CurrentApp"] = app;
        return new ControllerContext { HttpContext = context };
    }

    /// <summary>Request services containing the test IConfiguration (needed by ClientIps.Hash).</summary>
    public static IServiceProvider DefaultServices(IConfiguration? configuration = null)
        => new ServiceCollection()
            .AddSingleton(configuration ?? TestServices.Configuration())
            .BuildServiceProvider();
}
