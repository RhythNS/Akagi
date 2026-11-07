using Akagi.Bridge.Chat.Models;
using Akagi.Life.Japanese.Calls;

namespace Akagi.Life.Japanese.Vocabulary;

/// <summary>
/// Finds any piece of text in the transcripts, also ones that are not one word (それも, みたいな感じ, かもしれない): how
/// often the user and the partners said it, per call and per hour of speaking, and every line with its time.
/// </summary>
internal static class ExpressionSearch
{
    public const int MaxLines = 300;
    public const int MaxQueryLength = 40;

    public static VocabExpressionResult Run(VocabularyView view, string query)
    {
        string wanted = query.Trim();
        VocabExpressionResult result = new() { Query = wanted };
        if (wanted.Length == 0)
        {
            return result;
        }

        List<VocabLine> lines = [];
        double learnerSeconds = 0;
        double partnerSeconds = 0;
        foreach (CallTokens call in view.Calls)
        {
            Dictionary<int, bool> learnerSegments = call.Tokens
                .GroupBy(t => t.Segment)
                .ToDictionary(g => g.Key, g => g.First().Learner);
            VocabExpressionCall row = new() { CallId = call.CallId, Name = call.Name, Date = call.Date, HasPartner = call.HasPartner };
            for (int index = 0; index < call.Segments.Count; index++)
            {
                TranscriptSegment segment = call.Segments[index];
                (int count, string? form) = Count(segment.Text, wanted);
                if (count == 0)
                {
                    continue;
                }
                bool learner = learnerSegments.GetValueOrDefault(index, true);
                if (learner)
                {
                    row.Learner += count;
                }
                else
                {
                    row.Partner += count;
                }
                lines.Add(new VocabLine
                {
                    CallId = call.CallId,
                    CallName = call.Name,
                    Date = call.Date,
                    Time = segment.Start,
                    IsLearner = learner,
                    Speaker = learner ? "You" : (call.Partner.Length > 0 ? call.Partner : "Partner"),
                    Text = segment.Text.Trim(),
                    Marks = form == null ? [] : [form]
                });
            }
            row.LearnerPerHour = PerHour(row.Learner, call.LearnerSeconds);
            row.PartnerPerHour = call.HasPartner ? PerHour(row.Partner, call.PartnerSeconds) : null;
            learnerSeconds += call.LearnerSeconds;
            partnerSeconds += call.HasPartner ? call.PartnerSeconds : 0;
            result.Learner += row.Learner;
            result.Partner += row.Partner;
            result.Calls = [.. result.Calls, row];
        }

        result.LearnerPerHour = PerHour(result.Learner, learnerSeconds);
        result.PartnerPerHour = partnerSeconds > 0 ? PerHour(result.Partner, partnerSeconds) : null;
        result.TotalLines = lines.Count;
        result.Lines = [.. lines
            .OrderByDescending(l => l.Date)
            .ThenBy(l => l.CallId, StringComparer.Ordinal)
            .ThenBy(l => l.Time)
            .Take(MaxLines)];
        return result;
    }

    /// <summary>
    /// Non-overlapping matches, ignoring case and full/half width, and the text as it stands in the line for marking.
    /// </summary>
    internal static (int Count, string? Form) Count(string text, string query)
    {
        string haystack = text.Normalize(System.Text.NormalizationForm.FormKC);
        string needle = query.Normalize(System.Text.NormalizationForm.FormKC);
        int count = 0;
        string? form = null;
        int at = 0;
        while ((at = haystack.IndexOf(needle, at, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            form ??= haystack.Substring(at, needle.Length);
            count++;
            at += needle.Length;
        }
        // The line is shown as written, so the mark must be found in it too
        if (form != null && !text.Contains(form, StringComparison.Ordinal))
        {
            form = text.Contains(query, StringComparison.OrdinalIgnoreCase) ? query : null;
        }
        return (count, form);
    }

    private static double? PerHour(int count, double seconds) => seconds <= 0 ? null : Math.Round(count * 3600 / seconds, 1);
}
