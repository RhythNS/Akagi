using System.Globalization;

namespace Akagi.Life;

/// <summary>
/// Time zone helpers. All stored times are UTC; anything user facing (days, mornings, quiet hours) uses local time.
/// </summary>
internal static class LifeClock
{
    public static TimeZoneInfo FindTimeZone(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return TimeZoneInfo.Utc;
        }
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return TimeZoneInfo.Utc;
        }
    }

    public static bool IsValidTimeZone(string id)
    {
        try
        {
            TimeZoneInfo.FindSystemTimeZoneById(id);
            return true;
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return false;
        }
    }

    public static DateTime ToLocal(DateTime utc, TimeZoneInfo zone)
    {
        DateTime asUtc = utc.Kind == DateTimeKind.Utc ? utc : DateTime.SpecifyKind(utc, DateTimeKind.Utc);
        return TimeZoneInfo.ConvertTimeFromUtc(asUtc, zone);
    }

    public static DateTime ToUtc(DateTime local, TimeZoneInfo zone)
    {
        DateTime unspecified = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        if (zone.IsInvalidTime(unspecified))
        {
            unspecified = unspecified.AddHours(1);
        }
        return TimeZoneInfo.ConvertTimeToUtc(unspecified, zone);
    }

    public static string DateKey(DateTime local) => local.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static DateTime ParseDateKey(string key) => DateTime.ParseExact(key, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>
    /// Parses "HH:mm" into minutes after midnight.
    /// </summary>
    public static bool TryParseTimeOfDay(string? text, out int minutes)
    {
        minutes = 0;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }
        if (TimeSpan.TryParseExact(text.Trim(), [@"h\:mm", @"hh\:mm"], CultureInfo.InvariantCulture, out TimeSpan time)
            && time >= TimeSpan.Zero && time < TimeSpan.FromDays(1))
        {
            minutes = (int)time.TotalMinutes;
            return true;
        }
        return false;
    }

    /// <summary>
    /// True when <paramref name="localMinutes"/> lies inside [start, end), handling ranges that wrap past midnight.
    /// </summary>
    public static bool IsInRange(int localMinutes, int startMinutes, int endMinutes)
    {
        if (startMinutes == endMinutes)
        {
            return false;
        }
        return startMinutes < endMinutes
            ? localMinutes >= startMinutes && localMinutes < endMinutes
            : localMinutes >= startMinutes || localMinutes < endMinutes;
    }

    public static string FormatDuration(TimeSpan duration)
    {
        if (duration < TimeSpan.Zero)
        {
            duration = TimeSpan.Zero;
        }
        int hours = (int)duration.TotalHours;
        return hours > 0 ? $"{hours}h{duration.Minutes:00}m" : $"{duration.Minutes}m";
    }

    public static string FormatAge(TimeSpan age)
    {
        if (age.TotalMinutes < 1)
        {
            return "just now";
        }
        if (age.TotalHours < 1)
        {
            return $"{(int)age.TotalMinutes}m ago";
        }
        if (age.TotalDays < 2)
        {
            return $"{(int)age.TotalHours}h ago";
        }
        return $"{(int)age.TotalDays} days ago";
    }
}
