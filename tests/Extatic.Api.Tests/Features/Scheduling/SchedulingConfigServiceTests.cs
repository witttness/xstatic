using System.Text.Json;
using Extatic.Api.Features.Scheduling;
using Extatic.Api.Services;

namespace Extatic.Api.Tests.Features.Scheduling;

public class SchedulingConfigServiceTests(ApiTestFixture fixture) : IClassFixture<ApiTestFixture>
{
    private static readonly DateTimeOffset Now = new(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);

    private static SaveAppointmentTypeRequest TypeRequest(Guid calendarId, string slug = "intro",
        JsonElement? schema = null, string? manageUrl = null, int? duration = 45)
        => new(calendarId, "Intro call", slug, null, duration, null, null, null, null, null, null, null, schema, null, null, manageUrl, null);

    [Fact]
    public async Task Calendar_ValidatesTimezoneSlugAndEmails_AndSlugIsUniquePerApp()
    {
        var (_, app, _) = await fixture.SeedAppAsync();
        var (_, app2, _) = await fixture.SeedAppAsync();
        await using var db = fixture.CreateContext();
        var config = new SchedulingConfigService(db);

        var bad = await Assert.ThrowsAsync<ValidationException>(() => config.CreateCalendarAsync(app.Id, new("Room", "room", "Nowhere/Land", null, null)));
        Assert.Equal("timezone", bad.Errors.Single().Pointer);
        await Assert.ThrowsAsync<ValidationException>(() => config.CreateCalendarAsync(app.Id, new("Room", "Bad Slug", "UTC", null, null)));
        await Assert.ThrowsAsync<ValidationException>(() => config.CreateCalendarAsync(app.Id, new("Room", "room", "UTC", ["nope"], null)));

        var created = await config.CreateCalendarAsync(app.Id, new("Room", "room", "Europe/Berlin", ["Staff@Example.com"], null));
        Assert.Equal(["staff@example.com"], created.NotifyEmails);
        Assert.True(created.IsActive);
        await Assert.ThrowsAsync<ConflictException>(() => new SchedulingConfigService(fixture.CreateContext())
            .CreateCalendarAsync(app.Id, new("Room 2", "room", "UTC", null, null)));
        await new SchedulingConfigService(fixture.CreateContext()).CreateCalendarAsync(app2.Id, new("Room", "room", "UTC", null, null));

        var updated = await config.UpdateCalendarAsync(app.Id, created.Id, new(null, null, "America/New_York", null, false));
        Assert.Equal("America/New_York", updated.Timezone);
        Assert.False(updated.IsActive);
        Assert.Equal("room", updated.Slug);
    }

    [Fact]
    public async Task Rules_ReplaceAll_ValidatesOrderAndOverlap()
    {
        var (_, app, _) = await fixture.SeedAppAsync();
        await using var db = fixture.CreateContext();
        var config = new SchedulingConfigService(db);
        var calendar = await config.CreateCalendarAsync(app.Id, new("Room", "room", "UTC", null, null));

        var saved = await config.ReplaceRulesAsync(app.Id, calendar.Id, new([new(1, "09:00", "12:00"), new(1, "13:00", "17:00"), new(6, "22:00", "24:00")]));
        Assert.Equal(3, saved.Rules.Count);
        Assert.Equal("24:00", saved.ToDto().Rules.Single(r => r.DayOfWeek == 6).EndTime);

        await Assert.ThrowsAsync<ValidationException>(() => config.ReplaceRulesAsync(app.Id, calendar.Id, new([new(1, "09:00", "12:00"), new(1, "11:00", "13:00")])));
        await Assert.ThrowsAsync<ValidationException>(() => config.ReplaceRulesAsync(app.Id, calendar.Id, new([new(1, "12:00", "09:00")])));
        await Assert.ThrowsAsync<ValidationException>(() => config.ReplaceRulesAsync(app.Id, calendar.Id, new([new(7, "09:00", "10:00")])));
        await Assert.ThrowsAsync<ValidationException>(() => config.ReplaceRulesAsync(app.Id, calendar.Id, new([new(1, "9am", "10:00")])));

        // Adjacent ranges are fine; replace-all removes the previous rules.
        var replaced = await new SchedulingConfigService(fixture.CreateContext())
            .ReplaceRulesAsync(app.Id, calendar.Id, new([new(2, "09:00", "10:00"), new(2, "10:00", "11:00")]));
        Assert.Equal(2, replaced.Rules.Count);
        Assert.All(replaced.Rules, r => Assert.Equal(DayOfWeek.Tuesday, r.DayOfWeek));
    }

