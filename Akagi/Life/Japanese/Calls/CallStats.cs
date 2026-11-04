namespace Akagi.Life.Japanese.Calls;

/// <summary>
/// How long each side spoke in one stretch of the call.
/// </summary>
internal class CallWindow
{
    public double Start { get; set; }
    public double LearnerSeconds { get; set; }
    public double PartnerSeconds { get; set; }
}

/// <summary>
/// One dictionary word and how often each side said it.
/// </summary>
internal class CallWord
{
    public string Lemma { get; set; } = string.Empty;
    public string Reading { get; set; } = string.Empty;
    public WordClass Class { get; set; }
    public int Learner { get; set; }
    public int Partner { get; set; }
    /// <summary>
    /// The forms it was said in (忙しく, 忙しかった), to find it in the transcript.
    /// </summary>
    public List<string> Surfaces { get; set; } = [];
}

/// <summary>
/// What the transcript shows without any LLM: who spoke how much, how turns went, and which words were used.
/// Computed locally, so it is recomputed for free whenever <see cref="CurrentVersion"/> changes.
/// </summary>
internal class CallStats
{
    /// <summary>
    /// Raised whenever the measuring changes; stored statistics of an older version are computed again.
    /// </summary>
    public const int CurrentVersion = 2;
    public const double WindowSeconds = 300;
    public const double LongPauseSeconds = 2.0;
    /// <summary>
    /// Longer gaps before an answer are a change of topic or silence, not a reaction time.
    /// </summary>
    public const double MaxResponseSeconds = 10;
    private const int MaxSurfaces = 6;

    public int Version { get; set; } = CurrentVersion;
    /// <summary>
    /// The learner's lines are known. Without that, everything counts as the learner and the flow numbers are empty.
    /// </summary>
    public bool Separated { get; set; }
    public bool HasPartner { get; set; }

    public double LearnerSeconds { get; set; }
    public double PartnerSeconds { get; set; }
    public int LearnerCharacters { get; set; }
    public int PartnerCharacters { get; set; }
    public int LearnerTurns { get; set; }
    public int PartnerTurns { get; set; }
    public double LongestLearnerTurnSeconds { get; set; }
    /// <summary>
    /// Median gap between the partner finishing and the learner starting to answer.
    /// </summary>
    public double? MedianResponseSeconds { get; set; }
    /// <summary>
    /// Learner turns that began while the partner was still speaking: back-channeling or cutting in.
    /// </summary>
    public int LearnerOverlaps { get; set; }
    /// <summary>
    /// Silences of <see cref="LongPauseSeconds"/> or more inside the learner's own turns.
    /// </summary>
    public int LongPauses { get; set; }
    public List<CallWindow> Windows { get; set; } = [];

    public int LearnerWords { get; set; }
    public int PartnerWords { get; set; }
    public List<CallWord> Words { get; set; } = [];

    public double? LearnerShare => HasPartner && LearnerSeconds + PartnerSeconds > 0 ? LearnerSeconds / (LearnerSeconds + PartnerSeconds) : null;
    public int LearnerDistinct => Words.Count(w => w.Learner > 0);
    public int PartnerDistinct => Words.Count(w => w.Partner > 0);
    public double LearnerCharactersPerTurn => LearnerTurns == 0 ? 0 : (double)LearnerCharacters / LearnerTurns;
    public double PartnerCharactersPerTurn => PartnerTurns == 0 ? 0 : (double)PartnerCharacters / PartnerTurns;

    public static CallStats Compute(CallJob job)
    {
        List<(TranscriptSegment Segment, bool Learner)> lines = [.. job.Segments
            .OrderBy(s => s.Start)
            .Select(s => (s, job.IsLearnerLine(s)))];

        CallStats stats = new() { Separated = job.HasLearnerSeparation, HasPartner = lines.Any(l => !l.Learner) };
        stats.MeasureTime(lines);
        stats.MeasureTurns(lines);
        stats.CountWords(lines);
        return stats;
    }

