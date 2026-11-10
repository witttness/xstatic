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
            var (_, _, collection) = await fixture.SeedAppAsync();
            var itemId = Guid.NewGuid();
            var data = "{}";
            await using (var db = fixture.CreateContext())
            {
                await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO items (\"Id\", \"CollectionId\", \"Data\", \"CreatedAt\", \"UpdatedAt\") VALUES ({itemId}, {collection.Id}, {data}::jsonb, {DateTime.UtcNow}, {DateTime.UtcNow})");
                await db.Database.MigrateAsync();
            }
            await using var verify = fixture.CreateContext();
            Assert.Equal(1, (await verify.Items.SingleAsync(i => i.Id == itemId)).Version);
        }
        finally { await fixture.DisposeAsync(); }
    }
}
