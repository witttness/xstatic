using Extatic.Api.Modules;

namespace Extatic.Api.Webhooks;

/// <summary>
/// The set of webhook event types a Webhook may subscribe to: the core item events plus every
/// event declared by a feature module.
/// </summary>
public class WebhookEventCatalog
{
    public static readonly IReadOnlyList<string> CoreEvents = ["item.created", "item.updated", "item.deleted"];

    private readonly HashSet<string> known;

    public WebhookEventCatalog(IEnumerable<IFeatureModule> modules)
    {
        var all = new List<string>(CoreEvents);
        foreach (var e in modules.SelectMany(m => m.WebhookEvents))
            if (!all.Contains(e, StringComparer.Ordinal)) all.Add(e);
        Events = all;
        known = new HashSet<string>(all, StringComparer.Ordinal);
    }

    /// <summary>Core events first, then module events in module discovery order.</summary>
    public IReadOnlyList<string> Events { get; }

    public bool IsKnown(string eventType) => known.Contains(eventType);
}
