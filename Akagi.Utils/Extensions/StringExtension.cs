using System.Text;

namespace Akagi.Utils.Extensions;

public static class StringExtension
{
    public static string Pluralize(this int value, string singular, string plural
        ) => value == 1 ? $"{value} {singular}" : $"{value} {plural}";


    public static string Stringify(this TimeSpan timeSpan)
    {
        if (timeSpan.TotalDays >= 1)
        {
            return $"~{timeSpan.Days.Pluralize("day", "days")}";
        }
        else if (timeSpan.TotalHours >= 1)
        {
            return $"~{timeSpan.Hours.Pluralize("hour", "hours")}";
        }
        else if (timeSpan.TotalMinutes >= 1)
        {
            return $"~{timeSpan.Minutes.Pluralize("minute", "minutes")}";
        }
        else
        {
            return $"~{timeSpan.Seconds.Pluralize("second", "seconds")}";
        }
    }

    public static string StringifyPrecise(this TimeSpan timeSpan)
    {
        StringBuilder sb = new();
        if (timeSpan.Days > 0)
        {
            sb.Append($"{timeSpan.Days} day{(timeSpan.Days > 1 ? "s" : "")}, ");
        }
        if (timeSpan.Hours > 0)
        {
            sb.Append($"{timeSpan.Hours} hour{(timeSpan.Hours > 1 ? "s" : "")}, ");
        }
        if (timeSpan.Minutes > 0)
        {
            sb.Append($"{timeSpan.Minutes} minute{(timeSpan.Minutes > 1 ? "s" : "")}, ");
        }
        sb.Append($"{timeSpan.Seconds} second{(timeSpan.Seconds > 1 ? "s" : "")}");
        return sb.ToString().TrimEnd(',', ' ');
    }
}
