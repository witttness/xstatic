using System.Reflection;
using System.Text;
using System.Text.Json;
using Extatic.Api.Auth;
using Extatic.Api.Features.Scheduling;
using Extatic.Api.Modules;
using Extatic.Api.Services;
using Extatic.Api.Tests.TestSupport;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace Extatic.Api.Tests.Features.Scheduling;

public class SchedulingControllerTests(ApiTestFixture fixture) : IClassFixture<ApiTestFixture>
{
    private static readonly DateTimeOffset Now = new(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTime Mon0900 = new(2026, 3, 2, 9, 0, 0, DateTimeKind.Utc);

    private (ClientSchedulingController Controller, Microsoft.EntityFrameworkCore.DbContext Db) Client(SchedulingTestKit kit,
        Extatic.Api.Domain.Entities.App app, Guid? appUserId = null, Guid? tokenAppId = null)
    {
        var (db, svc, _) = kit.Services();
        var controller = new ClientSchedulingController(svc, db)
        {
            ControllerContext = TestContexts.Client(app, appUserId, tokenAppId: tokenAppId)
        };
        return (controller, db);
    }

    [Fact]
    public async Task ClientBooking_Returns201WithTokenOnce_ThenTokenEndpointsWork()
    {
        var kit = new SchedulingTestKit(fixture, Now);
        var (app, _, _) = await kit.SeedAsync();
        var (controller, _) = Client(kit, app);

        var types = (await controller.ListTypes()).Value!;
        Assert.Equal("consult", Assert.Single(types).Slug);
        var slots = (await controller.Slots("consult", new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 3), null)).Value!;
        Assert.Equal(16, slots.Days.Single().Slots.Count);

        var result = await controller.Book("consult", new BookAppointmentRequest(Mon0900, "Casey", "casey@example.com", null, "Europe/Berlin",
            JsonSerializer.SerializeToElement(new { note = "hi" })));
        var created = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(201, created.StatusCode);
        var body = Assert.IsType<BookAppointmentResponse>(created.Value);
        Assert.Equal("Confirmed", body.Appointment.Status);
        Assert.Equal(43, body.ManageToken.Length);
        Assert.True(body.Appointment.CanCancel);

        var (c2, _) = Client(kit, app);
        var fetched = (await c2.GetAppointment(body.ManageToken)).Value!;
        Assert.Equal(body.Appointment.Id, fetched.Id);
        var json = JsonSerializer.Serialize(fetched);
        Assert.DoesNotContain("Notes", json);
        Assert.DoesNotContain("token", json, StringComparison.OrdinalIgnoreCase);

        var ics = Assert.IsType<FileContentResult>(await c2.Ics(body.ManageToken));
        Assert.Equal("text/calendar; charset=utf-8", ics.ContentType);
        Assert.Contains("DTSTART:20260302T090000Z", Encoding.UTF8.GetString(ics.FileContents));

        var rescheduled = (await c2.Reschedule(body.ManageToken, new RescheduleAppointmentRequest(Mon0900.AddHours(1)))).Value!;
        Assert.Equal(Mon0900.AddHours(1), rescheduled.StartsAt);
        var cancelled = (await c2.Cancel(body.ManageToken, new AppointmentReasonRequest("bye"))).Value!;
        Assert.Equal("Cancelled", cancelled.Status);
        Assert.False(cancelled.CanReschedule);
    }

    [Fact]
    public async Task ClientTypes_HideInactiveTypesAndCalendars()
    {
        var kit = new SchedulingTestKit(fixture, Now);
        var (app, calendar, _) = await kit.SeedAsync();
        await kit.AddTypeAsync(app, calendar, "hidden", t => t.IsActive = false);
        var (controller, _) = Client(kit, app);
        Assert.Single((await controller.ListTypes()).Value!);
        await Assert.ThrowsAsync<NotFoundException>(() => controller.GetAppointmentType("hidden"));

        await using (var db = fixture.CreateContext())
            await db.BookingCalendars.Where(c => c.Id == calendar.Id).ExecuteUpdateAsync(s => s.SetProperty(c => c.IsActive, false));
        var (c2, _) = Client(kit, app);
        Assert.Empty((await c2.ListTypes()).Value!);
        await Assert.ThrowsAsync<NotFoundException>(() => c2.Book("consult", new BookAppointmentRequest(Mon0900, "a", "a@example.com", null, null, null)));
    }

    [Fact]
    public async Task SignedInCustomer_IsLinked_ProfileFillsMissingFields_AndSeesOwnAppointments()
    {
        var kit = new SchedulingTestKit(fixture, Now);
        var (app, _, _) = await kit.SeedAsync();
        var user = await fixture.SeedAppUserAsync(app.Id);
        var (controller, _) = Client(kit, app, user.Id);

        var created = (BookAppointmentResponse)((ObjectResult)(await controller.Book("consult",
            new BookAppointmentRequest(Mon0900, "Sam", null, null, null, null))).Result!).Value!;
        Assert.Equal(user.Email!.ToLowerInvariant(), created.Appointment.CustomerEmail);

        // Anonymous booking is not listed for the user.
        var (anon, _) = Client(kit, app);
        await anon.Book("consult", new BookAppointmentRequest(Mon0900.AddHours(1), "Other", "other@example.com", null, null, null));

        var (c2, _) = Client(kit, app, user.Id);
        var mine = (await c2.MyAppointments()).Value!;
        Assert.Equal(created.Appointment.Id, Assert.Single(mine).Id);

        // A token issued for another App is anonymous here: not linked, and me/ requires sign-in.
        var (foreign, _) = Client(kit, app, user.Id, tokenAppId: Guid.NewGuid());
        await Assert.ThrowsAsync<UnauthorizedException>(() => foreign.MyAppointments());
        await using var db = fixture.CreateContext();
        Assert.Equal(1, await db.Appointments.CountAsync(a => a.AppId == app.Id && a.AppUserId == user.Id));
    }

