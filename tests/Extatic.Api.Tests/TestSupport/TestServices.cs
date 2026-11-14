using Extatic.Api.Auth;
using Extatic.Api.Notifications;
using Extatic.Api.Security;
using Extatic.Api.Webhooks;
using Microsoft.Extensions.Configuration;

namespace Extatic.Api.Tests.TestSupport;

/// <summary>Factories for commonly needed services in feature tests.</summary>
public static class TestServices
{
    public const string JwtKey = "test-secret-key-minimum-32-chars-long!!";

    /// <summary>In-memory configuration with Jwt:* set, plus any overrides.</summary>
    public static IConfiguration Configuration(IDictionary<string, string?>? overrides = null)
    {
        var values = new Dictionary<string, string?>
        {
            ["Jwt:Key"] = JwtKey,
            ["Jwt:Issuer"] = "extatic-test",
            ["Jwt:Audience"] = "extatic-client"
        };
        if (overrides is not null)
            foreach (var (k, v) in overrides) values[k] = v;
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    public static WebhookDispatcher WebhookDispatcher(ApiTestFixture fixture)
        => new(fixture.CreateScopeFactory(), new WebhookPayloadBuilder());

    public static IEmailQueue EmailQueue() => new EmailQueue();

    /// <summary>Ephemeral (in-memory keys) protector with the production purpose string.</summary>
    public static ISecretProtector SecretProtector() => DataProtectionSecretProtector.CreateEphemeral();

    public static AppUserTokenService AppUserTokenService(IConfiguration? configuration = null)
        => new(configuration ?? Configuration());

    public static IPlatformAdminService PlatformAdmins(params string[] adminEmails)
        => new PlatformAdminService(Configuration(new Dictionary<string, string?>
        {
            ["Platform:AdminEmails"] = string.Join(',', adminEmails)
        }));
}

/// <summary>Records sent emails; optionally fails the first N sends.</summary>
public sealed class FakeEmailSender(int failFirst = 0) : IEmailSender
{
    private int remainingFailures = failFirst;
    public List<OutgoingEmail> Sent { get; } = [];
    public int Calls { get; private set; }

    public Task SendAsync(OutgoingEmail email, CancellationToken ct = default)
    {
        Calls++;
        if (remainingFailures > 0)
        {
            remainingFailures--;
            throw new InvalidOperationException("Simulated SMTP failure");
        }
        Sent.Add(email);
        return Task.CompletedTask;
    }
}
