using Akagi.Bridge.Chat.Models;
using System.Globalization;
using System.Web;

namespace Akagi.Web.Pages.Health;

public enum GoogleHealthState
{
    NotConfigured,
    NotLinked,
    Expired,
    /// <summary>The link runs out within <see cref="GoogleHealthView.WarnBefore"/>.</summary>
    Soon,
    Ok
}

/// <summary>
/// How the dashboard words a Google Health link.
/// </summary>
public static class GoogleHealthView
{
    public static readonly TimeSpan WarnBefore = TimeSpan.FromDays(2);

    public const string CallbackPath = "Health/Callback";

    public static GoogleHealthState State(GoogleHealthStatus status, DateTime nowUtc)
    {
        if (!status.Configured)
        {
            return GoogleHealthState.NotConfigured;
        }
        if (!status.Linked)
        {
            // A link that existed and was dropped by Google leaves its error behind
            return string.IsNullOrEmpty(status.LastError) ? GoogleHealthState.NotLinked : GoogleHealthState.Expired;
        }
        if (status.ExpiresAtUtc is not { } expires)
        {
            return GoogleHealthState.Ok;
        }
        if (expires <= nowUtc)
        {
            return GoogleHealthState.Expired;
        }
        return expires - nowUtc <= WarnBefore ? GoogleHealthState.Soon : GoogleHealthState.Ok;
    }

    /// <summary>
    /// "3 days", "20 hours", "less than an hour".
    /// </summary>
    public static string Remaining(TimeSpan left)
    {
        if (left.TotalHours < 1)
        {
            return "less than an hour";
        }
        if (left.TotalHours < 48)
        {
            int hours = (int)left.TotalHours;
            return hours == 1 ? "1 hour" : $"{hours} hours";
        }
        int days = (int)left.TotalDays;
        return $"{days} days";
    }

    public static string When(DateTime utc) =>
        utc.ToLocalTime().ToString("ddd d MMM, HH:mm", CultureInfo.InvariantCulture);

    /// <summary>
    /// Whether Google sends the browser back to this website after consent, so nothing has to be pasted.
    /// </summary>
    public static bool ReturnsHere(string authorizationUrl, string baseUri)
    {
        int query = authorizationUrl.IndexOf('?');
        if (query < 0)
        {
            return false;
        }
        string? redirect = HttpUtility.ParseQueryString(authorizationUrl[(query + 1)..])["redirect_uri"];
        return redirect != null && redirect.TrimEnd('/').Equals((baseUri + CallbackPath).TrimEnd('/'), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A pasted address that can be sent as it is.
    /// </summary>
    public static bool LooksComplete(string? pasted) =>
        pasted != null && pasted.Contains("code=", StringComparison.Ordinal) && pasted.Contains("state=", StringComparison.Ordinal);
}