    private void MeasureTime(List<(TranscriptSegment Segment, bool Learner)> lines)
    {
        List<(double Start, double End)> learner = Union(lines.Where(l => l.Learner).Select(l => l.Segment));
        List<(double Start, double End)> partner = Union(lines.Where(l => !l.Learner).Select(l => l.Segment));
        LearnerSeconds = learner.Sum(i => i.End - i.Start);
        PartnerSeconds = partner.Sum(i => i.End - i.Start);
        LearnerCharacters = lines.Where(l => l.Learner).Sum(l => Letters(l.Segment.Text));
        PartnerCharacters = lines.Where(l => !l.Learner).Sum(l => Letters(l.Segment.Text));

        double end = lines.Count == 0 ? 0 : lines.Max(l => l.Segment.End);
        for (double start = 0; start < end; start += WindowSeconds)
        {
            Windows.Add(new CallWindow
            {
                Start = start,
                LearnerSeconds = Math.Round(Overlap(learner, start, start + WindowSeconds), 1),
                PartnerSeconds = Math.Round(Overlap(partner, start, start + WindowSeconds), 1)
            });
        }
    }

    private void MeasureTurns(List<(TranscriptSegment Segment, bool Learner)> lines)
    {
        List<double> responses = [];
        int i = 0;
        double previousPartnerEnd = double.NaN;
        while (i < lines.Count)
        {
            bool learner = lines[i].Learner;
            int first = i;
            double turnEnd = lines[i].Segment.End;
            while (i + 1 < lines.Count && lines[i + 1].Learner == learner)
            {
                i++;
                if (learner && lines[i].Segment.Start - turnEnd >= LongPauseSeconds)
                {
                    LongPauses++;
                }
                turnEnd = Math.Max(turnEnd, lines[i].Segment.End);
            }

            double turnStart = lines[first].Segment.Start;
            if (learner)
            {
                LearnerTurns++;
                LongestLearnerTurnSeconds = Math.Max(LongestLearnerTurnSeconds, turnEnd - turnStart);
                if (!double.IsNaN(previousPartnerEnd))
                {
                    double gap = turnStart - previousPartnerEnd;
                    if (gap < 0)
                    {
                        LearnerOverlaps++;
                    }
                    else if (gap <= MaxResponseSeconds)
                    {
                        responses.Add(gap);
                    }
                }
            }
            else
            {
                PartnerTurns++;
                previousPartnerEnd = turnEnd;
            }
            i++;
        }

        if (responses.Count > 0)
        {
            responses.Sort();
            int middle = responses.Count / 2;
            MedianResponseSeconds = Math.Round(responses.Count % 2 == 1 ? responses[middle] : (responses[middle - 1] + responses[middle]) / 2, 2);
        }
    }

    private void CountWords(List<(TranscriptSegment Segment, bool Learner)> lines)
    {
        Dictionary<(string Lemma, WordClass Class), CallWord> words = [];
        foreach ((TranscriptSegment segment, bool learner) in lines)
        {
            foreach (JapaneseToken token in JapaneseTokenizer.Tokenize(segment.Text))
            {
                if (token.Class is not WordClass wordClass || !token.Lemma.Any(JapaneseText.IsJapanese))
                {
                    continue;
                }
                if (!words.TryGetValue((token.Lemma, wordClass), out CallWord? word))
                {
                    word = new CallWord { Lemma = token.Lemma, Class = wordClass, Reading = JapaneseTokenizer.ReadingOf(token.Lemma) };
                    words[(token.Lemma, wordClass)] = word;
                }
                if (learner)
                {
                    word.Learner++;
                    LearnerWords++;
                }
                else
                {
                    word.Partner++;
                    PartnerWords++;
                }
                if (word.Surfaces.Count < MaxSurfaces && !word.Surfaces.Contains(token.Surface))
                {
                    word.Surfaces.Add(token.Surface);
                }
            }
        }
        Words = [.. words.Values.OrderByDescending(w => w.Learner + w.Partner).ThenBy(w => w.Lemma, StringComparer.Ordinal)];
    }

    private static int Letters(string text) => text.Count(char.IsLetterOrDigit);

    private static List<(double Start, double End)> Union(IEnumerable<TranscriptSegment> segments)
    {
        List<(double Start, double End)> merged = [];
        foreach (TranscriptSegment segment in segments.Where(s => s.End > s.Start).OrderBy(s => s.Start))
        {
            if (merged.Count > 0 && segment.Start <= merged[^1].End)
            {
                merged[^1] = (merged[^1].Start, Math.Max(merged[^1].End, segment.End));
            }
            else
            {
                merged.Add((segment.Start, segment.End));
            }
        }
        return merged;
    }

    private static double Overlap(List<(double Start, double End)> intervals, double from, double to) =>
        intervals.Sum(i => Math.Max(0, Math.Min(i.End, to) - Math.Max(i.Start, from)));
}
