using System.Globalization;
using Extatic.Api.Notifications;
using Extatic.Api.Services;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Extatic.Api.Features.Scheduling;

internal static class SchedulingValidation
{
    private static readonly string[] TimeFormats = ["HH:mm", "HH:mm:ss", "H:mm"];

    public static ValidationException Invalid(string pointer, string message)
        => new("Validation failed", [new ValidationError(pointer, message)]);

    /// <summary>Parses "HH:mm"; when <paramref name="isEnd"/>, "24:00" means midnight (stored as 00:00).</summary>
    public static bool TryParseTime(string? value, bool isEnd, out TimeOnly time)
    {
        time = default;
        if (string.IsNullOrWhiteSpace(value)) return false;
        if (isEnd && value.Trim() is "24:00" or "24:00:00") { time = TimeOnly.MinValue; return true; }
        return TimeOnly.TryParseExact(value.Trim(), TimeFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out time);
    }

    public static TimeZoneInfo RequireZone(string? id, string pointer)
        => SlotEngine.TryFindZone(id, out var zone) ? zone : throw Invalid(pointer, "Must be a valid IANA time zone id, e.g. Europe/Berlin");

    public static string RequireText(string? value, string pointer, int max)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed.Length > max || trimmed.Any(c => char.IsControl(c) && c is not '\n' and not '\r' and not '\t'))
            throw Invalid(pointer, $"Required, at most {max} characters");
        return trimmed;
    }

    public static string? OptionalText(string? value, string pointer, int max, bool singleLine = true)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed)) return null;
        if (trimmed.Length > max) throw Invalid(pointer, $"At most {max} characters");
        if (trimmed.Any(c => char.IsControl(c) && (singleLine || c is not '\n' and not '\r' and not '\t')))
            throw Invalid(pointer, "Contains invalid characters");
        return trimmed;
    }

    public static string RequireEmail(string? value, string pointer)
    {
        var trimmed = value?.Trim().ToLowerInvariant();
        if (!EmailQueue.IsValidAddress(trimmed)) throw Invalid(pointer, "Must be a valid email address");
        return trimmed!;
    }

    public static DateTime AsUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Local => value.ToUniversalTime(),
        DateTimeKind.Unspecified => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        _ => value
    };

    public static bool IsUniqueViolation(DbUpdateException ex)
        => ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };

    public static bool IsOverlapViolation(DbUpdateException ex)
        => ex.InnerException is PostgresException { SqlState: SchedulingSchema.ExclusionViolationSqlState };

    /// <summary>Sorted, start &lt; end and non-overlapping (adjacent allowed) per weekday.</summary>
    public static List<(DayOfWeek Day, TimeOnly Start, TimeOnly End)> ParseRules(IReadOnlyList<AvailabilityRuleDto> rules)
    {
        if (rules.Count > 100) throw Invalid("rules", "At most 100 time ranges");
        var parsed = new List<(DayOfWeek Day, TimeOnly Start, TimeOnly End)>();
        for (var i = 0; i < rules.Count; i++)
        {
            var r = rules[i];
            if (r.DayOfWeek is < 0 or > 6) throw Invalid($"/rules/{i}/day_of_week", "Must be 0 (Sunday) to 6 (Saturday)");
            if (!TryParseTime(r.StartTime, false, out var start)) throw Invalid($"/rules/{i}/start_time", "Must be HH:mm");
            if (!TryParseTime(r.EndTime, true, out var end)) throw Invalid($"/rules/{i}/end_time", "Must be HH:mm");
            if (start.ToTimeSpan() >= SlotEngine.EndOffset(end)) throw Invalid($"/rules/{i}/end_time", "Must be after start_time");
            parsed.Add(((DayOfWeek)r.DayOfWeek, start, end));
        }
        foreach (var day in parsed.GroupBy(p => p.Day))
        {
            var ordered = day.OrderBy(p => p.Start).ToList();
            for (var i = 1; i < ordered.Count; i++)
                if (ordered[i].Start.ToTimeSpan() < SlotEngine.EndOffset(ordered[i - 1].End))
                    throw Invalid("rules", $"Time ranges on {day.Key} overlap");
        }
        return parsed;
    }
}
