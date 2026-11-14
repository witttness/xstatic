using Extatic.Api.Features.Scheduling;

namespace Extatic.Api.Tests.Features.Scheduling;

/// <summary>Pure slot generation (no database).</summary>
public class SlotEngineTests
{
    private static readonly DateTime Monday = new(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime LongAgo = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static AvailabilityRule Rule(DayOfWeek day, int startH, int startM, int endH, int endM)
        => new() { DayOfWeek = day, StartTime = new(startH, startM), EndTime = new(endH, endM) };

    private static SlotSettings Settings(int duration = 30, int interval = 30, int before = 0, int after = 0,
        int minNotice = 0, int maxAdvance = 400, int? maxPerDay = null)
        => new(duration, before, after, interval, minNotice, maxAdvance, maxPerDay);

    private static List<Slot> Run(string zoneId, IEnumerable<AvailabilityRule> rules, SlotSettings settings,
        DateOnly from, DateOnly to, DateTime? now = null, IEnumerable<AvailabilityOverride>? overrides = null,
        IEnumerable<BusyBlock>? busy = null, IEnumerable<DateTime>? sameType = null, bool enforceMinNotice = true)
    {
        Assert.True(SlotEngine.TryFindZone(zoneId, out var zone));
        return SlotEngine.Generate(zone, rules, overrides ?? [], settings, busy ?? [], sameType ?? [],
            SlotEngine.StartOfLocalDateUtc(from, zone), SlotEngine.StartOfLocalDateUtc(to, zone), now ?? LongAgo, enforceMinNotice);
    }

    private static DateTime U(int month, int day, int hour, int minute = 0)
        => new(2026, month, day, hour, minute, 0, DateTimeKind.Utc);

    private static readonly DateOnly Mon = new(2026, 3, 2);

    [Fact]
    public void Basics_StepsThroughWindow_EndingWithinIt()
    {
        var slots = Run("UTC", [Rule(DayOfWeek.Monday, 9, 0, 11, 0)], Settings(), Mon, Mon.AddDays(1));
        Assert.Equal([U(3, 2, 9), U(3, 2, 9, 30), U(3, 2, 10), U(3, 2, 10, 30)], slots.Select(s => s.StartsAt));
        Assert.All(slots, s => Assert.Equal(TimeSpan.FromMinutes(30), s.EndsAt - s.StartsAt));
        Assert.All(slots, s => Assert.Equal(DateTimeKind.Utc, s.StartsAt.Kind));
    }

    [Fact]
    public void Interval_IsIndependentOfDuration()
    {
        var slots = Run("UTC", [Rule(DayOfWeek.Monday, 9, 0, 11, 0)], Settings(duration: 60, interval: 15), Mon, Mon.AddDays(1));
        Assert.Equal([U(3, 2, 9), U(3, 2, 9, 15), U(3, 2, 9, 30), U(3, 2, 9, 45), U(3, 2, 10)], slots.Select(s => s.StartsAt));
    }

    [Fact]
    public void MultipleRangesPerDay_AndMidnightEnd()
    {
        var slots = Run("UTC", [Rule(DayOfWeek.Monday, 9, 0, 10, 0), Rule(DayOfWeek.Monday, 23, 0, 0, 0)],
            Settings(), Mon, Mon.AddDays(1));
        Assert.Equal([U(3, 2, 9), U(3, 2, 9, 30), U(3, 2, 23), U(3, 2, 23, 30)], slots.Select(s => s.StartsAt));
    }

    [Fact]
    public void Buffers_ExtendTheBlockThatMustNotOverlapBusyTime()
    {
        var busy = new BusyBlock(U(3, 2, 10), U(3, 2, 10, 30));
        var slots = Run("UTC", [Rule(DayOfWeek.Monday, 9, 0, 12, 0)], Settings(before: 15, after: 15), Mon, Mon.AddDays(1), busy: [busy]);
        // 09:30 (block to 10:15), 10:00 and 10:30 (block from 10:15) collide with the 10:00–10:30 booking.
        Assert.Equal([U(3, 2, 9), U(3, 2, 11), U(3, 2, 11, 30)], slots.Select(s => s.StartsAt));
        var first = slots[0];
        Assert.Equal(U(3, 2, 8, 45), first.BlockStartsAt);
        Assert.Equal(U(3, 2, 9, 45), first.BlockEndsAt);
    }

    [Fact]
    public void AdjacentBusyBlocks_DoNotConflict()
    {
        var busy = new BusyBlock(U(3, 2, 9), U(3, 2, 9, 30));
        var slots = Run("UTC", [Rule(DayOfWeek.Monday, 9, 0, 10, 0)], Settings(), Mon, Mon.AddDays(1), busy: [busy]);
        Assert.Equal([U(3, 2, 9, 30)], slots.Select(s => s.StartsAt));
    }

    [Fact]
    public void MinNotice_DropsSlotsTooSoon_UnlessBypassed()
    {
        var now = U(3, 2, 9, 10);
        var rules = new[] { Rule(DayOfWeek.Monday, 9, 0, 11, 0) };
        var enforced = Run("UTC", rules, Settings(minNotice: 60), Mon, Mon.AddDays(1), now);
        Assert.Equal([U(3, 2, 10, 30)], enforced.Select(s => s.StartsAt));
        var bypassed = Run("UTC", rules, Settings(minNotice: 60), Mon, Mon.AddDays(1), now, enforceMinNotice: false);
        Assert.Equal([U(3, 2, 9, 30), U(3, 2, 10), U(3, 2, 10, 30)], bypassed.Select(s => s.StartsAt));
    }

    [Fact]
    public void MaxAdvance_DropsSlotsTooFarAhead()
    {
        var rules = new[] { Rule(DayOfWeek.Monday, 9, 0, 10, 0), Rule(DayOfWeek.Tuesday, 9, 0, 10, 0) };
        var slots = Run("UTC", rules, Settings(maxAdvance: 1), Mon, Mon.AddDays(2), now: Monday);
        Assert.All(slots, s => Assert.Equal(2, s.StartsAt.Day));
        Assert.Equal(2, slots.Count);
    }

    [Fact]
    public void MaxPerDay_ClosesTheLocalDayOnceReached()
    {
        var rules = new[] { Rule(DayOfWeek.Monday, 9, 0, 10, 0), Rule(DayOfWeek.Tuesday, 9, 0, 10, 0) };
        var sameType = new[] { U(3, 2, 14), U(3, 2, 15) };
        var slots = Run("UTC", rules, Settings(maxPerDay: 2), Mon, Mon.AddDays(2), sameType: sameType);
        Assert.Equal([U(3, 3, 9), U(3, 3, 9, 30)], slots.Select(s => s.StartsAt));
        var under = Run("UTC", rules, Settings(maxPerDay: 3), Mon, Mon.AddDays(2), sameType: sameType);
        Assert.Equal(4, under.Count);
    }

    [Fact]
    public void Override_ClosedDay()
    {
        var closed = new AvailabilityOverride { Date = Mon, IsUnavailable = true };
        var slots = Run("UTC", [Rule(DayOfWeek.Monday, 9, 0, 11, 0)], Settings(), Mon, Mon.AddDays(1), overrides: [closed]);
        Assert.Empty(slots);
    }

    [Fact]
    public void Override_CustomHoursReplaceWeeklyRules()
    {
        var custom = new AvailabilityOverride { Date = Mon, StartTime = new(13, 0), EndTime = new(14, 0) };
        var saturday = new AvailabilityOverride { Date = new(2026, 3, 7), StartTime = new(10, 0), EndTime = new(10, 30) };
        var slots = Run("UTC", [Rule(DayOfWeek.Monday, 9, 0, 11, 0)], Settings(), Mon, Mon.AddDays(6), overrides: [custom, saturday]);
        Assert.Equal([U(3, 2, 13), U(3, 2, 13, 30), U(3, 7, 10)], slots.Select(s => s.StartsAt));
    }

    [Fact]
    public void Dst_NewYork_SpringForward_SkipsNonexistentLocalTimes()
    {
        // 2026-03-08: 02:00 EST jumps to 03:00 EDT.
        var day = new DateOnly(2026, 3, 8);
        var slots = Run("America/New_York", [Rule(DayOfWeek.Sunday, 1, 0, 4, 0)], Settings(), day, day.AddDays(1));
        Assert.Equal([U(3, 8, 6), U(3, 8, 6, 30), U(3, 8, 7), U(3, 8, 7, 30)], slots.Select(s => s.StartsAt));
    }

    [Fact]
    public void Dst_NewYork_FallBack_AmbiguousTimeUsesFirstOccurrence()
    {
        // 2026-11-01: 02:00 EDT falls back to 01:00 EST; 01:00 local happens twice.
        var day = new DateOnly(2026, 11, 1);
        var slots = Run("America/New_York", [Rule(DayOfWeek.Sunday, 0, 0, 3, 0)], Settings(duration: 60, interval: 60), day, day.AddDays(1));
        // 00:00 EDT, 01:00 EDT (first occurrence), 02:00 EST. The second 01:00 (06:00Z) is not offered.
        Assert.Equal([U(11, 1, 4), U(11, 1, 5), U(11, 1, 7)], slots.Select(s => s.StartsAt));
        Assert.Equal(U(11, 1, 8), slots[^1].EndsAt);
    }

    [Fact]
    public void Dst_Berlin_SpringForward()
    {
        // 2026-03-29: 02:00 CET jumps to 03:00 CEST.
        var day = new DateOnly(2026, 3, 29);
        var slots = Run("Europe/Berlin", [Rule(DayOfWeek.Sunday, 1, 0, 4, 0)], Settings(duration: 60, interval: 60), day, day.AddDays(1));
        Assert.Equal([U(3, 29, 0), U(3, 29, 1)], slots.Select(s => s.StartsAt));
    }

    [Fact]
    public void Dst_Berlin_FallBack()
    {
        // 2026-10-25: 03:00 CEST falls back to 02:00 CET.
        var day = new DateOnly(2026, 10, 25);
        var slots = Run("Europe/Berlin", [Rule(DayOfWeek.Sunday, 1, 0, 4, 0)], Settings(duration: 60, interval: 60), day, day.AddDays(1));
        Assert.Equal([U(10, 24, 23), U(10, 25, 0), U(10, 25, 2)], slots.Select(s => s.StartsAt));
    }

    [Fact]
    public void Dst_SlotSpanningTheGap_HasRealElapsedDuration()
    {
        // A 01:30 EST start for 60 minutes ends at 03:30 EDT local (07:30Z) — still 60 real minutes.
        var day = new DateOnly(2026, 3, 8);
        var slots = Run("America/New_York", [Rule(DayOfWeek.Sunday, 1, 30, 4, 0)], Settings(duration: 60, interval: 60), day, day.AddDays(1));
        Assert.Equal(U(3, 8, 6, 30), slots[0].StartsAt);
        Assert.Equal(U(3, 8, 7, 30), slots[0].EndsAt);
    }

    [Theory]
    [InlineData("Europe/Berlin", true)]
    [InlineData("America/New_York", true)]
    [InlineData("UTC", true)]
    [InlineData("Mars/Olympus", false)]
    [InlineData("", false)]
    public void TryFindZone_AcceptsIanaIds(string id, bool ok)
        => Assert.Equal(ok, SlotEngine.TryFindZone(id, out _));
}
