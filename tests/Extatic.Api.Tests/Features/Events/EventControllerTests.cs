using System.Reflection;
using System.Text;
using Extatic.Api.Auth;
using Extatic.Api.Dtos;
using Extatic.Api.Features.Events;
using Extatic.Api.Services;
using Extatic.Api.Tests.TestSupport;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.RateLimiting;
using static Extatic.Api.Tests.Features.Events.EventTestHelper;

namespace Extatic.Api.Tests.Features.Events;

public class EventControllerTests(ApiTestFixture fixture) : IClassFixture<ApiTestFixture>
{
    private ClientEventsController Client(Domain.Entities.App app, Data.AppDbContext db, Guid? appUserId = null,
        Guid? tokenAppId = null)
        => new(Service(fixture, db)) { ControllerContext = TestContexts.Client(app, appUserId, tokenAppId: tokenAppId) };

    private PlatformEventsController Platform(Domain.Entities.App app, Data.AppDbContext db)
        => new(Service(fixture, db)) { ControllerContext = TestContexts.Platform(app, app.OwnerId) };

    [Fact]
    public async Task Client_Register_Returns201WithTokenOnce_AndViewHidesOnlineUrlUntilConfirmed()
    {
        var (_, app, _) = await fixture.SeedAppAsync();
        var ev = await SeedEventAsync(fixture, app, Request(capacity: 1, waitlist: true));
        await using var db = fixture.CreateContext();
        var controller = Client(app, db);

        var result = await controller.Register(ev.Slug, new RegisterRequest("Ann", "ann@example.com", 1, null));
        var created = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(201, created.StatusCode);
        var body = Assert.IsType<RegistrationCreatedDto>(created.Value);
        Assert.Equal("confirmed", body.Registration.Status);
        Assert.Equal("https://meet.example.com/abc", body.Registration.OnlineUrl);

        var waitlisted = (RegistrationCreatedDto)((ObjectResult)(await controller.Register(ev.Slug,
            new RegisterRequest("Bob", "bob@example.com", 1, null))).Result!).Value!;
        Assert.Equal("waitlisted", waitlisted.Registration.Status);
        Assert.Equal(1, waitlisted.Registration.WaitlistPosition);
        Assert.Null(waitlisted.Registration.OnlineUrl);

        var view = Assert.IsType<OkObjectResult>((await controller.GetByToken(body.ManageToken)).Result);
        var dto = Assert.IsType<ClientRegistrationDto>(view.Value);
        Assert.Equal("ann@example.com", dto.Email);

        var publicEvent = (ClientEventDto)((OkObjectResult)(await controller.Get(ev.Slug)).Result!).Value!;
        Assert.Equal(0, publicEvent.SeatsRemaining);
        Assert.True(publicEvent.IsOnline);
        Assert.True(publicEvent.WaitlistOpen);
        Assert.True(publicEvent.RegistrationOpen);
    }

    [Fact]
    public async Task Client_ListsPublishedUpcoming_WithReasonCodes()
    {
        var (_, app, _) = await fixture.SeedAppAsync();
        await SeedEventAsync(fixture, app, Request(slug: "soon", capacity: 1));
        await SeedEventAsync(fixture, app, Request(slug: "draft"), publish: false);
        await SeedEventAsync(fixture, app, Request(slug: "past", configure: r =>
        {
            r.StartsAt = DateTimeOffset.UtcNow.AddDays(-3);
            r.EndsAt = DateTimeOffset.UtcNow.AddDays(-2);
        }));
        await RegisterAsync(fixture, app, "soon", "a@example.com");
        await using var db = fixture.CreateContext();
        var controller = Client(app, db);

        var upcoming = (PagedResult<ClientEventDto>)((OkObjectResult)(await controller.List()).Result!).Value!;
        var only = Assert.Single(upcoming.Items);
        Assert.Equal("soon", only.Slug);
        Assert.False(only.RegistrationOpen);
        Assert.Equal(RegistrationClosedReasons.SoldOut, only.RegistrationClosedReason);

        var all = (PagedResult<ClientEventDto>)((OkObjectResult)(await controller.List(include_past: true)).Result!).Value!;
        Assert.Equal(2, all.Total);
        Assert.Equal(RegistrationClosedReasons.Ended, all.Items.Single(e => e.Slug == "past").RegistrationClosedReason);
        await Assert.ThrowsAsync<NotFoundException>(() => controller.Get("draft"));
    }

    [Fact]
    public async Task Client_CrossTenant_EventsAndTokensInvisible()
    {
        var (_, app, _) = await fixture.SeedAppAsync();
        var (_, other, _) = await fixture.SeedAppAsync();
        var ev = await SeedEventAsync(fixture, app);
        var (_, token) = await RegisterAsync(fixture, app, ev.Slug, "a@example.com");
        await using var db = fixture.CreateContext();
        var foreign = Client(other, db);
        await Assert.ThrowsAsync<NotFoundException>(() => foreign.Get(ev.Slug));
        await Assert.ThrowsAsync<NotFoundException>(() => foreign.Calendar(ev.Slug));
        await Assert.ThrowsAsync<NotFoundException>(() => foreign.GetByToken(token));
        await Assert.ThrowsAsync<NotFoundException>(() => foreign.CancelByToken(token));
        await Assert.ThrowsAsync<NotFoundException>(() =>
            foreign.Register(ev.Slug, new RegisterRequest("X", "x@example.com", 1, null)));
    }

