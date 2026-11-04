using Akagi.Life.Japanese.Calls;
using System.Collections.Concurrent;

namespace Akagi.Life.Japanese.Vocabulary;

/// <summary>
/// One token where it was said: which sentence and transcript line, when, and by whom.
/// </summary>
internal readonly record struct VocabToken(int Sentence, int Segment, double Time, bool Learner, DetailedToken Token);

/// <summary>
/// A sentence as the token range [<see cref="Start"/>, <see cref="End"/>) of one speaker's line.
/// </summary>
internal readonly record struct VocabSentence(int Start, int End, int Segment, bool Learner, double Time, bool Question);

/// <summary>
/// Every token of one call, in order. Built locally from the transcript, so it costs nothing to rebuild.
/// </summary>
internal class CallTokens
{
    /// <summary>
    /// Raised when the way tokens are taken changes; cached ones of an older version are built again.
    /// </summary>
    public const int Version = 1;

    private static readonly HashSet<string> SentenceEnds = ["。", "．", ".", "！", "!", "？", "?", "…"];

    public required string CallId { get; init; }
    public required string Name { get; init; }
    public required string Partner { get; init; }
    public required DateTime Date { get; init; }
    public required DateTime CreatedAt { get; init; }
    public required bool HasPartner { get; init; }
    public double LearnerSeconds { get; init; }
    public double PartnerSeconds { get; init; }
    public List<VocabToken> Tokens { get; } = [];
    public List<VocabSentence> Sentences { get; } = [];
    /// <summary>
    /// The transcript lines, to show where a word was said.
    /// </summary>
    public List<TranscriptSegment> Segments { get; init; } = [];

    public static CallTokens Build(CallJob job)
    {
        CallStats stats = job.Stats ?? CallStats.Compute(job);
        CallTokens call = new()
        {
            CallId = job.Id!,
            Name = string.IsNullOrWhiteSpace(job.Partner) ? (string.IsNullOrWhiteSpace(job.Title) ? "Call" : job.Title) : $"Call with {job.Partner}",
            Partner = job.Partner.Trim(),
            Date = job.RecordedAt ?? job.CreatedAt,
            CreatedAt = job.CreatedAt,
            HasPartner = job.HasBothVoices,
            LearnerSeconds = stats.LearnerSeconds,
            PartnerSeconds = stats.PartnerSeconds,
            Segments = [.. job.Segments.OrderBy(s => s.Start)]
        };

        for (int segmentIndex = 0; segmentIndex < call.Segments.Count; segmentIndex++)
        {
            TranscriptSegment segment = call.Segments[segmentIndex];
            bool learner = job.IsLearnerLine(segment);
            int start = call.Tokens.Count;
            foreach (DetailedToken token in JapaneseTokenizer.TokenizeDetailed(segment.Text))
            {
                call.Tokens.Add(new VocabToken(call.Sentences.Count, segmentIndex, segment.Start, learner, token));
                if (token.IsPunctuation && SentenceEnds.Contains(token.Surface))
                {
                    call.EndSentence(start, segmentIndex, learner, segment.Start, token.Surface is "？" or "?");
                    start = call.Tokens.Count;
                }
            }
            // A line ends a sentence even without punctuation
            call.EndSentence(start, segmentIndex, learner, segment.Start, false);
        }
        return call;
    }

    private void EndSentence(int start, int segment, bool learner, double time, bool question)
    {
        if (Tokens.Count == start || Tokens.Skip(start).All(t => t.Token.IsPunctuation))
        {
            return;
        }
        Sentences.Add(new VocabSentence(start, Tokens.Count, segment, learner, time, question));
    }

    /// <summary>
    /// The words of a sentence as they were said.
    /// </summary>
    public string Text(VocabSentence sentence) =>
        string.Concat(Tokens.Skip(sentence.Start).Take(sentence.End - sentence.Start).Select(t => t.Token.Surface));
}

/// <summary>
/// All of a user's calls as tokens, for the vocabulary page.
/// </summary>
internal interface IVocabularyIndex
{
    /// <summary>
    /// Finished calls whose learner voice is known, oldest first, and how many finished calls were left out because it
    /// is not.
    /// </summary>
    public Task<(List<CallTokens> Calls, int Mixed)> LoadAsync(string userId);
}

/// <summary>
/// Keeps every call's tokens in memory, built on first use and again whenever the call changed. Tokenizing an hour of
/// conversation takes a fraction of a second, so nothing is stored.
/// </summary>
internal class VocabularyIndex : IVocabularyIndex
{
    private readonly ICallJobDatabase _jobs;
    private readonly ConcurrentDictionary<string, (DateTime Updated, int Version, CallTokens Tokens)> _cache = new();

    public VocabularyIndex(ICallJobDatabase jobs)
    {
        _jobs = jobs;
    }

    public async Task<(List<CallTokens> Calls, int Mixed)> LoadAsync(string userId)
    {
        List<CallJob> finished = [.. (await _jobs.GetForUser(userId)).Where(j => j.Status == CallJobStatus.Completed && j.Segments.Count > 0)];
        List<CallTokens> calls = [];
        foreach (CallJob job in finished.Where(j => j.HasLearnerSeparation))
        {
            if (!_cache.TryGetValue(job.Id!, out var cached) || cached.Updated != job.UpdatedAt || cached.Version != CallTokens.Version)
            {
                cached = (job.UpdatedAt, CallTokens.Version, CallTokens.Build(job));
                _cache[job.Id!] = cached;
            }
            calls.Add(cached.Tokens);
        }
        return ([.. calls.OrderBy(c => c.Date).ThenBy(c => c.CreatedAt)], finished.Count(j => !j.HasLearnerSeparation));
    }
}
