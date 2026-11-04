using System.Text;

namespace Akagi.Life.Japanese.Calls;

internal enum FindingKind
{
    /// <summary>Something to work on, including comprehension breakdowns.</summary>
    Mistake,
    /// <summary>A known weak spot the learner got right.</summary>
    Correct,
    Strength,
    /// <summary>A natural expression the partner used that the learner could adopt.</summary>
    Phrase
}

internal enum FindingOrigin
{
    /// <summary>The tutor's review of a transcript chunk.</summary>
    Review,
    /// <summary>The local analyzers: habit words, register, recurring frames.</summary>
    Measurement,
    /// <summary>Copied from the diagnosis for a call analyzed before findings were stored on calls.</summary>
    Legacy
}

/// <summary>
/// Whether the quoted words were found in the transcript, checked in code rather than trusted.
/// </summary>
internal enum QuoteCheck
{
    /// <summary>Nothing quoted, or not checked.</summary>
    Unchecked,
    Found,
    /// <summary>Found with small differences, e.g. a dropped filler.</summary>
    Approximate,
    /// <summary>Found, but the two transcriptions disagree on these words, so the finding is only possible.</summary>
    Disputed,
    NotFound,
    /// <summary>The words were said, but by the other side.</summary>
    OtherSpeaker
}

internal enum FindingState
{
    /// <summary>Noted by a chunk review, not yet consolidated.</summary>
    Candidate,
    Kept,
    /// <summary>The consolidation judged it not worth keeping (not a mistake, transcription error, duplicate).</summary>
    Dropped,
    /// <summary>The quote check failed.</summary>
    Rejected
}

internal enum FindingFeedback
{
    None,
    Useful,
    /// <summary>The user says it is not a mistake; it no longer counts in the diagnosis.</summary>
    NotAMistake
}

internal class CallFinding
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public FindingKind Kind { get; set; }
    public FindingOrigin Origin { get; set; }
    public IssueCategory Category { get; set; } = IssueCategory.Other;
    public string Pattern { get; set; } = string.Empty;
    /// <summary>
    /// The words in the transcript: what the learner said, or the partner's phrase.
    /// </summary>
    public string Said { get; set; } = string.Empty;
    /// <summary>
    /// The natural version, or for a phrase what it means.
    /// </summary>
    public string Better { get; set; } = string.Empty;
    public string Explanation { get; set; } = string.Empty;
    /// <summary>
    /// The consolidation's explanation of the issue as a whole, which the diagnosis keeps for the issue.
    /// </summary>
    public string IssueExplanation { get; set; } = string.Empty;
    public int Severity { get; set; } = 2;
    public int ChunkIndex { get; set; } = -1;
    /// <summary>
    /// Seconds into the call where the quote was found.
    /// </summary>
    public double? Time { get; set; }
    /// <summary>
    /// The diagnosis issue this finding belongs to: suggested by the chunk review, settled by the consolidation.
    /// </summary>
    public string IssueId { get; set; } = string.Empty;
    public QuoteCheck Quote { get; set; }
    public FindingState State { get; set; } = FindingState.Candidate;
    /// <summary>
    /// Why it was dropped or rejected.
    /// </summary>
    public string Note { get; set; } = string.Empty;
    public FindingFeedback Feedback { get; set; }
    /// <summary>
    /// Counted in the diagnosis; taking it back undoes exactly that.
    /// </summary>
    public bool Applied { get; set; }

    public bool Counts => State == FindingState.Kept && Feedback != FindingFeedback.NotAMistake;
}

/// <summary>
/// The consolidation's view of the whole call.
/// </summary>
internal class CallReview
{
    public string Summary { get; set; } = string.Empty;
    public List<string> Priorities { get; set; } = [];
    /// <summary>
    /// The consolidation request returned nothing usable and every checked finding was kept as noted.
    /// </summary>
    public bool Fallback { get; set; }
}

/// <summary>
/// Finds quoted words in the transcript, so a finding about words nobody said, or the partner's words, does not count.
/// </summary>
internal static class CallQuotes
{
    /// <summary>
    /// Share of a quote that must appear in one piece for an approximate match.
    /// </summary>
    public const double ApproximateShare = 0.75;
    private const int MinApproximateLength = 4;

    public static void Check(CallJob job, CallFinding finding)
    {
        if (string.IsNullOrWhiteSpace(finding.Said))
        {
            finding.Quote = QuoteCheck.Unchecked;
            return;
        }

        // A phrase is the partner's; everything else is about the learner's own words
        bool wantLearner = finding.Kind != FindingKind.Phrase;
        (QuoteCheck check, TranscriptSegment? segment) = Locate(job, finding.Said, wantLearner, finding.ChunkIndex);
        if (check is QuoteCheck.NotFound)
        {
            (QuoteCheck other, TranscriptSegment? otherSegment) = Locate(job, finding.Said, !wantLearner, finding.ChunkIndex);
            if (other is QuoteCheck.Found or QuoteCheck.Approximate or QuoteCheck.Disputed)
            {
                check = QuoteCheck.OtherSpeaker;
                segment = otherSegment;
            }
        }
        finding.Quote = check;
        finding.Time = segment?.Start;
    }