    [Fact]
    public async Task Client_Me_TokenForOtherAppIsUnauthorized()
    {
        var (_, app, _) = await fixture.SeedAppAsync();
        var user = await fixture.SeedAppUserAsync(app.Id);
        var ev = await SeedEventAsync(fixture, app);
        await RegisterAsync(fixture, app, ev.Slug, "u@example.com", appUserId: user.Id);
        await using var db = fixture.CreateContext();

        var mine = (PagedResult<ClientRegistrationDto>)((OkObjectResult)(await Client(app, db, user.Id).Mine()).Result!).Value!;
        Assert.Single(mine.Items);
        await Assert.ThrowsAsync<UnauthorizedException>(() => Client(app, db, user.Id, tokenAppId: Guid.NewGuid()).Mine());
    }

    [Fact]
    public async Task Platform_CrossTenant_Returns404()
    {
        var (_, app, _) = await fixture.SeedAppAsync();
        var (_, other, _) = await fixture.SeedAppAsync();
        var ev = await SeedEventAsync(fixture, app);
        var (reg, _) = await RegisterAsync(fixture, app, ev.Slug, "a@example.com");
        await using var db = fixture.CreateContext();
        var foreign = Platform(other, db);

        await Assert.ThrowsAsync<NotFoundException>(() => foreign.Get(ev.Id));
        await Assert.ThrowsAsync<NotFoundException>(() => foreign.Update(ev.Id, Request()));
        await Assert.ThrowsAsync<NotFoundException>(() => foreign.Delete(ev.Id));
        await Assert.ThrowsAsync<NotFoundException>(() => foreign.Publish(ev.Id));
        await Assert.ThrowsAsync<NotFoundException>(() => foreign.Cancel(ev.Id));
        await Assert.ThrowsAsync<NotFoundException>(() => foreign.Stats(ev.Id));
        await Assert.ThrowsAsync<NotFoundException>(() => foreign.Registrations(ev.Id));
        await Assert.ThrowsAsync<NotFoundException>(() => foreign.Export(ev.Id));
        await Assert.ThrowsAsync<NotFoundException>(() => foreign.AddRegistration(ev.Id, new("X", "x@example.com", 1, null)));
        await Assert.ThrowsAsync<NotFoundException>(() => foreign.CancelRegistration(ev.Id, reg.Id));
        await Assert.ThrowsAsync<NotFoundException>(() => foreign.Promote(ev.Id, reg.Id, null));
        await Assert.ThrowsAsync<NotFoundException>(() => foreign.CheckIn(ev.Id, reg.Id, new(true)));
        var list = (PagedResult<EventDto>)((OkObjectResult)(await foreign.List()).Result!).Value!;
        Assert.Empty(list.Items);

        // Registration id from another event of the same app → 404 too.
        var ev2 = await SeedEventAsync(fixture, app, Request(slug: "other"));
        await Assert.ThrowsAsync<NotFoundException>(() => Platform(app, db).CancelRegistration(ev2.Id, reg.Id));
        Assert.Equal(EventRegistrationStatus.Confirmed, (await ReloadAsync(fixture, reg.Id)).Status);
    }

    [Fact]
    public async Task Platform_ListSearchStatsAndCsv()
    {
        var (_, app, _) = await fixture.SeedAppAsync();
        var ev = await SeedEventAsync(fixture, app, Request(capacity: 1, waitlist: true));
        await RegisterAsync(fixture, app, ev.Slug, "alice@example.com");
        await RegisterAsync(fixture, app, ev.Slug, "=bob@example.com");
        await using var db = fixture.CreateContext();
        var controller = Platform(app, db);

        var events = (PagedResult<EventDto>)((OkObjectResult)(await controller.List()).Result!).Value!;
        var dto = Assert.Single(events.Items);
        Assert.Equal(1, dto.Stats.ConfirmedSeats);
        Assert.Equal(1, dto.Stats.WaitlistedRegistrations);

        var waitlisted = (PagedResult<EventRegistrationDto>)((OkObjectResult)(await controller.Registrations(ev.Id, status: "waitlisted")).Result!).Value!;
        Assert.Equal("=bob@example.com", Assert.Single(waitlisted.Items).Email);
        var search = (PagedResult<EventRegistrationDto>)((OkObjectResult)(await controller.Registrations(ev.Id, search: "ALICE")).Result!).Value!;
        Assert.Single(search.Items);
        var wildcard = (PagedResult<EventRegistrationDto>)((OkObjectResult)(await controller.Registrations(ev.Id, search: "%")).Result!).Value!;
        Assert.Empty(wildcard.Items);
        await Assert.ThrowsAsync<BadRequestException>(() => controller.Registrations(ev.Id, status: "bogus"));

        var file = Assert.IsType<FileContentResult>(await controller.Export(ev.Id));
        var csv = Encoding.UTF8.GetString(file.FileContents);
        Assert.Contains("alice@example.com", csv);
        Assert.Contains("'=bob@example.com", csv); // formula injection defused
    }

