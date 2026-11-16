namespace Akagi.Web.Services.Finance;

/// <summary>
/// International Securities Identification Numbers: two letters for the country, nine letters or digits, one check
/// digit (Luhn over the letters spelled as numbers, A = 10 ... Z = 35).
/// </summary>
public static class Isin
{
    /// <summary>Upper case without spaces, as it is stored.</summary>
    public static string Normalize(string? value) =>
        new([.. (value ?? string.Empty).Where(c => !char.IsWhiteSpace(c) && c != '-').Select(char.ToUpperInvariant)]);

    public static bool IsValid(string? value) => Problem(value) == null;

    /// <summary>Why the value is not an ISIN, or null when it is one.</summary>
    public static string? Problem(string? value)
    {
        string isin = Normalize(value);
        if (isin.Length != 12)
        {
            return "An ISIN has 12 characters, like IE00B4ND3602.";
        }
        if (!char.IsAsciiLetterUpper(isin[0]) || !char.IsAsciiLetterUpper(isin[1]))
        {
            return "An ISIN starts with two letters for the country.";
        }
        if (!isin[2..11].All(char.IsAsciiLetterOrDigit))
        {
            return "An ISIN has only letters and digits.";
        }
        if (!char.IsAsciiDigit(isin[11]))
        {
            return "An ISIN ends with a check digit.";
        }
        return isin[11] - '0' == CheckDigit(isin[..11]) ? null : "The check digit does not match; there is probably a typo.";
    }

    /// <summary>The check digit for the first eleven characters.</summary>
    public static int CheckDigit(string first11)
    {
        string digits = string.Concat(first11.Select(c => char.IsAsciiDigit(c) ? c.ToString() : (c - 'A' + 10).ToString()));
        int sum = 0;
        // From the right, the digit next to the (missing) check digit is doubled, then every second one
        for (int i = 0; i < digits.Length; i++)
        {
            int digit = digits[digits.Length - 1 - i] - '0';
            if (i % 2 == 0)
            {
                digit *= 2;
                if (digit > 9)
                {
                    digit -= 9;
                }
            }
            sum += digit;
        }
        return (10 - sum % 10) % 10;
    }
}
