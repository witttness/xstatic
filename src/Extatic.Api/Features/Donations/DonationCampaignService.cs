using System.Net.Mail;
using Extatic.Api.Common;
using Extatic.Api.Data;
using Extatic.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace Extatic.Api.Features.Donations;

public class DonationCampaignService(AppDbContext db)
{
    /// <summary>Stripe's maximum charge amount (8 digits in minor units).</summary>
    public const long ProviderMaxAmountMinor = 99_999_999;
    public const int MaxSuggestedAmounts = 10;
    public const int MaxNotifyEmails = 10;
    public const int RecentDonorLimit = 10;

    private static readonly DonationStatus[] CountedStatuses = [DonationStatus.Succeeded, DonationStatus.Refunded];

    // ---- Platform ------------------------------------------------------------------------------

    public async Task<List<CampaignDto>> ListAsync(Guid appId)
    {
        var campaigns = await db.DonationCampaigns.AsNoTracking()
            .Where(c => c.AppId == appId)
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync();
        var totals = await TotalsAsync(appId, campaigns.Select(c => c.Id).ToList());
        return campaigns.Select(c => c.ToDto(totals.GetValueOrDefault(c.Id, CampaignTotals.Zero))).ToList();
    }

    public async Task<CampaignDto> GetDtoAsync(Guid appId, Guid id)
    {
        var campaign = await GetAsync(appId, id);
        var totals = await TotalsAsync(appId, [campaign.Id]);
        return campaign.ToDto(totals.GetValueOrDefault(campaign.Id, CampaignTotals.Zero));
    }

    public async Task<DonationCampaign> GetAsync(Guid appId, Guid id)
        => await db.DonationCampaigns.SingleOrDefaultAsync(c => c.AppId == appId && c.Id == id)
           ?? throw new NotFoundException("Campaign not found");

    public async Task<CampaignDto> CreateAsync(Guid appId, CampaignRequest req)
    {
        var campaign = new DonationCampaign { AppId = appId };
        Apply(campaign, req);
        db.DonationCampaigns.Add(campaign);
        await SaveAsync();
        return campaign.ToDto(CampaignTotals.Zero);
    }

    public async Task<CampaignDto> UpdateAsync(Guid appId, Guid id, CampaignRequest req)
    {
        var campaign = await GetAsync(appId, id);
        Apply(campaign, req);
        await SaveAsync();
        return await GetDtoAsync(appId, id);
    }

    public async Task DeleteAsync(Guid appId, Guid id)
    {
        var campaign = await GetAsync(appId, id);
        db.DonationCampaigns.Remove(campaign); // donations keep their history (CampaignId → null)
        await db.SaveChangesAsync();
    }

    private async Task SaveAsync()
    {
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (DonationDb.IsUniqueViolation(ex))
        {
            throw new ConflictException("A campaign with this slug already exists");
        }
    }

