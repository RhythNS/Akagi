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
        CallAnalysisState.Consolidating => "Merging",
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

    /// <summary>
    /// What the quote check found, for a badge; empty when there is nothing to point out.
    /// </summary>
    public static string QuoteLabel(CallQuoteState quote) => quote switch
    {
        CallQuoteState.Approximate => "quote approximate",
        CallQuoteState.Possibly => "possibly",
        CallQuoteState.NotFound => "quote not found",
        CallQuoteState.OtherSpeaker => "other speaker",
        _ => string.Empty
    };

    public static string QuoteTitle(CallQuoteState quote) => quote switch
    {
        CallQuoteState.Approximate => "The quote was found with small differences, e.g. a dropped filler.",
        CallQuoteState.Possibly => "The two transcriptions disagree on these words, so this is only possible.",
        CallQuoteState.NotFound => "The quoted words are not in the transcript.",
        CallQuoteState.OtherSpeaker => "The quoted words were said by the other side.",
        _ => string.Empty
    };

    public static string QuoteClass(CallQuoteState quote) => quote switch
    {
        CallQuoteState.Possibly or CallQuoteState.Approximate => "bg-yellow-lt",
        CallQuoteState.NotFound or CallQuoteState.OtherSpeaker => "bg-red-lt",
        _ => "bg-secondary-lt"
    };

    public static string ClassLabel(string wordClass) => wordClass switch
    {
        "Noun" => "noun",
        "Verb" => "verb",
        "Adjective" => "i-adjective",
        "AdjectivalNoun" => "na-adjective",
        "Adverb" => "adverb",
        "Adnominal" => "adnominal",
        "Conjunction" => "conjunction",
        "Interjection" => "interjection",
        _ => wordClass.ToLowerInvariant()
    };

    /// <summary>
    /// The default word filter: words that carry meaning, without back-channel words such as うん and はい.
    /// </summary>
    public const string ContentFilter = "Content";

    private static readonly string[] ContentClasses = ["Noun", "Verb", "Adjective", "AdjectivalNoun", "Adverb"];

    /// <summary>
    /// Whether a word of <paramref name="wordClass"/> passes the filter: empty for all, <see cref="ContentFilter"/>, or one class.
    /// </summary>
    public static bool MatchesClass(string filter, string wordClass) =>
        filter.Length == 0 || (filter == ContentFilter ? ContentClasses.Contains(wordClass) : wordClass == filter);

    public static readonly string[] WordClasses = ["Noun", "Verb", "Adjective", "AdjectivalNoun", "Adverb", "Adnominal", "Conjunction", "Interjection"];

    public static string Seconds(double seconds) => seconds.ToString(seconds < 10 ? "0.0" : "0", CultureInfo.InvariantCulture) + " s";

    public static string Percent(double ratio) => (ratio * 100).ToString("0", CultureInfo.InvariantCulture) + "%";

    public static string Number(double value) => value.ToString("0.0", CultureInfo.InvariantCulture);
}