    /// <summary>
    /// Rejects findings whose quote is not in the transcript or belongs to the other side. Returns whether it passed.
    /// </summary>
    public static bool Passes(CallFinding finding)
    {
        if (finding.Quote == QuoteCheck.NotFound)
        {
            finding.State = FindingState.Rejected;
            finding.Note = "The quoted words are not in the transcript.";
            return false;
        }
        if (finding.Quote == QuoteCheck.OtherSpeaker)
        {
            finding.State = FindingState.Rejected;
            finding.Note = finding.Kind == FindingKind.Phrase ? "You said this yourself." : "Your partner said this, not you.";
            return false;
        }
        return true;
    }

    internal static (QuoteCheck Check, TranscriptSegment? Segment) Locate(CallJob job, string quote, bool learner, int chunkIndex)
    {
        string wanted = Normalize(quote);
        if (wanted.Length == 0)
        {
            return (QuoteCheck.Unchecked, null);
        }

        // Without separate voices every line may be the learner's, and nothing is the partner's for sure
        List<TranscriptSegment> side = [.. job.Segments.Where(s => !job.HasLearnerSeparation ? learner : job.IsLearnerLine(s) == learner)];
        if (side.Count == 0)
        {
            return (QuoteCheck.NotFound, null);
        }

        // The chunk the finding came from first, then the rest of the call
        CallChunk? chunk = chunkIndex >= 0 && chunkIndex < job.Chunks.Count ? job.Chunks[chunkIndex] : null;
        IEnumerable<TranscriptSegment> ordered = chunk == null
            ? side
            : side.OrderBy(s => s.Start >= chunk.Start && s.Start <= chunk.End ? 0 : 1);
        List<TranscriptSegment> candidates = [.. ordered];

        // A quote may run over two or three consecutive lines
        foreach (int span in new[] { 1, 2, 3 })
        {
            foreach (TranscriptSegment segment in candidates)
            {
                List<TranscriptSegment> run = Run(side, segment, span);
                if (run.Count < span)
                {
                    continue;
                }
                string text = Normalize(string.Concat(run.Select(s => s.Text)));
                if (text.Contains(wanted, StringComparison.Ordinal))
                {
                    return (IsDisputed(run, wanted) ? QuoteCheck.Disputed : QuoteCheck.Found, segment);
                }
            }
        }

        if (wanted.Length >= MinApproximateLength)
        {
            (TranscriptSegment? best, int length) = (null, 0);
            foreach (TranscriptSegment segment in candidates)
            {
                List<TranscriptSegment> run = Run(side, segment, 2);
                int common = LongestCommonSubstring(wanted, Normalize(string.Concat(run.Select(s => s.Text))));
                if (common > length)
                {
                    (best, length) = (segment, common);
                }
            }
            if (best != null && length >= Math.Ceiling(wanted.Length * ApproximateShare))
            {
                return (IsDisputed(Run(side, best, 2), wanted) ? QuoteCheck.Disputed : QuoteCheck.Approximate, best);
            }
        }
        return (QuoteCheck.NotFound, null);
    }

    private static List<TranscriptSegment> Run(List<TranscriptSegment> side, TranscriptSegment first, int span)
    {
        int index = side.IndexOf(first);
        return side.GetRange(index, Math.Min(span, side.Count - index));
    }

    /// <summary>
    /// The second hearing heard something else in these words: "A -> B; C -> D".
    /// </summary>
    private static bool IsDisputed(IEnumerable<TranscriptSegment> run, string wanted)
    {
        foreach (TranscriptSegment segment in run.Where(s => !string.IsNullOrWhiteSpace(s.Disputed)))
        {
            foreach (string difference in segment.Disputed.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                foreach (string side in difference.Split("->", StringSplitOptions.TrimEntries))
                {
                    string words = Normalize(side);
                    if (words.Length > 0 && (wanted.Contains(words, StringComparison.Ordinal) || words.Contains(wanted, StringComparison.Ordinal)))
                    {
                        return true;
                    }
                }
            }
        }
        return false;
    }

    /// <summary>
    /// Letters and digits only, katakana as hiragana: punctuation, spaces and the script of a kana word do not matter.
    /// </summary>
    public static string Normalize(string text)
    {
        StringBuilder normalized = new(text.Length);
        foreach (char c in JapaneseTokenizer.ToHiragana(text.Normalize(NormalizationForm.FormKC)))
        {
            if (char.IsLetterOrDigit(c) || c == 'ー')
            {
                normalized.Append(char.ToLowerInvariant(c));
            }
        }
        return normalized.ToString();
    }

