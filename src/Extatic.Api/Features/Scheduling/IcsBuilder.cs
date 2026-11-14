using System.Globalization;
using System.Text;

namespace Extatic.Api.Features.Scheduling;

/// <summary>RFC 5545 single-event calendar (METHOD:PUBLISH, UTC times, TEXT escaping, 75-octet folding, CRLF).</summary>
public static class IcsBuilder
{
    /// <summary>Requires Calendar and AppointmentType to be loaded.</summary>
    public static string Build(Appointment a, DateTime nowUtc)
    {
        var type = a.AppointmentType;
        var lines = new List<string>
        {
            "BEGIN:VCALENDAR",
            "VERSION:2.0",
            "PRODID:-//Extatic//Scheduling//EN",
            "CALSCALE:GREGORIAN",
            "METHOD:PUBLISH",
            "BEGIN:VEVENT",
            $"UID:{a.Id}@extatic",
            $"DTSTAMP:{Utc(nowUtc)}",
            $"DTSTART:{Utc(a.StartsAt)}",
            $"DTEND:{Utc(a.EndsAt)}",
            $"SUMMARY:{Escape($"{type.Name} – {a.Calendar.Name}")}",
            $"STATUS:{a.Status switch { AppointmentStatus.Confirmed or AppointmentStatus.Completed => "CONFIRMED", AppointmentStatus.Pending => "TENTATIVE", _ => "CANCELLED" }}"
        };
        if (type.Location is { } location) lines.Add($"LOCATION:{Escape(location)}");
        var description = new StringBuilder();
        if (type.Description is { } d) description.Append(d);
        if (a.Status == AppointmentStatus.Confirmed && type.MeetingUrl is { } url)
        {
            if (description.Length > 0) description.Append("\n\n");
            description.Append("Join: ").Append(url);
            lines.Add($"URL:{url}");
        }
        if (description.Length > 0) lines.Add($"DESCRIPTION:{Escape(description.ToString())}");
        lines.Add("END:VEVENT");
        lines.Add("END:VCALENDAR");

        var sb = new StringBuilder();
        foreach (var line in lines) sb.Append(Fold(line)).Append("\r\n");
        return sb.ToString();
    }

    public static string Utc(DateTime value)
        => DateTime.SpecifyKind(value, DateTimeKind.Utc).ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);

    /// <summary>TEXT value escaping: backslash, semicolon, comma, newline; other control characters dropped.</summary>
    public static string Escape(string value)
    {
        var sb = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            switch (c)
            {
                case '\\': sb.Append("\\\\"); break;
                case ';': sb.Append("\\;"); break;
                case ',': sb.Append("\\,"); break;
                case '\r':
                    if (i + 1 < value.Length && value[i + 1] == '\n') i++;
                    sb.Append("\\n");
                    break;
                case '\n': sb.Append("\\n"); break;
                default:
                    if (!char.IsControl(c)) sb.Append(c);
                    break;
            }
        }
        return sb.ToString();
    }

    /// <summary>Folds to at most 75 octets per physical line, never splitting a UTF-8 sequence or surrogate pair.</summary>
    public static string Fold(string line)
    {
        if (Encoding.UTF8.GetByteCount(line) <= 75) return line;
        var sb = new StringBuilder();
        var octets = 0;
        var limit = 75;
        for (var i = 0; i < line.Length; i++)
        {
            var len = char.IsHighSurrogate(line[i]) && i + 1 < line.Length ? 2 : 1;
            var unit = line.Substring(i, len);
            var bytes = Encoding.UTF8.GetByteCount(unit);
            if (octets + bytes > limit)
            {
                sb.Append("\r\n ");
                octets = 0;
                limit = 74; // continuation lines start with one space
            }
            sb.Append(unit);
            octets += bytes;
            i += len - 1;
        }
        return sb.ToString();
    }
}
