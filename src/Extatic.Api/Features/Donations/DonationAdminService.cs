using Extatic.Api.Common;
using Extatic.Api.Data;
using Extatic.Api.Dtos;
using Extatic.Api.Features.Donations.Providers;
using Extatic.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace Extatic.Api.Features.Donations;

public class DonationAdminService(
    AppDbContext db,
    PaymentProviderRegistry registry,
    PaymentProviderConfigService configs,
    DonationNotifier notifier)
{
    public const int MaxExportRows = 50_000;

    private IQueryable<Donation> Filtered(Guid appId, DonationQuery q)
    {
        var query = db.Donations.AsNoTracking().Where(d => d.AppId == appId);
        if (DonationApiValues.ParseStatus(q.Status) is { } status) query = query.Where(d => d.Status == status);
        if (q.CampaignId is { } campaignId) query = query.Where(d => d.CampaignId == campaignId);
        if (q.From is { } from) { var f = from.ToUniversalTime(); query = query.Where(d => d.CreatedAt >= f); }
        if (q.To is { } to) { var t = to.ToUniversalTime(); query = query.Where(d => d.CreatedAt < t); }
        if (!string.IsNullOrWhiteSpace(q.Q))
        {
            var term = q.Q.Trim();
            if (term.Length > 200) throw new BadRequestException("q must be at most 200 characters");
            var pattern = DonationDb.LikeContains(term);
            query = query.Where(d => EF.Functions.ILike(d.DonorName ?? "", pattern, "\\")
                                     || EF.Functions.ILike(d.DonorEmail ?? "", pattern, "\\"));
        }
        return query;
    }

    public async Task<PagedResult<DonationDto>> ListAsync(Guid appId, DonationQuery q, int page, int pageSize)
    {
        Paging.Validate(page, pageSize);
        var query = Filtered(appId, q);
        var total = await query.CountAsync();
        var items = await query.Include(d => d.Campaign)
            .OrderByDescending(d => d.CreatedAt).ThenBy(d => d.Id)
            .Skip(Paging.Skip(page, pageSize)).Take(pageSize)
            .ToListAsync();
        return new PagedResult<DonationDto>(items.Select(d => d.ToDto()).ToList(), total, page, pageSize);
    }

    public async Task<DonationDto> GetAsync(Guid appId, Guid id)
        => (await db.Donations.AsNoTracking().Include(d => d.Campaign)
               .SingleOrDefaultAsync(d => d.AppId == appId && d.Id == id)
           ?? throw new NotFoundException("Donation not found")).ToDto();

    /// <summary>
    /// Full (amount null) or partial refund through the tenant's provider. The donation row is locked
    /// for the whole operation so concurrent refunds can never exceed the donated amount.
    /// </summary>
    public async Task<DonationDto> RefundAsync(Guid appId, Guid id, long? amountMinor, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var donation = await DonationDb.LockDonationAsync(db, appId, id)
            ?? throw new NotFoundException("Donation not found");
        if (donation.Status == DonationStatus.Refunded || (donation.Status == DonationStatus.Succeeded && donation.NetAmountMinor == 0))
            throw new ConflictException("This donation has already been fully refunded");
        if (donation.Status != DonationStatus.Succeeded)
            throw new ConflictException("Only succeeded donations can be refunded");
        if (string.IsNullOrEmpty(donation.ProviderPaymentId))
            throw new ConflictException("This donation has no refundable one-time payment recorded; refund recurring donations in the payment provider's dashboard");

        var refundable = donation.NetAmountMinor;
        var amount = amountMinor ?? refundable;
        if (amount < 1 || amount > refundable)
            throw new ValidationException("Invalid refund amount",
                [new ValidationError("amount_minor", $"Must be between 1 and {refundable}")]);

        var provider = registry.Get(donation.Provider);
        var config = await configs.FindAsync(appId, donation.Provider)
            ?? throw new ConflictException("The payment provider for this donation is no longer configured");
        await provider.RefundAsync(configs.Credentials(config), new RefundRequest(
            donation.Id, donation.ProviderPaymentId, amount,
            $"refund_{donation.Id:N}_{donation.RefundedAmountMinor}_{amount}"), ct);

        donation.RefundedAmountMinor += amount;
        if (donation.RefundedAmountMinor == donation.AmountMinor) donation.Status = DonationStatus.Refunded;
        await notifier.StageRefundedAsync(db, donation);
        await db.SaveChangesAsync(CancellationToken.None);
        await tx.CommitAsync(CancellationToken.None);
        return await GetAsync(appId, id);
    }

    public async Task<byte[]> ExportCsvAsync(Guid appId, DonationQuery q)
    {
        var rows = await Filtered(appId, q).Include(d => d.Campaign)
            .OrderByDescending(d => d.CreatedAt).ThenBy(d => d.Id)
            .Take(MaxExportRows)
            .ToListAsync();
        var csv = new CsvBuilder().AddRow("id", "created_at", "completed_at", "status", "campaign", "amount", "refunded",
            "currency", "amount_minor", "refunded_amount_minor", "interval", "donor_name", "donor_email", "is_anonymous",
            "message", "provider", "provider_payment_id");
        foreach (var d in rows)
            csv.AddRow(d.Id, d.CreatedAt, d.CompletedAt, d.Status.ToApi(), d.Campaign?.Slug,
                Currencies.FormatMajor(d.AmountMinor, d.Currency), Currencies.FormatMajor(d.RefundedAmountMinor, d.Currency),
                d.Currency, d.AmountMinor, d.RefundedAmountMinor, d.Interval.ToApi(), d.DonorName, d.DonorEmail,
                d.IsAnonymous, d.Message, d.Provider, d.ProviderPaymentId);
        return csv.ToBytes();
    }

    public async Task<DonationSummaryDto> SummaryAsync(Guid appId)
    {
        var since = DateTime.UtcNow.AddDays(-30);
        var counted = db.Donations.AsNoTracking()
            .Where(d => d.AppId == appId && (d.Status == DonationStatus.Succeeded || d.Status == DonationStatus.Refunded));

        async Task<List<CurrencyAmount>> ByCurrency(IQueryable<Donation> source)
            => (await source.GroupBy(d => d.Currency)
                    .Select(g => new
                    {
                        Currency = g.Key,
                        Amount = g.Sum(d => d.AmountMinor - d.RefundedAmountMinor),
                        Count = g.Count(d => d.AmountMinor > d.RefundedAmountMinor)
                    })
                    .ToListAsync())
                .OrderBy(r => r.Currency, StringComparer.Ordinal)
                .Select(r => new CurrencyAmount(r.Currency, r.Amount, r.Count)).ToList();

        var total = await ByCurrency(counted);
        var recent = await ByCurrency(counted.Where(d => d.CompletedAt >= since));
        var recurring = await db.Donations.CountAsync(d => d.AppId == appId && d.Status == DonationStatus.Succeeded
                                                           && d.Interval != DonationInterval.OneTime);
        var pending = await db.Donations.CountAsync(d => d.AppId == appId && d.Status == DonationStatus.Pending);
        return new DonationSummaryDto(total, recent, recurring, pending);
    }
}
