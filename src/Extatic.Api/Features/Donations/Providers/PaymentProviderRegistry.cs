using Extatic.Api.Services;

namespace Extatic.Api.Features.Donations.Providers;

/// <summary>
/// Resolves providers by name. The "fake" provider is only resolvable when <c>allowFake</c> is true
/// (Development environment, or tests that construct the registry directly).
/// </summary>
public class PaymentProviderRegistry
{
    private readonly Dictionary<string, IPaymentProvider> providers;

    public PaymentProviderRegistry(IEnumerable<IPaymentProvider> all, bool allowFake)
    {
        providers = all
            .Where(p => allowFake || p.Name != FakePaymentProvider.ProviderName)
            .ToDictionary(p => p.Name, StringComparer.Ordinal);
    }

    [ActivatorUtilitiesConstructor]
    public PaymentProviderRegistry(IEnumerable<IPaymentProvider> all, IHostEnvironment environment)
        : this(all, environment.IsDevelopment())
    {
    }

    public IReadOnlyCollection<string> Names => providers.Keys;

    public bool IsAvailable(string? name) => name is not null && providers.ContainsKey(name);

    /// <summary>Throws <see cref="NotFoundException"/> (404) for unknown / unavailable providers.</summary>
    public IPaymentProvider Get(string? name)
        => name is not null && providers.TryGetValue(name, out var p) ? p
            : throw new NotFoundException("Payment provider not found");
}
