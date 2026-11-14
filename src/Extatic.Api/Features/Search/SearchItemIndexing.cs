using Extatic.Api.Data;
using Extatic.Api.Domain.Entities;
using Extatic.Api.Services;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace Extatic.Api.Features.Search;

/// <summary>
/// Keeps Item documents in step with Item writes. Runs inside ItemService's transaction, so the
/// document upsert commits (or rolls back) with the Item. Deletes need no code: search_documents.item_id
/// is ON DELETE CASCADE, so the row disappears in the same statement as the Item.
/// </summary>
public class SearchItemChangeListener : IItemChangeListener
{
    public async Task OnItemSavedAsync(AppDbContext db, Guid appId, Item item, bool created)
    {
        var config = await db.SearchCollectionConfigs.AsNoTracking()
            .FirstOrDefaultAsync(c => c.AppId == appId && c.CollectionId == item.CollectionId);
        if (config is not { Enabled: true }) return;
        var enabled = await db.SearchSettings.Where(s => s.AppId == appId).Select(s => s.Enabled).FirstOrDefaultAsync();
        if (!enabled) return;
        var document = ItemDocumentBuilder.Build(config, item.Id, item.Data, item.CreatedAt);
        await SearchIndexWriter.UpsertAsync(db, appId, [document]);
    }

    public Task OnItemDeletingAsync(AppDbContext db, Guid appId, Item item) => Task.CompletedTask;
}

/// <summary>Executes one reindex job (all enabled collections of an app, or one collection).</summary>
public class SearchReindexer(AppDbContext db)
{
    public const int BatchSize = 200;

    /// <param name="progress">Called with (processed, total) after each batch; used to heartbeat the job lease.</param>
    public async Task RunAsync(SearchReindexJob job, Func<int, int, Task> progress, CancellationToken ct = default)
    {
        var settings = await SearchSettingsStore.GetAsync(db, job.AppId, ct);
        var configs = await db.SearchCollectionConfigs.AsNoTracking()
            .Where(c => c.AppId == job.AppId && (job.CollectionId == null || c.CollectionId == job.CollectionId))
            .ToListAsync(ct);

        if (job.CollectionId is { } single && configs.All(c => !c.Enabled))
        {
            await SearchIndexWriter.DeleteCollectionDocumentsAsync(db, job.AppId, single, ct);
            await progress(0, 0);
            return;
        }
        if (!settings.Enabled) { await progress(0, 0); return; }

        var enabled = configs.Where(c => c.Enabled).ToList();
        if (job.CollectionId is null)
        {
            // Drop item documents of collections that are no longer configured for search.
            var keep = enabled.Select(c => c.CollectionId).ToList();
            await db.SearchDocuments
                .Where(d => d.AppId == job.AppId && d.Source == SearchSources.Item
                    && (d.CollectionId == null || !keep.Contains(d.CollectionId.Value)))
                .ExecuteDeleteAsync(ct);
        }

        var ids = enabled.Select(c => c.CollectionId).ToList();
        var total = await db.Items.CountAsync(i => ids.Contains(i.CollectionId) && i.Collection.AppId == job.AppId, ct);
        var processed = 0;
        await progress(processed, total);
        foreach (var config in enabled)
        {
            Guid? after = null;
            while (true)
            {
                var count = await IndexBatchAsync(config, after, ct);
                if (count.Rows == 0) break;
                processed += count.Rows;
                after = count.LastId;
                await progress(processed, Math.Max(total, processed));
                if (count.Rows < BatchSize) break;
            }
        }
    }

    /// <summary>
    /// Indexes the next batch of Items. The Items are read FOR SHARE inside the same transaction as the
    /// upsert, so a concurrent Item update either commits first (and is read here) or waits until this
    /// batch commits (and then writes the newer document) — the index never regresses to stale data.
    /// </summary>
    private async Task<(int Rows, Guid? LastId)> IndexBatchAsync(SearchCollectionConfig config, Guid? after,
        CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var rows = await SearchSql.RunAsync(db, async (connection, tx) =>
        {
            await using var command = new NpgsqlCommand("""
                SELECT i."Id", i."Data"::text, i."CreatedAt"
                FROM items i JOIN collections c ON c."Id" = i."CollectionId"
                WHERE i."CollectionId" = @collection AND c."AppId" = @app AND (@after IS NULL OR i."Id" > @after)
                ORDER BY i."Id"
                LIMIT @take
                FOR SHARE OF i
                """, connection, tx);
            command.Parameters.Add(SearchSql.P("collection", config.CollectionId, NpgsqlDbType.Uuid));
            command.Parameters.Add(SearchSql.P("app", config.AppId, NpgsqlDbType.Uuid));
            command.Parameters.Add(SearchSql.P("after", after, NpgsqlDbType.Uuid));
            command.Parameters.Add(SearchSql.P("take", BatchSize, NpgsqlDbType.Integer));
            var list = new List<(Guid Id, string Data, DateTime CreatedAt)>();
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                list.Add((reader.GetGuid(0), reader.GetString(1), reader.GetDateTime(2)));
            return list;
        }, ct);
        if (rows.Count == 0) return (0, null);
        var documents = rows.Select(r => ItemDocumentBuilder.Build(config, r.Id, r.Data, r.CreatedAt)).ToList();
        await SearchIndexWriter.UpsertAsync(db, config.AppId, documents, ct);
        await transaction.CommitAsync(ct);
        return (rows.Count, rows[^1].Id);
    }
}