    /// <summary>Validates and copies a request onto <paramref name="c"/>. Throws ValidationException (422).</summary>
    public static void Apply(DonationCampaign c, CampaignRequest req)
    {
        var errors = new List<ValidationError>();
        var name = req.Name?.Trim();
        if (string.IsNullOrEmpty(name) || name.Length > 200) errors.Add(new("name", "Required, at most 200 characters"));
        if (!Slugs.IsValid(req.Slug)) errors.Add(new("slug", "Must be 1-100 characters: lowercase letters, digits and hyphens"));
        var description = string.IsNullOrWhiteSpace(req.Description) ? null : req.Description.Trim();
        if (description?.Length > 5000) errors.Add(new("description", "At most 5000 characters"));

        var currency = req.Currency?.Trim().ToLowerInvariant();
        if (!Currencies.IsSupported(currency)) errors.Add(new("currency", "Unsupported currency (ISO 4217 code, e.g. usd, eur, jpy)"));

        var min = req.MinAmountMinor ?? 1;
        var max = req.MaxAmountMinor ?? ProviderMaxAmountMinor;
        if (min < 1) errors.Add(new("min_amount_minor", "Must be at least 1"));
        if (max > ProviderMaxAmountMinor) errors.Add(new("max_amount_minor", $"At most {ProviderMaxAmountMinor}"));
        if (max < min) errors.Add(new("max_amount_minor", "Must be greater than or equal to min_amount_minor"));
        if (req.GoalAmountMinor is < 1) errors.Add(new("goal_amount_minor", "Must be positive"));

        var suggested = (req.SuggestedAmountsMinor ?? []).Distinct().Order().ToArray();
        if (suggested.Length > MaxSuggestedAmounts) errors.Add(new("suggested_amounts_minor", $"At most {MaxSuggestedAmounts} amounts"));
        if (suggested.Any(a => a < min || a > max))
            errors.Add(new("suggested_amounts_minor", "Every suggested amount must be within min and max"));
        var allowCustom = req.AllowCustomAmount ?? true;
        if (!allowCustom && suggested.Length == 0)
            errors.Add(new("suggested_amounts_minor", "Required when custom amounts are not allowed"));

        if (!RedirectUrls.IsValid(req.SuccessUrl)) errors.Add(new("success_url", "Must be an absolute https URL (http only for localhost)"));
        if (!RedirectUrls.IsValid(req.CancelUrl)) errors.Add(new("cancel_url", "Must be an absolute https URL (http only for localhost)"));

        var notify = (req.NotifyEmails ?? []).Select(e => e?.Trim() ?? "").Where(e => e.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (notify.Length > MaxNotifyEmails) errors.Add(new("notify_emails", $"At most {MaxNotifyEmails} addresses"));
        for (var i = 0; i < notify.Length; i++)
            if (!IsEmail(notify[i])) errors.Add(new($"notify_emails/{i}", "Invalid email address"));

        var startsAt = req.StartsAt?.ToUniversalTime();
        var endsAt = req.EndsAt?.ToUniversalTime();
        if (startsAt is not null && endsAt is not null && endsAt <= startsAt)
            errors.Add(new("ends_at", "Must be after starts_at"));

        if (errors.Count > 0) throw new ValidationException("Invalid campaign", errors);

        c.Name = name!;
        c.Slug = req.Slug;
        c.Description = description;
        c.Currency = currency!;
        c.GoalAmountMinor = req.GoalAmountMinor;
        c.SuggestedAmountsMinor = suggested;
        c.MinAmountMinor = min;
        c.MaxAmountMinor = max;
        c.AllowCustomAmount = allowCustom;
        c.AllowRecurring = req.AllowRecurring ?? false;
        c.SuccessUrl = req.SuccessUrl;
        c.CancelUrl = req.CancelUrl;
        c.ShowDonorList = req.ShowDonorList ?? false;
        c.NotifyEmails = notify;
        c.IsActive = req.IsActive ?? true;
        c.StartsAt = startsAt;
        c.EndsAt = endsAt;
    }

    internal static bool IsEmail(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 320 || value.Any(char.IsControl)) return false;
        try
        {
            var parsed = new MailAddress(value);
            return parsed.Address == value && parsed.DisplayName.Length == 0 && value.IndexOf('.', value.IndexOf('@')) > 0;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    // ---- Totals (computed, never stored) -------------------------------------------------------

    /// <summary>Net raised (succeeded minus refunded) and donor count per campaign.</summary>
    public async Task<Dictionary<Guid, CampaignTotals>> TotalsAsync(Guid appId, IReadOnlyCollection<Guid> campaignIds)
    {
        if (campaignIds.Count == 0) return [];
        var rows = await db.Donations.AsNoTracking()
            .Where(d => d.AppId == appId && d.CampaignId != null && campaignIds.Contains(d.CampaignId.Value)
                        && CountedStatuses.Contains(d.Status))
            .GroupBy(d => d.CampaignId!.Value)
            .Select(g => new
            {
                CampaignId = g.Key,
                Raised = g.Sum(d => d.AmountMinor - d.RefundedAmountMinor),
                Donors = g.Count(d => d.AmountMinor > d.RefundedAmountMinor)
            })
            .ToListAsync();
        return rows.ToDictionary(r => r.CampaignId, r => new CampaignTotals(r.Raised, r.Donors));
    }

    // ---- Client --------------------------------------------------------------------------------

    private IQueryable<DonationCampaign> OpenCampaigns(Guid appId, DateTime now)
        => db.DonationCampaigns.AsNoTracking()
            .Where(c => c.AppId == appId && c.IsActive
                        && (c.StartsAt == null || c.StartsAt <= now)
                        && (c.EndsAt == null || c.EndsAt > now));

    /// <summary>Active campaign within its date window, or 404.</summary>
    public async Task<DonationCampaign> GetOpenBySlugAsync(Guid appId, string slug, DateTime now)
        => await OpenCampaigns(appId, now).SingleOrDefaultAsync(c => c.Slug == slug)
           ?? throw new NotFoundException("Campaign not found");

    /// <summary>The only open campaign, for checkouts that omit campaign_slug.</summary>
    public async Task<DonationCampaign> GetDefaultOpenAsync(Guid appId, DateTime now)
    {
        var open = await OpenCampaigns(appId, now).Take(2).ToListAsync();
        return open.Count switch
        {
            1 => open[0],
            0 => throw new NotFoundException("No open campaign"),
            _ => throw new ValidationException("campaign_slug is required",
                [new ValidationError("campaign_slug", "Required when the app has more than one open campaign")])
        };
    }

    public async Task<List<ClientCampaignDto>> ListOpenAsync(Guid appId, DateTime now)
    {
        var campaigns = await OpenCampaigns(appId, now).OrderBy(c => c.Name).ToListAsync();
        var totals = await TotalsAsync(appId, campaigns.Select(c => c.Id).ToList());
        var result = new List<ClientCampaignDto>();
        foreach (var c in campaigns)
            result.Add(ToClient(c, totals.GetValueOrDefault(c.Id, CampaignTotals.Zero),
                c.ShowDonorList ? await RecentDonorsAsync(appId, c.Id) : null));
        return result;
    }

    public async Task<ClientCampaignDto> GetOpenDtoAsync(Guid appId, string slug, DateTime now)
    {
        var c = await GetOpenBySlugAsync(appId, slug, now);
        var totals = await TotalsAsync(appId, [c.Id]);
        return ToClient(c, totals.GetValueOrDefault(c.Id, CampaignTotals.Zero),
            c.ShowDonorList ? await RecentDonorsAsync(appId, c.Id) : null);
    }

    /// <summary>Recent counted donations: name or "Anonymous", net amount, message. Never email.</summary>
    public async Task<List<RecentDonorDto>> RecentDonorsAsync(Guid appId, Guid campaignId)
    {
        var rows = await db.Donations.AsNoTracking()
            .Where(d => d.AppId == appId && d.CampaignId == campaignId && CountedStatuses.Contains(d.Status)
                        && d.AmountMinor > d.RefundedAmountMinor)
            .OrderByDescending(d => d.CompletedAt)
            .Take(RecentDonorLimit)
            .Select(d => new { d.DonorName, d.IsAnonymous, d.AmountMinor, d.RefundedAmountMinor, d.Currency, d.Message, d.CompletedAt, d.CreatedAt })
            .ToListAsync();
        return rows.Select(r => new RecentDonorDto(
            r.IsAnonymous || string.IsNullOrWhiteSpace(r.DonorName) ? "Anonymous" : r.DonorName!,
            r.AmountMinor - r.RefundedAmountMinor, r.Currency, r.Message, r.CompletedAt ?? r.CreatedAt)).ToList();
    }

    private static ClientCampaignDto ToClient(DonationCampaign c, CampaignTotals totals, List<RecentDonorDto>? donors) => new(
        c.Slug, c.Name, c.Description, c.Currency, c.GoalAmountMinor, totals.RaisedMinor, totals.DonorCount,
        c.SuggestedAmountsMinor, c.MinAmountMinor, c.MaxAmountMinor, c.AllowCustomAmount, c.AllowRecurring,
        c.StartsAt, c.EndsAt, donors);
}