    [Fact]
    public async Task Rules_ConcurrentReplaceAll_NeverMerges()
    {
        var (_, app, _) = await fixture.SeedAppAsync();
        await using var setup = fixture.CreateContext();
        var calendar = await new SchedulingConfigService(setup).CreateCalendarAsync(app.Id, new("Room", "room", "UTC", null, null));
        var start = new TaskCompletionSource();
        var tasks = Enumerable.Range(0, 6).Select(async i =>
        {
            await start.Task;
            await using var db = fixture.CreateContext();
            await new SchedulingConfigService(db).ReplaceRulesAsync(app.Id, calendar.Id,
                new([new(i % 7, "09:00", "10:00"), new(i % 7, "11:00", "12:00")]));
        }).ToList();
        start.SetResult();
        await Task.WhenAll(tasks);
        await using var check = fixture.CreateContext();
        var rules = check.AvailabilityRules.Where(r => r.CalendarId == calendar.Id).ToList();
        Assert.Equal(2, rules.Count);
        Assert.Single(rules.Select(r => r.DayOfWeek).Distinct());
    }

    [Fact]
    public async Task Overrides_ValidateAndAreUniquePerDate()
    {
        var (_, app, _) = await fixture.SeedAppAsync();
        await using var db = fixture.CreateContext();
        var config = new SchedulingConfigService(db);
        var calendar = await config.CreateCalendarAsync(app.Id, new("Room", "room", "UTC", null, null));
        var date = new DateOnly(2026, 12, 24);

        await Assert.ThrowsAsync<ValidationException>(() => config.CreateOverrideAsync(app.Id, calendar.Id, new(date, false, null, null, null)));
        await Assert.ThrowsAsync<ValidationException>(() => config.CreateOverrideAsync(app.Id, calendar.Id, new(date, false, "14:00", "10:00", null)));
        var closed = await config.CreateOverrideAsync(app.Id, calendar.Id, new(date, true, "09:00", "10:00", "Holiday"));
        Assert.Null(closed.StartTime);
        await Assert.ThrowsAsync<ConflictException>(() => new SchedulingConfigService(fixture.CreateContext())
            .CreateOverrideAsync(app.Id, calendar.Id, new(date, true, null, null, null)));

        var custom = await config.UpdateOverrideAsync(app.Id, calendar.Id, closed.Id, new(date, false, "10:00", "12:00", "Half day"));
        Assert.Equal(new TimeOnly(10, 0), custom.StartTime);
        Assert.Single(await config.ListOverridesAsync(app.Id, calendar.Id, date, date.AddDays(1)));
        Assert.Empty(await config.ListOverridesAsync(app.Id, calendar.Id, date.AddDays(1), null));
        await config.DeleteOverrideAsync(app.Id, calendar.Id, closed.Id);
        await Assert.ThrowsAsync<NotFoundException>(() => config.DeleteOverrideAsync(app.Id, calendar.Id, closed.Id));
    }

    [Fact]
    public async Task Types_DefaultsAndValidation()
    {
        var (_, app, _) = await fixture.SeedAppAsync();
        await using var db = fixture.CreateContext();
        var config = new SchedulingConfigService(db);
        var calendar = await config.CreateCalendarAsync(app.Id, new("Room", "room", "UTC", null, null));

        var type = await config.CreateTypeAsync(app.Id, TypeRequest(calendar.Id));
        Assert.Equal(45, type.SlotIntervalMinutes); // defaults to duration
        Assert.Equal(120, type.MinNoticeMinutes);
        Assert.Equal(60, type.MaxAdvanceDays);
        Assert.True(type.IsActive);

        await Assert.ThrowsAsync<ConflictException>(() => new SchedulingConfigService(fixture.CreateContext()).CreateTypeAsync(app.Id, TypeRequest(calendar.Id)));
        await Assert.ThrowsAsync<ValidationException>(() => config.CreateTypeAsync(app.Id, TypeRequest(calendar.Id, "x1", duration: 2)));
        await Assert.ThrowsAsync<ValidationException>(() => config.CreateTypeAsync(app.Id, TypeRequest(Guid.NewGuid(), "x2")));
        await Assert.ThrowsAsync<ValidationException>(() => config.CreateTypeAsync(app.Id, TypeRequest(calendar.Id, "x3", manageUrl: "http://site.example.com/{token}")));
        await Assert.ThrowsAsync<ValidationException>(() => config.CreateTypeAsync(app.Id, TypeRequest(calendar.Id, "x4", manageUrl: "https://site.example.com/manage")));
        await Assert.ThrowsAsync<ValidationException>(() => config.CreateTypeAsync(app.Id, TypeRequest(calendar.Id, "x5", schema: JsonSerializer.SerializeToElement("nope"))));
        await Assert.ThrowsAsync<ValidationException>(() => config.CreateTypeAsync(app.Id, TypeRequest(calendar.Id, "x6",
            schema: JsonDocument.Parse("""{"type": 12}""").RootElement)));

        var withSchema = await config.CreateTypeAsync(app.Id, TypeRequest(calendar.Id, "x7",
            schema: JsonDocument.Parse("""{"type":"object"}""").RootElement, manageUrl: "https://site.example.com/manage?t={token}"));
        Assert.NotNull(withSchema.IntakeSchema);
        var cleared = await config.UpdateTypeAsync(app.Id, withSchema.Id, new(null, null, null, null, null, null, null, null, null, null, null, null,
            JsonDocument.Parse("null").RootElement, null, null, null, false));
        Assert.Null(cleared.IntakeSchema);
        Assert.False(cleared.IsActive);
        Assert.Equal("x7", cleared.Slug);
    }
}
