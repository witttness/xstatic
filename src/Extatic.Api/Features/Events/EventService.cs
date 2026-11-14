using Extatic.Api.Common;
using Extatic.Api.Data;
using Extatic.Api.Domain.Entities;
using Extatic.Api.Dtos;
using Extatic.Api.Services;
using Extatic.Api.Validation;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Extatic.Api.Features.Events;

/// <summary>
/// Events and registrations. Every write that depends on seat counts runs in a transaction that first
/// locks the event row (<c>SELECT ... FOR UPDATE</c>), so registrations, cancellations, promotions and
/// capacity changes for one event are serialized.
/// </summary>
public class EventService(AppDbContext db, JsonSchemaValidator validator, EventNotifications notifications)
{
    /// <summary>Overridable clock for tests.</summary>
    public Func<DateTime> Clock { get; set; } = () => DateTime.UtcNow;

    // ---------------------------------------------------------------- events (platform)

    public async Task<(List<Event> Items, int Total)> ListAsync(Guid appId, string? status, int page, int pageSize)
    {
        Paging.Validate(page, pageSize);
        var query = db.Events.AsNoTracking().Where(e => e.AppId == appId);
        if (!string.IsNullOrEmpty(status))
        {
            var parsed = ParseEventStatus(status);
            query = query.Where(e => e.Status == parsed);
        }
        var total = await query.CountAsync();
        var items = await query.OrderByDescending(e => e.StartsAt).ThenBy(e => e.Id)
            .Skip(Paging.Skip(page, pageSize)).Take(pageSize).ToListAsync();
        return (items, total);
    }

    public async Task<Event> GetAsync(Guid appId, Guid id)
        => await db.Events.FirstOrDefaultAsync(e => e.AppId == appId && e.Id == id)
           ?? throw new NotFoundException("Event not found");

    public async Task<Event> CreateAsync(App app, EventRequest req)
    {
        var ev = new Event { AppId = app.Id, Status = EventStatus.Draft };
        EventRules.Apply(ev, req, app);
        if (await db.Events.AnyAsync(e => e.AppId == app.Id && e.Slug == ev.Slug))
            throw new ConflictException($"An event with slug '{ev.Slug}' already exists");
        db.Events.Add(ev);
        await SaveAsync("An event with that slug already exists");
        return ev;
    }

    public async Task<Event> UpdateAsync(App app, Guid id, EventRequest req)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        var ev = await LockAsync(app.Id, id);
        if (ev.Status == EventStatus.Cancelled)
            throw new ConflictException("A cancelled event cannot be edited");
        var oldCapacity = ev.Capacity;
        var oldSlug = ev.Slug;
        EventRules.Apply(ev, req, app);
        if (ev.Slug != oldSlug && await db.Events.AnyAsync(e => e.AppId == app.Id && e.Slug == ev.Slug && e.Id != id))
            throw new ConflictException($"An event with slug '{ev.Slug}' already exists");

        var availability = await AvailabilityAsync(ev);
        if (ev.Capacity is { } cap && cap < availability.ConfirmedSeats)
            throw new ValidationException("Invalid event",
                [new ValidationError("capacity", $"Cannot be below the {availability.ConfirmedSeats} seats already confirmed")]);

