namespace Extatic.Api.Features.Donations;

/// <summary>
/// Supported ISO 4217 currencies (lowercase) and their minor-unit exponent as the payment provider
/// expects it. Three-decimal currencies (BHD, JOD, KWD, OMR, TND) and currencies with provider-specific
/// rounding rules (HUF, ISK, TWD, UGX) are deliberately not supported yet.
/// </summary>
public static class Currencies
{
    /// <summary>Currencies with no minor unit: amount_minor == major amount (e.g. ¥500 → 500).</summary>
    public static readonly IReadOnlySet<string> ZeroDecimal = new HashSet<string>(StringComparer.Ordinal)
    {
        "bif", "clp", "djf", "gnf", "jpy", "kmf", "krw", "mga", "pyg", "rwf", "vnd", "vuv", "xaf", "xof", "xpf"
    };

    public static readonly IReadOnlySet<string> TwoDecimal = new HashSet<string>(StringComparer.Ordinal)
    {
        "aed", "ars", "aud", "bdt", "bgn", "brl", "cad", "chf", "cny", "cop", "czk", "dkk", "egp", "eur",
        "gbp", "ghs", "hkd", "idr", "ils", "inr", "kes", "lkr", "mad", "mxn", "myr", "ngn", "nok", "nzd",
        "pen", "php", "pkr", "pln", "qar", "ron", "rsd", "sar", "sek", "sgd", "thb", "try", "uah", "usd",
        "uyu", "zar"
    };

    public static bool IsSupported(string? code) => code is not null && (ZeroDecimal.Contains(code) || TwoDecimal.Contains(code));

    /// <summary>Number of minor-unit digits (0 or 2).</summary>
    public static int Decimals(string code) => ZeroDecimal.Contains(code) ? 0 : 2;

    /// <summary>"1050" usd → "10.50"; "500" jpy → "500". Invariant culture, no grouping.</summary>
    public static string FormatMajor(long amountMinor, string currency)
    {
        var decimals = Decimals(currency);
        if (decimals == 0) return amountMinor.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var sign = amountMinor < 0 ? "-" : "";
        var abs = Math.Abs(amountMinor);
        return $"{sign}{abs / 100}.{abs % 100:00}";
    }

    public static string Display(long amountMinor, string currency) => $"{FormatMajor(amountMinor, currency)} {currency.ToUpperInvariant()}";
}
