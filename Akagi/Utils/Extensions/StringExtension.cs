namespace Akagi.Utils.Extensions;

internal static class StringExtension
{
    public static string Pluralize(this int value, string singular, string plural
        ) => value == 1 ? $"{value} {singular}" : $"{value} {plural}";
}
