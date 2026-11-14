using System.Security.Cryptography;
using Extatic.Api.Data;
using Extatic.Api.Features.Donations.Providers;
using Extatic.Api.Security;
using Extatic.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace Extatic.Api.Features.Donations;

public class PaymentProviderConfigService(AppDbContext db, ISecretProtector protector, PaymentProviderRegistry registry)
{
    /// <summary>Events the tenant must enable on their provider webhook endpoint.</summary>
    public static readonly IReadOnlyList<string> RequiredStripeEvents =
    [
        "checkout.session.completed",
        "checkout.session.async_payment_succeeded",
        "checkout.session.async_payment_failed",
        "checkout.session.expired",
        "charge.refunded"
    ];

    public async Task<ProviderConfigDto> GetAsync(Guid appId, string provider, string webhookUrl)
    {
        registry.Get(provider);
        var config = await db.PaymentProviderConfigs.AsNoTracking()
            .SingleOrDefaultAsync(c => c.AppId == appId && c.Provider == provider);
        return ToDto(provider, config, webhookUrl);
    }

    public async Task<ProviderConfigDto> PutAsync(Guid appId, string provider, PutProviderConfigRequest req, string webhookUrl)
    {
        var impl = registry.Get(provider);
        var config = await db.PaymentProviderConfigs
            .SingleOrDefaultAsync(c => c.AppId == appId && c.Provider == provider);

        var secretKey = Normalise(req.SecretKey);
        var webhookSecret = Normalise(req.WebhookSecret);
        var publishableKey = req.PublishableKey is null ? null : Normalise(req.PublishableKey) ?? "";
        if (config is null && secretKey is null)
            throw new ValidationException("Secret key is required",
                [new ValidationError("secret_key", "Required when connecting a provider")]);

        // Validate against the effective secret so a publishable key can't silently mismatch live/test mode.
        var effectiveSecret = secretKey ?? (config is null ? null : TryUnprotect(config.SecretKeyProtected));
        var keyInfo = impl.ValidateKeys(effectiveSecret, publishableKey is "" ? null : publishableKey ?? config?.PublishableKey, webhookSecret);

        var created = config is null;
        config ??= new PaymentProviderConfig { AppId = appId, Provider = provider };
        if (secretKey is not null)
        {
            config.SecretKeyProtected = protector.Protect(secretKey);
            config.SecretKeyLast4 = Last4(secretKey);
        }
        if (webhookSecret is not null)
        {
            config.WebhookSecretProtected = protector.Protect(webhookSecret);
            config.WebhookSecretLast4 = Last4(webhookSecret);
        }
        if (publishableKey is not null) config.PublishableKey = publishableKey is "" ? null : publishableKey;
        config.IsLiveMode = keyInfo.IsLiveMode;
        if (req.IsEnabled is { } enabled) config.IsEnabled = enabled;

        if (created) db.PaymentProviderConfigs.Add(config);
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (DonationDb.IsUniqueViolation(ex))
        {
            throw new ConflictException("The provider configuration was changed concurrently; retry");
        }
        return ToDto(provider, config, webhookUrl);
    }

    public async Task DeleteAsync(Guid appId, string provider)
    {
        registry.Get(provider);
        var deleted = await db.PaymentProviderConfigs
            .Where(c => c.AppId == appId && c.Provider == provider)
            .ExecuteDeleteAsync();
        if (deleted == 0) throw new NotFoundException("Payment provider is not configured");
    }

    /// <summary>The enabled, available provider used for new checkouts (Stripe preferred), or null.</summary>
    public async Task<PaymentProviderConfig?> GetCheckoutConfigAsync(Guid appId)
    {
        var configs = await db.PaymentProviderConfigs.AsNoTracking()
            .Where(c => c.AppId == appId && c.IsEnabled)
            .ToListAsync();
        return configs
            .Where(c => registry.IsAvailable(c.Provider))
            .OrderBy(c => c.Provider == StripePaymentProvider.ProviderName ? 0 : 1)
            .ThenBy(c => c.Provider, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    public Task<PaymentProviderConfig?> FindAsync(Guid appId, string provider)
        => db.PaymentProviderConfigs.AsNoTracking().SingleOrDefaultAsync(c => c.AppId == appId && c.Provider == provider);

    /// <summary>Decrypts the stored keys. Throws <see cref="PaymentProviderException"/> if they can't be read.</summary>
    public PaymentCredentials Credentials(PaymentProviderConfig config)
    {
        try
        {
            return new PaymentCredentials(
                protector.Unprotect(config.SecretKeyProtected),
                config.PublishableKey,
                config.WebhookSecretProtected is null ? null : protector.Unprotect(config.WebhookSecretProtected));
        }
        catch (CryptographicException)
        {
            throw new PaymentProviderException("Stored payment provider keys could not be read; re-enter them in the dashboard");
        }
    }

    private string? TryUnprotect(string protectedValue)
    {
        try { return protector.Unprotect(protectedValue); }
        catch (CryptographicException) { return null; }
    }

    private static string? Normalise(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string Last4(string value) => value.Length <= 4 ? value : value[^4..];

    public static ProviderConfigDto ToDto(string provider, PaymentProviderConfig? c, string webhookUrl) => new(
        provider,
        c is not null,
        c?.PublishableKey,
        c is not null && !string.IsNullOrEmpty(c.SecretKeyProtected),
        c?.SecretKeyLast4,
        c?.WebhookSecretProtected is not null,
        c?.WebhookSecretLast4,
        c?.IsLiveMode ?? false,
        c?.IsEnabled ?? false,
        webhookUrl,
        RequiredStripeEvents,
        c?.UpdatedAt);
}
