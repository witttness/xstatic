using Microsoft.EntityFrameworkCore;

namespace Extatic.Api.Tests;

public class ItemMigrationTests
{
    [Fact]
    public async Task Migration_AssignsVersionOneToExistingItems()
    {
        var fixture = new ApiTestFixture();
        try
        {
            await fixture.PrepareLegacyDatabaseAsync();
            // Seed with raw SQL: the current EF model has columns the legacy schema does not.
            var (ownerId, appId, collectionId, itemId) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
            var now = DateTime.UtcNow;
            var data = "{}";
            await using (var db = fixture.CreateContext())
            {
                await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO users (\"Id\", \"ExternalId\", \"Email\", \"Name\", \"CreatedAt\", \"UpdatedAt\") VALUES ({ownerId}, 'legacy-owner', 'legacy@example.test', 'Owner', {now}, {now})");
                await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO apps (\"Id\", \"OwnerId\", \"Name\", \"Slug\", \"PublicId\", \"ApiKeyHash\", \"AllowedOrigins\", \"CreatedAt\", \"UpdatedAt\") VALUES ({appId}, {ownerId}, 'Legacy', 'legacy-app', 'app_legacy', 'hash', '{{}}', {now}, {now})");
                await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO collections (\"Id\", \"AppId\", \"Name\", \"Slug\", \"AttachmentsEnabled\", \"AllowedAttachmentTypes\", \"CreatedAt\", \"UpdatedAt\") VALUES ({collectionId}, {appId}, 'Items', 'items', false, '{{}}', {now}, {now})");
                await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO items (\"Id\", \"CollectionId\", \"Data\", \"CreatedAt\", \"UpdatedAt\") VALUES ({itemId}, {collectionId}, {data}::jsonb, {now}, {now})");
                await db.Database.MigrateAsync();
            }
            await using var verify = fixture.CreateContext();
            Assert.Equal(1, (await verify.Items.SingleAsync(i => i.Id == itemId)).Version);
        }
        finally { await fixture.DisposeAsync(); }
    }
}
