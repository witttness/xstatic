using Extatic.Api.Data;
using Extatic.Api.Domain.Entities;

namespace Extatic.Api.Services;

/// <summary>
/// Hook for features that react to generic Item writes (e.g. search indexing). Register with
/// <c>services.AddScoped&lt;IItemChangeListener, MyListener&gt;()</c> in the feature module.
/// Listeners run inside ItemService's transaction: make changes through <paramref name="db"/>
/// (do NOT call SaveChanges or open a new context) and they are committed atomically with the Item.
/// Throwing aborts the Item write.
/// </summary>
public interface IItemChangeListener
{
    /// <summary>After the Item is inserted/updated (Item.Id is set), before commit.</summary>
    Task OnItemSavedAsync(AppDbContext db, Guid appId, Item item, bool created);

    /// <summary>Before the Item is removed; changes are saved with the delete.</summary>
    Task OnItemDeletingAsync(AppDbContext db, Guid appId, Item item);
}