        var raised = oldCapacity is not null && (ev.Capacity is null || ev.Capacity > oldCapacity);
        if (raised) await PromoteWaitlistAsync(ev);
        await SaveAsync("An event with that slug already exists");
        await tx.CommitAsync();
        return ev;
    }

    public async Task DeleteAsync(Guid appId, Guid id)
    {
        var ev = await GetAsync(appId, id);
        db.Events.Remove(ev);
        await db.SaveChangesAsync();
    }

    public async Task<Event> PublishAsync(Guid appId, Guid id)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        var ev = await LockAsync(appId, id);
        if (ev.Status == EventStatus.Cancelled)
            throw new ConflictException("A cancelled event cannot be published");
        if (ev.Status == EventStatus.Draft)
        {
            ev.Status = EventStatus.Published;
            await PromoteWaitlistAsync(ev);
            await db.SaveChangesAsync();
        }
        await tx.CommitAsync();
        return ev;
    }

    public async Task<Event> CancelEventAsync(Guid appId, Guid id)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        var ev = await LockAsync(appId, id);
        if (ev.Status == EventStatus.Cancelled)
            throw new ConflictException("Event is already cancelled");
        ev.Status = EventStatus.Cancelled;
        var active = await db.EventRegistrations
            .Where(r => r.AppId == appId && r.EventId == id && r.Status != EventRegistrationStatus.Cancelled)
            .OrderBy(r => r.CreatedAt).ToListAsync();
        await notifications.EventCancelledAsync(db, ev, active);
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return ev;
    }

    // ---------------------------------------------------------------- availability

    public async Task<EventAvailability> AvailabilityAsync(Event ev)
        => (await AvailabilityAsync(ev.AppId, [ev.Id])).GetValueOrDefault(ev.Id, EventAvailability.Empty);

    public async Task<Dictionary<Guid, EventAvailability>> AvailabilityAsync(Guid appId, IReadOnlyCollection<Guid> eventIds)
    {
        var rows = await db.EventRegistrations.AsNoTracking()
            .Where(r => r.AppId == appId && eventIds.Contains(r.EventId))
            .GroupBy(r => new { r.EventId, r.Status })
            .Select(g => new
            {
                g.Key.EventId,
                g.Key.Status,
                Count = g.Count(),
                Seats = g.Sum(r => r.Seats),
                CheckedIn = g.Count(r => r.CheckedInAt != null),
                CheckedInSeats = g.Where(r => r.CheckedInAt != null).Sum(r => (int?)r.Seats) ?? 0
            })
            .ToListAsync();
        return rows.GroupBy(r => r.EventId).ToDictionary(g => g.Key, g =>
        {
            var confirmed = g.FirstOrDefault(x => x.Status == EventRegistrationStatus.Confirmed);
            var waitlisted = g.FirstOrDefault(x => x.Status == EventRegistrationStatus.Waitlisted);
            var cancelled = g.FirstOrDefault(x => x.Status == EventRegistrationStatus.Cancelled);
            return new EventAvailability(
                confirmed?.Count ?? 0, confirmed?.Seats ?? 0,
                waitlisted?.Count ?? 0, waitlisted?.Seats ?? 0,
                cancelled?.Count ?? 0, confirmed?.CheckedIn ?? 0, confirmed?.CheckedInSeats ?? 0);
        });
    }

    // ---------------------------------------------------------------- client reads

    public async Task<(List<Event> Items, int Total)> ListPublishedAsync(Guid appId, bool includePast, int page, int pageSize)
    {
        Paging.Validate(page, pageSize);
        var now = Clock();
        var query = db.Events.AsNoTracking().Where(e => e.AppId == appId && e.Status == EventStatus.Published);
        if (!includePast) query = query.Where(e => e.EndsAt > now);
        var total = await query.CountAsync();
        var items = await query.OrderBy(e => e.StartsAt).ThenBy(e => e.Id)
            .Skip(Paging.Skip(page, pageSize)).Take(pageSize).ToListAsync();
        return (items, total);
    }

    /// <summary>Published or cancelled event by slug (drafts are invisible to the client API → 404).</summary>
    public async Task<Event> GetPublicAsync(Guid appId, string slug)
        => await db.Events.AsNoTracking().FirstOrDefaultAsync(e =>
               e.AppId == appId && e.Slug == slug && e.Status != EventStatus.Draft)
           ?? throw new NotFoundException("Event not found");

    // ---------------------------------------------------------------- registration (client)

    public async Task<(EventRegistration Registration, Event Event, string Token)> RegisterAsync(
        App app, string slug, Guid? appUserId, RegisterRequest req)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        var ev = (await db.Events
                .FromSql($"SELECT * FROM events WHERE \"AppId\" = {app.Id} AND \"Slug\" = {slug} FOR UPDATE")
                .ToListAsync()).SingleOrDefault();
        if (ev is null || ev.Status == EventStatus.Draft) throw new NotFoundException("Event not found");

        var reason = EventRules.WindowClosedReason(ev, Clock());
        if (reason is not null) throw new RegistrationClosedException(reason, EventRules.ReasonMessage(reason));
        if (ev.RequireAppUser && appUserId is null)
            throw new UnauthorizedException("Sign-in required to register for this event");

        var email = req.Email;
        if (string.IsNullOrWhiteSpace(email) && appUserId is not null)
            email = await db.AppUsers.Where(u => u.AppId == app.Id && u.Id == appUserId).Select(u => u.Email).FirstOrDefaultAsync();
        var (name, normalisedEmail, seats, data) =
            EventRules.ValidateRegistrant(ev, req.Name, email, req.Seats, req.Data, validator);

        var result = await AddRegistrationAsync(ev, appUserId, name, normalisedEmail, seats, data, overrideCapacity: false);
        await tx.CommitAsync();
        return (result.Registration, ev, result.Token);
    }

    // ---------------------------------------------------------------- registration (platform)

    public async Task<(EventRegistration Registration, string Token)> AddManualAsync(
        Guid appId, Guid eventId, ManualRegistrationRequest req)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        var ev = await LockAsync(appId, eventId);
        if (ev.Status == EventStatus.Cancelled)
            throw new RegistrationClosedException(RegistrationClosedReasons.Cancelled, "This event has been cancelled");
        var (name, email, seats, data) = EventRules.ValidateRegistrant(
            ev, req.Name, req.Email, req.Seats, req.Data, validator, enforceMaxSeats: !req.OverrideCapacity);
        var result = await AddRegistrationAsync(ev, null, name, email, seats, data, req.OverrideCapacity);
        await tx.CommitAsync();
        return result;
    }

    /// <summary>Must be called with the event row locked inside a transaction.</summary>
    private async Task<(EventRegistration Registration, string Token)> AddRegistrationAsync(
        Event ev, Guid? appUserId, string name, string email, int seats, string data, bool overrideCapacity)
    {
        if (await db.EventRegistrations.AnyAsync(r => r.AppId == ev.AppId && r.EventId == ev.Id
                && r.Email == email && r.Status != EventRegistrationStatus.Cancelled))
            throw new ConflictException("This email address is already registered for this event");

        var availability = await AvailabilityAsync(ev);
        var now = Clock();
        EventRegistrationStatus status;
        if (overrideCapacity || EventRules.CanConfirm(ev, availability, seats)) status = EventRegistrationStatus.Confirmed;
        else if (EventRules.WaitlistHasRoom(ev, availability, seats)) status = EventRegistrationStatus.Waitlisted;
        else throw new RegistrationClosedException(RegistrationClosedReasons.SoldOut, "This event is sold out");

        var token = Tokens.NewUrlToken();
        var registration = new EventRegistration
        {
            AppId = ev.AppId,
            EventId = ev.Id,
            AppUserId = appUserId,
            Name = name,
            Email = email,
            Seats = seats,
            Data = data,
            Status = status,
            ManageTokenHash = Tokens.Hash(token),
            ConfirmedAt = status == EventRegistrationStatus.Confirmed ? now : null
        };
        db.EventRegistrations.Add(registration);
        await SaveAsync("This email address is already registered for this event");
        // Webhook payload needs the database-generated id; emails/webhooks are staged now and saved below,
        // still inside the caller's transaction.
        await notifications.RegisteredAsync(db, ev, registration, token);
        await db.SaveChangesAsync();
        return (registration, token);
    }

    public async Task<(List<EventRegistration> Items, int Total)> ListRegistrationsAsync(
        Guid appId, Guid eventId, string? status, string? search, int page, int pageSize)
    {
        Paging.Validate(page, pageSize);
        await GetAsync(appId, eventId);
        var query = FilteredRegistrations(appId, eventId, status, search);
        var total = await query.CountAsync();
        var items = await query.OrderBy(r => r.CreatedAt).ThenBy(r => r.Id)
            .Skip(Paging.Skip(page, pageSize)).Take(pageSize).ToListAsync();
        return (items, total);
    }

    public async Task<List<EventRegistration>> ExportRegistrationsAsync(Guid appId, Guid eventId, string? status, string? search)
    {
        await GetAsync(appId, eventId);
        return await FilteredRegistrations(appId, eventId, status, search)
            .OrderBy(r => r.CreatedAt).ThenBy(r => r.Id).ToListAsync();
    }

    private IQueryable<EventRegistration> FilteredRegistrations(Guid appId, Guid eventId, string? status, string? search)
    {
        var query = db.EventRegistrations.AsNoTracking().Where(r => r.AppId == appId && r.EventId == eventId);
        if (!string.IsNullOrEmpty(status))
        {
            var parsed = ParseRegistrationStatus(status);
            query = query.Where(r => r.Status == parsed);
        }
        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = "%" + search.Trim().Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_") + "%";
            query = query.Where(r => EF.Functions.ILike(r.Name, pattern, @"\") || EF.Functions.ILike(r.Email, pattern, @"\"));
        }
        return query;
    }

    public async Task<EventRegistration> CancelRegistrationAsync(Guid appId, Guid eventId, Guid registrationId)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        var ev = await LockAsync(appId, eventId);
        var registration = await db.EventRegistrations.FirstOrDefaultAsync(r =>
                               r.AppId == appId && r.EventId == eventId && r.Id == registrationId)
                           ?? throw new NotFoundException("Registration not found");
        if (registration.Status == EventRegistrationStatus.Cancelled)
            throw new ConflictException("Registration is already cancelled");
        await CancelLockedAsync(ev, registration);
        await tx.CommitAsync();
        return registration;
    }

    public async Task<EventRegistration> PromoteAsync(Guid appId, Guid eventId, Guid registrationId, bool overrideCapacity)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        var ev = await LockAsync(appId, eventId);
        if (ev.Status == EventStatus.Cancelled) throw new ConflictException("This event has been cancelled");
        var registration = await db.EventRegistrations.FirstOrDefaultAsync(r =>
                               r.AppId == appId && r.EventId == eventId && r.Id == registrationId)
                           ?? throw new NotFoundException("Registration not found");
        if (registration.Status != EventRegistrationStatus.Waitlisted)
            throw new ConflictException("Only waitlisted registrations can be promoted");
        var availability = await AvailabilityAsync(ev);
        if (!overrideCapacity && ev.Capacity is { } cap && availability.ConfirmedSeats + registration.Seats > cap)
            throw new ConflictException("Not enough seats remaining; use override_capacity to exceed capacity");
        await ConfirmPromotionAsync(ev, registration);
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return registration;
    }

    public async Task<EventRegistration> SetCheckInAsync(Guid appId, Guid eventId, Guid registrationId, bool checkedIn)
    {
        var registration = await db.EventRegistrations.FirstOrDefaultAsync(r =>
                               r.AppId == appId && r.EventId == eventId && r.Id == registrationId)
                           ?? throw new NotFoundException("Registration not found");
        if (checkedIn && registration.Status != EventRegistrationStatus.Confirmed)
            throw new ConflictException("Only confirmed registrations can be checked in");
        registration.CheckedInAt = checkedIn ? registration.CheckedInAt ?? Clock() : null;
        await db.SaveChangesAsync();
        return registration;
    }

    // ---------------------------------------------------------------- registrant self-service

    /// <summary>Looks up a registration by its manage token, scoped to <paramref name="appId"/>.</summary>
    public async Task<(EventRegistration Registration, Event Event)> GetByTokenAsync(Guid appId, string token)
    {
        if (string.IsNullOrEmpty(token) || token.Length > 200) throw new NotFoundException("Registration not found");
        var hash = Tokens.Hash(token);
        var registration = await db.EventRegistrations.AsNoTracking().Include(r => r.Event)
                               .FirstOrDefaultAsync(r => r.AppId == appId && r.ManageTokenHash == hash)
                           ?? throw new NotFoundException("Registration not found");
        return (registration, registration.Event);
    }

    public async Task<(EventRegistration Registration, Event Event)> CancelByTokenAsync(Guid appId, string token)
    {
        var (found, _) = await GetByTokenAsync(appId, token);
        await using var tx = await db.Database.BeginTransactionAsync();
        var ev = await LockAsync(appId, found.EventId);
        var registration = await db.EventRegistrations.FirstAsync(r => r.AppId == appId && r.Id == found.Id);
        if (registration.Status != EventRegistrationStatus.Cancelled)
        {
            if (ev.EndsAt <= Clock()) throw new ConflictException("This event has ended");
            await CancelLockedAsync(ev, registration);
        }
        await tx.CommitAsync();
        return (registration, ev);
    }

    public async Task<(List<EventRegistration> Items, int Total)> ListForAppUserAsync(
        Guid appId, Guid appUserId, int page, int pageSize)
    {
        Paging.Validate(page, pageSize);
        var query = db.EventRegistrations.AsNoTracking().Include(r => r.Event)
            .Where(r => r.AppId == appId && r.AppUserId == appUserId && r.Event.Status != EventStatus.Draft);
        var total = await query.CountAsync();
        var items = await query.OrderByDescending(r => r.Event.StartsAt).ThenBy(r => r.Id)
            .Skip(Paging.Skip(page, pageSize)).Take(pageSize).ToListAsync();
        return (items, total);
    }

    /// <summary>1-based FIFO position among waitlisted registrations, or null if not waitlisted.</summary>
    public async Task<int?> WaitlistPositionAsync(EventRegistration r)
    {
        if (r.Status != EventRegistrationStatus.Waitlisted) return null;
        return 1 + await db.EventRegistrations.CountAsync(x => x.AppId == r.AppId && x.EventId == r.EventId
            && x.Status == EventRegistrationStatus.Waitlisted
            && (x.CreatedAt < r.CreatedAt || (x.CreatedAt == r.CreatedAt && x.Id.CompareTo(r.Id) < 0)));
    }

    // ---------------------------------------------------------------- internals

    private async Task<Event> LockAsync(Guid appId, Guid id)
        => (await db.Events
               .FromSql($"SELECT * FROM events WHERE \"AppId\" = {appId} AND \"Id\" = {id} FOR UPDATE")
               .ToListAsync()).SingleOrDefault()
           ?? throw new NotFoundException("Event not found");

    /// <summary>Event row must be locked. Cancels, notifies, and promotes the waitlist if seats were freed.</summary>
    private async Task CancelLockedAsync(Event ev, EventRegistration registration)
    {
        var wasConfirmed = registration.Status == EventRegistrationStatus.Confirmed;
        registration.Status = EventRegistrationStatus.Cancelled;
        registration.CancelledAt = Clock();
        await notifications.CancelledAsync(db, ev, registration);
        await db.SaveChangesAsync();
        if (wasConfirmed)
        {
            await PromoteWaitlistAsync(ev);
            await db.SaveChangesAsync();
        }
    }

    /// <summary>
    /// Event row must be locked. Promotes waitlisted registrations strictly FIFO (CreatedAt, Id),
    /// stopping at the first that does not fit. No-op for cancelled or ended events.
    /// </summary>
    private async Task PromoteWaitlistAsync(Event ev)
    {
        if (ev.Status == EventStatus.Cancelled || ev.EndsAt <= Clock()) return;
        var confirmedSeats = await db.EventRegistrations
            .Where(r => r.AppId == ev.AppId && r.EventId == ev.Id && r.Status == EventRegistrationStatus.Confirmed)
            .SumAsync(r => (int?)r.Seats) ?? 0;
        var waitlist = await db.EventRegistrations
            .Where(r => r.AppId == ev.AppId && r.EventId == ev.Id && r.Status == EventRegistrationStatus.Waitlisted)
            .OrderBy(r => r.CreatedAt).ThenBy(r => r.Id)
            .ToListAsync();
        foreach (var r in waitlist)
        {
            if (ev.Capacity is { } cap && confirmedSeats + r.Seats > cap) break;
            await ConfirmPromotionAsync(ev, r);
            confirmedSeats += r.Seats;
        }
    }

    private async Task ConfirmPromotionAsync(Event ev, EventRegistration r)
    {
        var now = Clock();
        r.Status = EventRegistrationStatus.Confirmed;
        r.ConfirmedAt = now;
        r.PromotedAt = now;
        await notifications.PromotedAsync(db, ev, r);
    }

    private async Task SaveAsync(string conflictMessage)
    {
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new ConflictException(conflictMessage);
        }
    }

    public static EventStatus ParseEventStatus(string value)
        => Enum.TryParse<EventStatus>(value, ignoreCase: true, out var s) && Enum.IsDefined(s)
            ? s : throw new BadRequestException("status must be draft, published or cancelled");

    public static EventRegistrationStatus ParseRegistrationStatus(string value)
        => Enum.TryParse<EventRegistrationStatus>(value, ignoreCase: true, out var s) && Enum.IsDefined(s)
            ? s : throw new BadRequestException("status must be confirmed, waitlisted or cancelled");
}
