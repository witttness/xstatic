using System.Net;
using System.Text.Json;
using Extatic.Api.Data;
using Extatic.Api.Notifications;
using Extatic.Api.Webhooks;
using Microsoft.EntityFrameworkCore;

namespace Extatic.Api.Features.Donations;

/// <summary>
/// Stages emails and outgoing webhooks for donation state changes. Everything is only added to
/// <c>db</c>; the caller's SaveChanges / transaction makes it atomic with the donation update.
/// </summary>
public class DonationNotifier(IEmailQueue emailQueue, WebhookDispatcher webhookDispatcher)
{
    public const string ReceiptCategory = "donations.receipt";
    public const string NotificationCategory = "donations.notification";
    public const string EventSucceeded = "donation.succeeded";
    public const string EventFailed = "donation.failed";
    public const string EventRefunded = "donation.refunded";

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    public async Task StageSucceededAsync(AppDbContext db, Donation donation)
    {
        var appName = await db.Apps.Where(a => a.Id == donation.AppId).Select(a => a.Name).SingleAsync();
        var campaign = donation.CampaignId is null ? null
            : await db.DonationCampaigns.AsNoTracking()
                .SingleOrDefaultAsync(c => c.AppId == donation.AppId && c.Id == donation.CampaignId);
        var amount = Currencies.Display(donation.AmountMinor, donation.Currency);
        var cause = campaign?.Name ?? appName;
        var recurring = donation.Interval switch
        {
            DonationInterval.Monthly => " every month",
            DonationInterval.Yearly => " every year",
            _ => ""
        };

        if (!string.IsNullOrEmpty(donation.DonorEmail))
        {
            var greeting = string.IsNullOrWhiteSpace(donation.DonorName) ? "Hello" : $"Hello {donation.DonorName}";
            var text = $"{greeting},\n\nThank you for your donation of {amount}{recurring} to {cause}.\n\n"
                       + $"Reference: {donation.Id}\nDate: {donation.CompletedAt:yyyy-MM-dd HH:mm} UTC\n\n"
                       + "Your payment was processed by our payment provider; this email is your confirmation.\n";
            var html = $"<p>{Enc(greeting)},</p><p>Thank you for your donation of <strong>{Enc(amount)}</strong>{Enc(recurring)} to {Enc(cause)}.</p>"
                       + $"<p>Reference: {donation.Id}<br>Date: {donation.CompletedAt:yyyy-MM-dd HH:mm} UTC</p>"
                       + "<p>Your payment was processed by our payment provider; this email is your confirmation.</p>";
            emailQueue.Stage(db, donation.AppId, new EmailMessage(
                donation.DonorEmail, $"Thank you for your donation to {cause}", text, html, null, ReceiptCategory));
        }

        if (campaign is not null && campaign.NotifyEmails.Length > 0)
        {
            var donor = donation.IsAnonymous ? "An anonymous donor"
                : string.IsNullOrWhiteSpace(donation.DonorName) ? "A donor" : donation.DonorName!;
            var contact = !donation.IsAnonymous && donation.DonorEmail is not null ? $" ({donation.DonorEmail})" : "";
            var message = string.IsNullOrWhiteSpace(donation.Message) ? "" : $"\n\nMessage:\n{donation.Message}";
            var text = $"{donor}{contact} donated {amount}{recurring} to \"{campaign.Name}\".{message}\n\nReference: {donation.Id}\n";
            var html = $"<p>{Enc(donor)}{Enc(contact)} donated <strong>{Enc(amount)}</strong>{Enc(recurring)} to &quot;{Enc(campaign.Name)}&quot;.</p>"
                       + (string.IsNullOrWhiteSpace(donation.Message) ? "" : $"<blockquote>{Enc(donation.Message)}</blockquote>")
                       + $"<p>Reference: {donation.Id}</p>";
            foreach (var to in campaign.NotifyEmails)
                emailQueue.Stage(db, donation.AppId, new EmailMessage(
                    to, $"New donation: {amount} to {campaign.Name}", text, html, null, NotificationCategory));
        }

        await StageWebhookAsync(db, donation, EventSucceeded, campaign?.Slug);
    }

    public Task StageFailedAsync(AppDbContext db, Donation donation)
        => StageWebhookAsync(db, donation, EventFailed, null);

    public Task StageRefundedAsync(AppDbContext db, Donation donation)
        => StageWebhookAsync(db, donation, EventRefunded, null);

    private async Task StageWebhookAsync(AppDbContext db, Donation d, string eventType, string? campaignSlug)
    {
        campaignSlug ??= d.CampaignId is null ? null
            : await db.DonationCampaigns.Where(c => c.AppId == d.AppId && c.Id == d.CampaignId)
                .Select(c => c.Slug).SingleOrDefaultAsync();
        var data = JsonSerializer.SerializeToElement(new
        {
            Id = d.Id,
            CampaignId = d.CampaignId,
            CampaignSlug = campaignSlug,
            AmountMinor = d.AmountMinor,
            RefundedAmountMinor = d.RefundedAmountMinor,
            Currency = d.Currency,
            Interval = d.Interval.ToApi(),
            Status = d.Status.ToApi(),
            DonorName = d.IsAnonymous ? null : d.DonorName,
            DonorEmail = d.DonorEmail,
            IsAnonymous = d.IsAnonymous,
            Message = d.Message,
            AppUserId = d.AppUserId,
            Provider = d.Provider,
            CompletedAt = d.CompletedAt
        }, JsonOptions);
        await webhookDispatcher.StageAsync(db, d.AppId, eventType, data);
    }

    private static string Enc(string? value) => WebUtility.HtmlEncode(value ?? "");
}