    internal static int LongestCommonSubstring(string a, string b)
    {
        if (a.Length == 0 || b.Length == 0)
        {
            return 0;
        }
        int[] previous = new int[b.Length + 1];
        int[] current = new int[b.Length + 1];
        int best = 0;
        for (int i = 1; i <= a.Length; i++)
        {
            for (int j = 1; j <= b.Length; j++)
            {
                current[j] = a[i - 1] == b[j - 1] ? previous[j - 1] + 1 : 0;
                best = Math.Max(best, current[j]);
            }
            (previous, current) = (current, previous);
            Array.Clear(current);
        }
        return best;
    }
}

/// <summary>
/// Moves a call's findings into the long-term diagnosis and back out again.
/// </summary>
internal static class CallDiagnosis
{
    public static void Apply(JapaneseDiagnosis diagnosis, CallJob job, CallFinding finding, DateTime nowUtc)
    {
        if (finding.Applied || !finding.Counts)
        {
            return;
        }

        switch (finding.Kind)
        {
            case FindingKind.Mistake:
                IssueExample example = new() { Said = finding.Said, Better = finding.Better, Source = job.SourceLabel, Time = nowUtc, FindingId = finding.Id };
                if (diagnosis.GetIssue(finding.IssueId) is DiagnosisIssue known)
                {
                    diagnosis.RecordOn(known, IssueExplanation(finding), example, finding.Severity, nowUtc);
                }
                else
                {
                    (known, _) = diagnosis.RecordIssue(finding.Category, finding.Pattern, IssueExplanation(finding), example, finding.Severity, nowUtc);
                }
                finding.IssueId = known.Id;
                finding.Applied = true;
                break;

            case FindingKind.Correct:
                if ((diagnosis.GetIssue(finding.IssueId) ?? diagnosis.FindIssue(finding.Pattern)) is DiagnosisIssue issue)
                {
                    issue.RecordCorrect(nowUtc);
                    finding.IssueId = issue.Id;
                    finding.Applied = true;
                    diagnosis.Dirty = true;
                }
                break;

            case FindingKind.Strength:
                diagnosis.RecordStrength(finding.Pattern, nowUtc);
                finding.Applied = true;
                break;

            // Phrases are kept on the call; the diagnosis tracks weaknesses and strengths
            case FindingKind.Phrase:
                break;
        }
    }

    private static string IssueExplanation(CallFinding finding) =>
        string.IsNullOrWhiteSpace(finding.IssueExplanation) ? finding.Explanation : finding.IssueExplanation;

    public static void Undo(JapaneseDiagnosis diagnosis, CallJob job, CallFinding finding)
    {
        if (!finding.Applied)
        {
            return;
        }
        finding.Applied = false;

        switch (finding.Kind)
        {
            case FindingKind.Mistake when diagnosis.GetIssue(finding.IssueId) is DiagnosisIssue issue:
                issue.UndoMistake(finding.Origin == FindingOrigin.Legacy ? string.Empty : finding.Id, job.SourceLabel, finding.Said);
                diagnosis.RemoveIfEmpty(issue);
                break;

            case FindingKind.Correct when diagnosis.GetIssue(finding.IssueId) is DiagnosisIssue issue:
                issue.UndoCorrect();
                diagnosis.Dirty = true;
                break;

            case FindingKind.Strength:
                diagnosis.UndoStrength(finding.Pattern);
                break;
        }
    }

    /// <summary>
    /// The user's say on a finding. Saying it is not a mistake takes it out of the diagnosis; changing their mind puts
    /// it back.
    /// </summary>
    public static void SetFeedback(JapaneseDiagnosis diagnosis, CallJob job, CallFinding finding, FindingFeedback feedback, DateTime nowUtc)
    {
        finding.Feedback = feedback;
        if (feedback == FindingFeedback.NotAMistake)
        {
            Undo(diagnosis, job, finding);
        }
        else
        {
            Apply(diagnosis, job, finding, nowUtc);
        }
        diagnosis.Dirty = true;
    }

    /// <summary>
    /// Copies what is still in the diagnosis about a call analyzed before findings were stored on calls, so it stops
    /// fading as the diagnosis replaces old examples. Returns whether anything was copied.
    /// </summary>
    public static bool ImportLegacy(CallJob job, JapaneseDiagnosis? diagnosis)
    {
        if (job.LegacyImported || job.Findings.Count > 0 || job.Status != CallJobStatus.Completed)
        {
            return false;
        }
        job.LegacyImported = true;
        if (diagnosis == null)
        {
            return true;
        }

        string source = job.SourceLabel;
        foreach (DiagnosisIssue issue in diagnosis.Issues)
        {
            foreach (IssueExample example in issue.Examples.Where(e => e.Source == source))
            {
                CallFinding finding = new()
                {
                    Kind = FindingKind.Mistake,
                    Origin = FindingOrigin.Legacy,
                    Category = issue.Category,
                    Pattern = issue.Pattern,
                    Said = example.Said,
                    Better = example.Better,
                    Explanation = issue.Explanation,
                    Severity = issue.Severity,
                    IssueId = issue.Id,
                    State = FindingState.Kept,
                    // Already counted in the diagnosis when the call was analyzed
                    Applied = true
                };
                CallQuotes.Check(job, finding);
                job.Findings.Add(finding);
            }
        }
        return true;
    }
}