    [Fact]
    public async Task PlatformControllers_Return404ForAnotherAppsResources()
    {
        var kit = new SchedulingTestKit(fixture, Now);
        var (app, calendar, type) = await kit.SeedAsync();
        var (otherApp, _, _) = await kit.SeedAsync();
        var (db, svc, config) = kit.Services();
        var booked = await svc.BookAsync(await config.GetTypeAsync(app.Id, type.Id), SchedulingTestKit.Input(Mon0900), null);
        var ctx = TestContexts.Platform(otherApp, otherApp.OwnerId);

        var calendars = new SchedulingCalendarsController(config, svc) { ControllerContext = ctx };
        var types = new SchedulingTypesController(config) { ControllerContext = ctx };
        var appointments = new SchedulingAppointmentsController(svc, config) { ControllerContext = ctx };

        await Assert.ThrowsAsync<NotFoundException>(() => calendars.Get(calendar.Id));
        await Assert.ThrowsAsync<NotFoundException>(() => calendars.PreviewSlots(calendar.Id, new(2026, 3, 2), new(2026, 3, 3), null));
        await Assert.ThrowsAsync<NotFoundException>(() => types.Get(type.Id));
        await Assert.ThrowsAsync<NotFoundException>(() => appointments.Get(booked.Appointment.Id));
        await Assert.ThrowsAsync<NotFoundException>(() => appointments.Cancel(booked.Appointment.Id, null));
        await Assert.ThrowsAsync<NotFoundException>(() => appointments.Create(new ManualBookingRequest(type.Id, Mon0900.AddHours(2), "a", "a@example.com", null, null, null, null, null)));
        Assert.DoesNotContain((await calendars.List()).Value!, c => c.Id == calendar.Id);
        Assert.Equal(0, (await appointments.List(null, null, null, null, null)).Total);

        // Own App: preview slots for the calendar (raw availability and per type).
        var own = new SchedulingCalendarsController(config, svc) { ControllerContext = TestContexts.Platform(app, app.OwnerId) };
        var preview = (await own.PreviewSlots(calendar.Id, new(2026, 3, 2), new(2026, 3, 3), null, 60)).Value!;
        Assert.Equal(7, preview.Days.Single().Slots.Count); // 09–17 hourly, 09:00 taken
        var perType = (await own.PreviewSlots(calendar.Id, new(2026, 3, 2), new(2026, 3, 3), type.Id)).Value!;
        Assert.Equal(15, perType.Days.Single().Slots.Count);
    }

    [Fact]
    public void Endpoints_DeclareExpectedPoliciesAndRateLimits()
    {
        static string[] Policies(MemberInfo m) => m.GetCustomAttributes<AuthorizeAttribute>().Select(a => a.Policy!).ToArray();

        foreach (var controller in new[] { typeof(SchedulingCalendarsController), typeof(SchedulingTypesController), typeof(SchedulingAppointmentsController) })
        {
            Assert.Equal([PolicyNames.AppAnyAccess], Policies(controller));
            Assert.StartsWith("api/apps/{app_slug}/scheduling/", controller.GetCustomAttribute<RouteAttribute>()!.Template);
            foreach (var action in controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                var isRead = action.GetCustomAttribute<HttpGetAttribute>() is not null && action.Name != "Export";
                var expected = controller == typeof(SchedulingAppointmentsController) ? PolicyNames.AppOwnerOrEditor : PolicyNames.AppOwnerOrAdmin;
                if (isRead) Assert.Empty(Policies(action));
                else Assert.Equal([expected], Policies(action));
            }
        }

        var client = typeof(ClientSchedulingController);
        Assert.Equal([PolicyNames.ClientAnonymousOrAppUser], Policies(client));
        foreach (var action in client.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
        {
            var limit = action.GetCustomAttribute<EnableRateLimitingAttribute>()!.PolicyName;
            var isWrite = action.GetCustomAttribute<HttpPostAttribute>() is not null;
            Assert.Equal(isWrite ? RateLimitPolicyNames.ClientAnonymousWrites : RateLimitPolicyNames.ClientReads, limit);
        }
        Assert.Equal([PolicyNames.AuthenticatedAppUser], Policies(client.GetMethod(nameof(ClientSchedulingController.MyAppointments))!));
    }

    [Fact]
    public void Module_DeclaresWebhookEvents()
    {
        var module = FeatureModuleExtensions.DiscoverModules(typeof(SchedulingModule).Assembly).OfType<SchedulingModule>().Single();
        Assert.Equal("scheduling", module.Key);
        Assert.Equal(["appointment.booked", "appointment.confirmed", "appointment.cancelled", "appointment.rescheduled", "appointment.completed"],
            module.WebhookEvents);
    }
}
