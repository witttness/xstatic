using System.Globalization;
using System.Text;

namespace Extatic.Api.Features.Events;

/// <summary>RFC 5545 iCalendar for a single event: escaped TEXT values, CRLF lines folded at 75 octets.</summary>
public static class IcsBuilder
{
    public static string Build(Event ev, string appName, DateTime? now = null)
    {
        var sb = new StringBuilder();
        void Line(string content) => sb.Append(Fold(content)).Append("\r\n");

        Line("BEGIN:VCALENDAR");
        Line("VERSION:2.0");
        Line("PRODID:-//Extatic//Events//EN");
        Line("CALSCALE:GREGORIAN");
        Line("METHOD:PUBLISH");
        Line("BEGIN:VEVENT");
        Line($"UID:{ev.Id}@extatic");
        Line($"DTSTAMP:{Format(now ?? DateTime.UtcNow)}");
        Line($"DTSTART:{Format(ev.StartsAt)}");
        Line($"DTEND:{Format(ev.EndsAt)}");
        Line($"SUMMARY:{Escape(ev.Title)}");
        if (!string.IsNullOrEmpty(ev.Description)) Line($"DESCRIPTION:{Escape(ev.Description)}");
        if (!string.IsNullOrEmpty(ev.Location)) Line($"LOCATION:{Escape(ev.Location)}");
        Line($"ORGANIZER;CN={EscapeParam(appName)}:noreply@extatic.invalid");
        Line($"STATUS:{(ev.Status == EventStatus.Cancelled ? "CANCELLED" : "CONFIRMED")}");
        Line($"SEQUENCE:{new DateTimeOffset(DateTime.SpecifyKind(ev.UpdatedAt, DateTimeKind.Utc)).ToUnixTimeSeconds() % int.MaxValue}");
        Line("END:VEVENT");
        Line("END:VCALENDAR");
        return sb.ToString();
    }

    public static string Format(DateTime utc)
        => DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);

    /// <summary>TEXT escaping (RFC 5545 3.3.11): backslash, semicolon, comma, newline; other controls dropped.</summary>
    public static string Escape(string value)
    {
        var sb = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            switch (c)
            {
                case '\\': sb.Append(@"\\"); break;
                case ';': sb.Append(@"\;"); break;
                case ',': sb.Append(@"\,"); break;
                case '\r':
                    if (i + 1 < value.Length && value[i + 1] == '\n') i++;
                    sb.Append(@"\n");
                    break;
                case '\n': sb.Append(@"\n"); break;
                default:
                    if (!char.IsControl(c)) sb.Append(c);
                    break;
            }
        }
        return sb.ToString();
    }

    /// <summary>Parameter values: quoted, with DQUOTE and controls removed (RFC 5545 3.2).</summary>
    private static string EscapeParam(string value)
        => "\"" + new string(value.Where(c => c != '"' && !char.IsControl(c)).ToArray()) + "\"";

    /// <summary>Folds a content line so no physical line exceeds 75 octets (UTF-8), never splitting a character.</summary>
    public static string Fold(string line)
    {
        if (Encoding.UTF8.GetByteCount(line) <= 75) return line;
        var sb = new StringBuilder();
        var count = 0;
        var limit = 75;
        var e = StringInfo.GetTextElementEnumerator(line);
        while (e.MoveNext())
        {
            var element = e.GetTextElement();
            var bytes = Encoding.UTF8.GetByteCount(element);
            if (count + bytes > limit)
            {
                sb.Append("\r\n ");
                count = 0;
                limit = 74; // continuation lines start with a space
            }
            sb.Append(element);
            count += bytes;
        }
        return sb.ToString();
    }
}
