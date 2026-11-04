using MessagePack;

namespace Akagi.Bridge.Chat.Models;

public enum CallAnalysisState
{
    Queued,
    Transcribing,
    Analyzing,
    Reporting,
    Completed,
    Failed
}

/// <summary>
/// One recorded call in the list of analyses.
/// </summary>
[MessagePackObject]
public class CallAnalysisSummary
{
    [Key(0)]
    public string Id { get; set; } = string.Empty;
    [Key(1)]
    public string Title { get; set; } = string.Empty;
    [Key(2)]
    public string Partner { get; set; } = string.Empty;
    [Key(3)]
    public CallAnalysisState State { get; set; }
    [Key(4)]
    public DateTime? RecordedAt { get; set; }
    [Key(5)]
    public DateTime CreatedAt { get; set; }
    [Key(6)]
    public double DurationSeconds { get; set; }
    /// <summary>
    /// Issues the review noted, counted per transcript chunk.
    /// </summary>
    [Key(7)]
    public int IssueCount { get; set; }
    [Key(8)]
    public int ChunksAnalyzed { get; set; }
    [Key(9)]
    public int ChunkCount { get; set; }
    [Key(10)]
    public string Error { get; set; } = string.Empty;

    [IgnoreMember]
    public DateTime Date => RecordedAt ?? CreatedAt;
}

[MessagePackObject]
public class CallStruggleExample
{
    [Key(0)]
    public string Said { get; set; } = string.Empty;
    [Key(1)]
    public string Better { get; set; } = string.Empty;
}

/// <summary>
/// A struggle area of the Japanese diagnosis with the evidence one call added to it.
/// </summary>
[MessagePackObject]
public class CallStruggle
{
    [Key(0)]
    public string Id { get; set; } = string.Empty;
    /// <summary>
    /// The kind of problem in words, e.g. "Unnatural phrasing".
    /// </summary>
    [Key(1)]
    public string Category { get; set; } = string.Empty;
    [Key(2)]
    public string Pattern { get; set; } = string.Empty;
    [Key(3)]
    public string Explanation { get; set; } = string.Empty;
    /// <summary>
    /// 1 (minor polish) to 3 (gets in the way of being understood or sounds rude).
    /// </summary>
    [Key(4)]
    public int Severity { get; set; }
    /// <summary>
    /// Where the struggle stands overall: active, improving or resolved.
    /// </summary>
    [Key(5)]
    public string Status { get; set; } = string.Empty;
    /// <summary>
    /// Examples kept from this call. The diagnosis keeps only the latest few per struggle, so older calls can lose theirs.
    /// </summary>
    [Key(6)]
    public int TimesInCall { get; set; }
    [Key(7)]
    public int TimesOverall { get; set; }
    [Key(8)]
    public CallStruggleExample[] Examples { get; set; } = [];
}

[MessagePackObject]
public class CallFiller
{
    [Key(0)]
    public string Form { get; set; } = string.Empty;
    [Key(1)]
    public int Count { get; set; }
    /// <summary>
    /// Used often enough to count as a habit.
    /// </summary>
    [Key(2)]
    public bool IsHabit { get; set; }
    [Key(3)]
    public string Example { get; set; } = string.Empty;
}

[MessagePackObject]
public class CallGrammarUse
{
    [Key(0)]
    public string Pattern { get; set; } = string.Empty;
    [Key(1)]
    public string Meaning { get; set; } = string.Empty;
    [Key(2)]
    public int Count { get; set; }
    [Key(3)]
    public string Example { get; set; } = string.Empty;
}

/// <summary>
/// What the local analyzers measured in the call, without any LLM.
/// </summary>
[MessagePackObject]
public class CallSpeech
{
    /// <summary>
    /// The measurements cover only the learner. Without a learner channel or label they include everyone.
    /// </summary>
    [Key(0)]
    public bool LearnerOnly { get; set; }
    [Key(1)]
    public int JapaneseCharacters { get; set; }
    [Key(2)]
    public int Sentences { get; set; }
    [Key(3)]
    public double FillersPer100 { get; set; }
    [Key(4)]
    public CallFiller[] Fillers { get; set; } = [];
    [Key(5)]
    public double PoliteRatio { get; set; }
    [Key(6)]
    public int Keigo { get; set; }
    [Key(7)]
    public string ExpectedRegister { get; set; } = string.Empty;
    [Key(8)]
    public string RegisterMismatch { get; set; } = string.Empty;
    [Key(9)]
    public string[] RecurringStructures { get; set; } = [];
    [Key(10)]
    public CallGrammarUse[] Grammar { get; set; } = [];
}

[MessagePackObject]
public class CallTranscriptLine
{
    [Key(0)]
    public double Start { get; set; }
    [Key(1)]
    public string Speaker { get; set; } = string.Empty;
    [Key(2)]
    public string Text { get; set; } = string.Empty;
    /// <summary>
    /// Where a second transcription heard something else, as "this -> that; ...".
    /// </summary>
    [Key(3)]
    public string Disputed { get; set; } = string.Empty;
    [Key(4)]
    public bool IsLearner { get; set; }
}

/// <summary>
/// Everything one call analysis gathered.
/// </summary>
[MessagePackObject]
public class CallAnalysisDetail
{
    [Key(0)]
    public CallAnalysisSummary Summary { get; set; } = new();
    /// <summary>
    /// The politeness the call called for: casual, polite or formal.
    /// </summary>
    [Key(1)]
    public string Register { get; set; } = string.Empty;
    [Key(2)]
    public string Report { get; set; } = string.Empty;
    [Key(3)]
    public CallStruggle[] Struggles { get; set; } = [];
    [Key(4)]
    public CallSpeech? Speech { get; set; }
    /// <summary>
    /// Strengths noted while the call was being analyzed.
    /// </summary>
    [Key(5)]
    public string[] Strengths { get; set; } = [];
    [Key(6)]
    public CallTranscriptLine[] Transcript { get; set; } = [];
    [Key(7)]
    public bool Delivered { get; set; }
}