    [Fact]
    public async Task Platform_ValidationErrors()
    {
        var (_, app, _) = await fixture.SeedAppAsync();
        await using var db = fixture.CreateContext();
        var controller = Platform(app, db);
        var ex = await Assert.ThrowsAsync<ValidationException>(() => controller.Create(Request(slug: "Bad Slug", configure: r =>
        {
            r.Timezone = "Mars/Olympus";
            r.EndsAt = r.StartsAt!.Value.AddHours(-1);
            r.ManageUrlTemplate = "http://site.example.com/manage";
            r.NotifyEmails = ["nope"];
            r.RegistrationSchema = System.Text.Json.JsonSerializer.SerializeToElement(new { type = 5 });
        })));
        var pointers = ex.Errors.Select(e => e.Pointer).ToList();
        Assert.Contains("slug", pointers);
        Assert.Contains("timezone", pointers);
        Assert.Contains("ends_at", pointers);
        Assert.Contains("manage_url_template", pointers);
        Assert.Contains("notify_emails/0", pointers);
        Assert.Contains("registration_schema", pointers);

        await controller.Create(Request(slug: "taken"));
        await Assert.ThrowsAsync<ConflictException>(() => controller.Create(Request(slug: "taken")));
        await Assert.ThrowsAsync<ValidationException>(() => controller.Create(Request(slug: "me")));
    }

    [Fact]
    public void Endpoints_UseExpectedPoliciesAndRateLimits()
    {
        static string[] Policies(MethodInfo m)
            => m.GetCustomAttributes<AuthorizeAttribute>().Select(a => a.Policy!)
                .Concat(m.DeclaringType!.GetCustomAttributes<AuthorizeAttribute>().Select(a => a.Policy!)).ToArray();
        static string? Limit(MethodInfo m) => m.GetCustomAttribute<EnableRateLimitingAttribute>()?.PolicyName;
        MethodInfo P(string name) => typeof(PlatformEventsController).GetMethod(name)!;
        MethodInfo C(string name) => typeof(ClientEventsController).GetMethod(name)!;

        foreach (var write in new[] { "Create", "Update", "Publish", "Cancel", "AddRegistration", "CancelRegistration", "Promote", "CheckIn" })
            Assert.Contains(PolicyNames.AppOwnerOrEditor, Policies(P(write)));
        Assert.Contains(PolicyNames.AppOwnerOrAdmin, Policies(P("Delete")));
        foreach (var read in new[] { "List", "Get", "Stats", "Registrations", "Export" })
            Assert.Equal([PolicyNames.AppAnyAccess], Policies(P(read)));

        Assert.Equal(RateLimitPolicyNames.ClientAnonymousWrites, Limit(C("Register")));
        Assert.Equal(RateLimitPolicyNames.ClientAnonymousWrites, Limit(C("CancelByToken")));
        foreach (var read in new[] { "List", "Get", "Calendar", "GetByToken", "Mine" })
            Assert.Equal(RateLimitPolicyNames.ClientReads, Limit(C(read)));
        Assert.Contains(PolicyNames.AuthenticatedAppUser, Policies(C("Mine")));
        Assert.Contains(PolicyNames.ClientAnonymousOrAppUser, Policies(C("Register")));
    }

    [Fact]
    public async Task AuthenticatedAppUserPolicy_RejectsAnonymous_ForMe()
    {
        var (_, app, _) = await fixture.SeedAppAsync();
        using var pipeline = new TestAuthPipeline(fixture);
        var context = pipeline.CreateContext($"/api/{app.PublicId}/events/me/registrations");
        var result = await pipeline.AuthorizeAsync(context, PolicyNames.AuthenticatedAppUser);
        Assert.False(result.Succeeded);
    }

    [Fact]
    public void ProblemFilter_AddsReasonCode()
    {
        var http = new DefaultHttpContext();
        var context = new ExceptionContext(new ActionContext(http, new RouteData(), new ActionDescriptor()), [])
        {
            Exception = new RegistrationClosedException(RegistrationClosedReasons.SoldOut, "This event is sold out")
        };
        new EventProblemFilter().OnException(context);
        var result = Assert.IsType<ObjectResult>(context.Result);
        Assert.Equal(409, result.StatusCode);
        Assert.Equal("sold_out", Assert.IsType<ProblemDetails>(result.Value).Extensions["reason"]);
    }

    [Fact]
    public async Task Calendar_ReturnsIcs()
    {
        var (_, app, _) = await fixture.SeedAppAsync();
        var ev = await SeedEventAsync(fixture, app);
        await using var db = fixture.CreateContext();
        var file = Assert.IsType<FileContentResult>(await Client(app, db).Calendar(ev.Slug));
        Assert.StartsWith("text/calendar", file.ContentType);
        var ics = Encoding.UTF8.GetString(file.FileContents);
        Assert.Contains("SUMMARY:Monthly Meetup", ics);
        Assert.DoesNotContain("meet.example.com", ics); // online link only for registrants
    }
}
