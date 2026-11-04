using Akagi.Bridge.Chat.Models;
using System.Globalization;

namespace Akagi.Web.Pages.Learning.Components;

/// <summary>
/// How call analyses are put into words and colors on the website.
/// </summary>
public static class CallText
{
    public static string Name(CallAnalysisSummary call) =>
        !string.IsNullOrWhiteSpace(call.Partner) ? $"Call with {call.Partner}"
        : !string.IsNullOrWhiteSpace(call.Title) ? call.Title
        : "Call";

    public static string Duration(double seconds)
    {
        TimeSpan span = TimeSpan.FromSeconds(seconds);
        return span.TotalHours >= 1 ? $"{(int)span.TotalHours} h {span.Minutes} min"
            : span.TotalMinutes >= 1 ? $"{span.Minutes} min"
            : $"{span.Seconds} s";
    }

    public static string Timestamp(double seconds) =>
        TimeSpan.FromSeconds(seconds).ToString(seconds >= 3600 ? @"h\:mm\:ss" : @"mm\:ss", CultureInfo.InvariantCulture);

    public static string StateLabel(CallAnalysisState state) => state switch
    {
        CallAnalysisState.Queued => "Queued",
        CallAnalysisState.Transcribing => "Transcribing",
        CallAnalysisState.Analyzing => "Analyzing",
        CallAnalysisState.Reporting => "Reporting",
        CallAnalysisState.Completed => "Done",
        _ => "Failed"
    };

    public static string StateClass(CallAnalysisState state) => state switch
    {
        CallAnalysisState.Completed => "bg-success-lt",
        CallAnalysisState.Failed => "bg-danger-lt",
        _ => "bg-azure-lt"
    };

    public static string SeverityLabel(int severity) => severity switch
    {
        >= 3 => "Serious",
        2 => "Noticeable",
        _ => "Polish"
    };

    public static string SeverityClass(int severity) => severity switch
    {
        >= 3 => "bg-red-lt",
        2 => "bg-orange-lt",
        _ => "bg-secondary-lt"
    };

    public static string StatusClass(string status) => status switch
    {
        "resolved" => "bg-success-lt",
        "improving" => "bg-teal-lt",
        _ => "bg-secondary-lt"
    };

    public static string Percent(double ratio) => ratio.ToString("P0", CultureInfo.InvariantCulture);

    public static string Number(double value) => value.ToString("0.0", CultureInfo.InvariantCulture);
}
