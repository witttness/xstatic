namespace Extatic.Api.Features.Scheduling;

public record SlotSettings(
    int DurationMinutes,
    int BufferBeforeMinutes,
    int BufferAfterMinutes,
    int SlotIntervalMinutes,
    int MinNoticeMinutes,
    int MaxAdvanceDays,
    int? MaxPerDay)
{
    public static SlotSettings From(AppointmentType t) => new(t.DurationMinutes, t.BufferBeforeMinutes,
        t.BufferAfterMinutes, t.SlotIntervalMinutes, t.MinNoticeMinutes, t.MaxAdvanceDays, t.MaxPerDay);
}

/// <summary>A half-open UTC interval [Start, End).</summary>
public record BusyBlock(DateTime Start, DateTime End);

/// <summary>A bookable slot: the appointment itself (UTC) and the block it would occupy incl. buffers.</summary>
public record Slot(DateTime StartsAt, DateTime EndsAt, DateTime BlockStartsAt, DateTime BlockEndsAt);

/// <summary>
/// Pure slot generation. Availability is expanded per local date in the calendar's time zone and
/// converted to UTC per candidate start, so DST transitions are handled exactly:
/// <list type="bullet">
/// <item>a local start time that does not exist (spring-forward gap) produces no slot;</item>
/// <item>an ambiguous local start (fall-back overlap) maps to its FIRST occurrence (the earlier
/// UTC instant, i.e. the daylight-time offset), so each wall-clock start yields at most one slot;</item>
/// <item>the appointment [start, start + duration) must end by the window's end instant; buffers
/// only matter for conflicts and may extend outside the window.</item>
/// </list>
/// </summary>
public static class SlotEngine
{
    public const int MaxRangeDays = 62;

    public static List<Slot> Generate(
        TimeZoneInfo zone,
        IEnumerable<AvailabilityRule> rules,
        IEnumerable<AvailabilityOverride> overrides,
        SlotSettings settings,
        IEnumerable<BusyBlock> busy,
        IEnumerable<DateTime> sameTypeStartsUtc,
        DateTime fromUtc,
        DateTime toUtc,
        DateTime nowUtc,
        bool enforceMinNotice = true)
    {
        var ruleList = rules.ToList();
        var overrideMap = overrides.GroupBy(o => o.Date).ToDictionary(g => g.Key, g => g.First());
        var busyList = busy.OrderBy(b => b.Start).ToList();
        var perDay = sameTypeStartsUtc
            .GroupBy(s => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(s, zone)))
            .ToDictionary(g => g.Key, g => g.Count());

        var duration = TimeSpan.FromMinutes(settings.DurationMinutes);
        var interval = TimeSpan.FromMinutes(Math.Max(1, settings.SlotIntervalMinutes));
        var before = TimeSpan.FromMinutes(settings.BufferBeforeMinutes);
        var after = TimeSpan.FromMinutes(settings.BufferAfterMinutes);
        var earliest = enforceMinNotice ? nowUtc.AddMinutes(settings.MinNoticeMinutes) : nowUtc;
        var latest = nowUtc.AddDays(settings.MaxAdvanceDays);

        var firstDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(fromUtc, zone));
        var lastDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(toUtc, zone));

        var result = new List<Slot>();
        for (var date = firstDate; date <= lastDate; date = date.AddDays(1))
        {
            if (settings.MaxPerDay is { } max && perDay.GetValueOrDefault(date) >= max) continue;

            foreach (var (windowStart, windowEnd) in WindowsFor(date, ruleList, overrideMap))
            {
                var windowEndUtc = ToUtcLenient(windowEnd, zone);
                for (var local = windowStart; local < windowEnd; local = local.Add(interval))
                {
                    if (!TryToUtc(local, zone, out var startUtc)) continue;
                    var endUtc = startUtc + duration;
                    if (endUtc > windowEndUtc) continue;
                    if (startUtc < fromUtc || startUtc >= toUtc) continue;
                    if (startUtc < earliest || startUtc > latest) continue;
                    var blockStart = startUtc - before;
                    var blockEnd = endUtc + after;
                    if (Overlaps(busyList, blockStart, blockEnd)) continue;
                    result.Add(new Slot(startUtc, endUtc, blockStart, blockEnd));
                }
            }
        }
        return result.DistinctBy(s => s.StartsAt).OrderBy(s => s.StartsAt).ToList();
    }

    /// <summary>Local opening windows for a date: the override's custom hours / closure, else the weekly rules.</summary>
    public static IEnumerable<(DateTime Start, DateTime End)> WindowsFor(DateOnly date,
        IReadOnlyCollection<AvailabilityRule> rules, IReadOnlyDictionary<DateOnly, AvailabilityOverride> overrides)
    {
        var day = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        if (overrides.TryGetValue(date, out var o))
        {
            if (o.IsUnavailable || o.StartTime is null || o.EndTime is null) yield break;
            yield return (day + o.StartTime.Value.ToTimeSpan(), day + EndOffset(o.EndTime.Value));
            yield break;
        }
        foreach (var r in rules.Where(r => r.DayOfWeek == date.DayOfWeek).OrderBy(r => r.StartTime))
            yield return (day + r.StartTime.ToTimeSpan(), day + EndOffset(r.EndTime));
    }

    /// <summary>An end time of 00:00 means midnight at the end of the day.</summary>
    public static TimeSpan EndOffset(TimeOnly end) => end == TimeOnly.MinValue ? TimeSpan.FromDays(1) : end.ToTimeSpan();

    /// <summary>Converts a local wall-clock time to UTC. False for a skipped (invalid) local time;
    /// an ambiguous time maps to its first occurrence.</summary>
    public static bool TryToUtc(DateTime local, TimeZoneInfo zone, out DateTime utc)
    {
        local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        if (zone.IsInvalidTime(local)) { utc = default; return false; }
        var offset = zone.IsAmbiguousTime(local) ? zone.GetAmbiguousTimeOffsets(local).Max() : zone.GetUtcOffset(local);
        utc = DateTime.SpecifyKind(local - offset, DateTimeKind.Utc);
        return true;
    }

    /// <summary>Like <see cref="TryToUtc"/>, but a skipped local time is shifted forward by the gap
    /// (converted with the pre-transition standard offset). Used for window ends only.</summary>
    public static DateTime ToUtcLenient(DateTime local, TimeZoneInfo zone)
    {
        if (TryToUtc(local, zone, out var utc)) return utc;
        local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        return DateTime.SpecifyKind(local - zone.GetUtcOffset(local), DateTimeKind.Utc);
    }

    /// <summary>Midnight starting <paramref name="date"/> in <paramref name="zone"/>, as UTC.</summary>
    public static DateTime StartOfLocalDateUtc(DateOnly date, TimeZoneInfo zone)
        => ToUtcLenient(date.ToDateTime(TimeOnly.MinValue), zone);

    private static bool Overlaps(List<BusyBlock> busy, DateTime start, DateTime end)
    {
        foreach (var b in busy)
        {
            if (b.Start >= end) break;
            if (b.End > start) return true;
        }
        return false;
    }

    public static bool TryFindZone(string? id, out TimeZoneInfo zone)
    {
        zone = TimeZoneInfo.Utc;
        if (string.IsNullOrWhiteSpace(id) || id.Length > 100) return false;
        if (!TimeZoneInfo.TryFindSystemTimeZoneById(id, out var found)) return false;
        // Require the IANA form so stored ids are portable across OSes.
        if (!found.HasIanaId) return false;
        zone = found;
        return true;
    }
}
