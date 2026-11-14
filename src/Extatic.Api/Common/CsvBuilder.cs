using System.Text;

namespace Extatic.Api.Common;

/// <summary>
/// Builds RFC 4180 CSV (CRLF line endings, quoting where needed). Cells beginning with
/// <c>= + - @</c> (or tab / CR) are prefixed with a single quote to defuse spreadsheet formula injection.
/// </summary>
public class CsvBuilder
{
    private readonly StringBuilder sb = new();

    public CsvBuilder AddRow(params IEnumerable<object?> cells)
    {
        var first = true;
        foreach (var cell in cells)
        {
            if (!first) sb.Append(',');
            first = false;
            sb.Append(Escape(Format(cell)));
        }
        sb.Append("\r\n");
        return this;
    }

    public override string ToString() => sb.ToString();

    /// <summary>UTF-8 with BOM so Excel detects the encoding.</summary>
    public byte[] ToBytes() => Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();

    public static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        if (value[0] is '=' or '+' or '-' or '@' or '\t' or '\r')
            value = "'" + value;
        var needsQuotes = value.IndexOfAny([',', '"', '\r', '\n']) >= 0;
        return needsQuotes ? $"\"{value.Replace("\"", "\"\"")}\"" : value;
    }

    private static string? Format(object? cell) => cell switch
    {
        null => null,
        DateTime dt => dt.ToUniversalTime().ToString("O"),
        DateTimeOffset dto => dto.ToUniversalTime().ToString("O"),
        bool b => b ? "true" : "false",
        IFormattable f => f.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
        _ => cell.ToString()
    };
}
