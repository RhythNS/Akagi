using System.Globalization;

namespace Akagi.Web.Services.Finance;

/// <summary>
/// Reading and showing amounts. Inputs take both 1.234,56 and 1,234.56; output uses German grouping because the
/// accounts are in euros.
/// </summary>
public static class Money
{
    private static readonly CultureInfo Display = CultureInfo.GetCultureInfo("de-DE");

    public static string Symbol(string currency) => currency.ToUpperInvariant() switch
    {
        "EUR" => "€",
        "USD" => "$",
        "GBP" => "£",
        "CHF" => "CHF",
        "JPY" => "¥",
        _ => currency
    };

    public static string Format(decimal value, string currency = "EUR", int decimals = 2) =>
        value.ToString("N" + decimals, Display) + " " + Symbol(currency);

    /// <summary>With an explicit + for gains.</summary>
    public static string Signed(decimal value, string currency = "EUR", int decimals = 2) =>
        (value > 0 ? "+" : value < 0 ? "−" : "") + Format(Math.Abs(value), currency, decimals);

    public static string Percent(decimal? value, int decimals = 1) =>
        value is decimal v ? v.ToString("N" + decimals, Display) + " %" : "–";

    public static string SignedPercent(decimal? value, int decimals = 2) =>
        value is decimal v ? (v > 0 ? "+" : v < 0 ? "−" : "") + Math.Abs(v).ToString("N" + decimals, Display) + " %" : "–";

    /// <summary>Shares: whole numbers plain, fractions with up to six places.</summary>
    public static string Shares(decimal value) => value.ToString(value == decimal.Truncate(value) ? "N0" : "#,##0.######", Display);

    /// <summary>The value as an input shows it again (no grouping, comma as decimal point).</summary>
    public static string Input(decimal value, int maxDecimals = 6) =>
        Math.Round(value, maxDecimals).ToString("0.######", Display);

    /// <summary>
    /// Reads 1234,56 / 1.234,56 / 1,234.56 / 1234.56 / "100 €". With a single separator, a comma is the decimal point,
    /// and so is a dot unless exactly three digits follow it more than once (1.234.567).
    /// </summary>
    public static bool TryParse(string? text, out decimal value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }
        string s = new([.. text.Where(c => char.IsAsciiDigit(c) || c is ',' or '.' or '-' or '−' or '+')]);
        s = s.Replace('−', '-');
        if (s.Length == 0)
        {
            return false;
        }
        int comma = s.LastIndexOf(',');
        int dot = s.LastIndexOf('.');
        if (comma >= 0 && dot >= 0)
        {
            // The later one is the decimal point
            s = comma > dot ? s.Replace(".", "").Replace(',', '.') : s.Replace(",", "");
        }
        else if (comma >= 0)
        {
            s = s.Count(c => c == ',') > 1 ? s.Replace(",", "") : s.Replace(',', '.');
        }
        else if (dot >= 0 && s.Count(c => c == '.') > 1)
        {
            s = s.Replace(".", "");
        }
        return decimal.TryParse(s, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value);
    }

    public static decimal? Parse(string? text) => TryParse(text, out decimal value) ? value : null;
}
