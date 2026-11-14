using Extatic.Api.Data;

namespace Extatic.Api.Notifications;

public interface IEmailQueue
{
    /// <summary>
    /// Validates <paramref name="message"/> and adds an outbox row to <paramref name="db"/> WITHOUT
    /// saving. The caller's SaveChanges (and transaction, if any) makes the email atomic with the
    /// business change. Throws <see cref="Services.ValidationException"/> for invalid addresses.
    /// </summary>
    EmailOutboxMessage Stage(AppDbContext db, Guid appId, EmailMessage message);
}
