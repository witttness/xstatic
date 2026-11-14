using Extatic.Api.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Extatic.Api.Features.Donations;

internal static class DonationDb
{
    public static bool IsUniqueViolation(DbUpdateException ex)
        => ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };

    /// <summary>Loads and row-locks (FOR UPDATE) a donation of <paramref name="appId"/>. Call inside a transaction.</summary>
    public static Task<Donation?> LockDonationAsync(AppDbContext db, Guid appId, Guid donationId)
        => db.Donations
            .FromSql($"SELECT * FROM donations WHERE \"Id\" = {donationId} AND \"AppId\" = {appId} FOR UPDATE")
            .SingleOrDefaultAsync();

    /// <summary>Row-locks the donation of <paramref name="appId"/> paid by <paramref name="paymentId"/>.</summary>
    public static Task<Donation?> LockDonationByPaymentAsync(AppDbContext db, Guid appId, string provider, string paymentId)
        => db.Donations
            .FromSql($"SELECT * FROM donations WHERE \"AppId\" = {appId} AND \"Provider\" = {provider} AND \"ProviderPaymentId\" = {paymentId} FOR UPDATE")
            .FirstOrDefaultAsync();

    /// <summary>Escapes LIKE wildcards; use with escape character '\'.</summary>
    public static string LikeContains(string term)
        => "%" + term.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
}
