using MessagePack;

namespace Akagi.Bridge.Chat.Models;

public enum CallAnalysisState
{
    Queued,
    Transcribing,
    Analyzing,
    Reporting,
    Completed,
    Failed,
    /// <summary>All parts are reviewed; their findings are being merged and checked.</summary>
    Consolidating,
    /// <summary>The whole conversation is read for coaching.</summary>
    Coaching,
    /// <summary>The whole conversation is read for facts about the people in it.</summary>
    Remembering
}

public enum CallFindingKind
{
    Mistake,
    Correct,
    Strength,
    Phrase,
    Conversation,
    Fact
}

/// <summary>
/// Whether the finding's quoted words were found in the transcript.
/// </summary>
public enum CallQuoteState
{
    NotQuoted,
    Found,
    /// <summary>Found with small differences.</summary>
    Approximate,
    /// <summary>The two transcriptions disagree on these words.</summary>
    Possibly,
    NotFound,
    OtherSpeaker
}

public enum CallFindingState
{
    Kept,
    /// <summary>The consolidation decided it should not count.</summary>
    Dropped,
    /// <summary>Its quote is not in the right lines of the transcript.</summary>
    Rejected,
    /// <summary>The analysis has not finished with it yet.</summary>
    Pending
}

public enum CallFindingFeedback
{
    None,
    Useful,
    NotAMistake
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
    /// Mistakes that count in this call.
    /// </summary>
    [Key(7)]
    public int IssueCount { get; set; }
    [Key(8)]
    public int ChunksAnalyzed { get; set; }
    [Key(9)]
    public int ChunkCount { get; set; }
    [Key(10)]
    public string Error { get; set; } = string.Empty;
    /// <summary>
    /// The learner's share of the speaking time, when both voices were recorded.
    /// </summary>
    [Key(11)]
    public double? LearnerShare { get; set; }
    /// <summary>
    /// What the analysis cost in US dollars; null for calls analyzed before costs were recorded.
    /// </summary>
    [Key(12)]
    public double? Cost { get; set; }

    [IgnoreMember]
    public DateTime Date => RecordedAt ?? CreatedAt;

    /// <summary>
    /// The day the call took place is known; otherwise <see cref="Date"/> is when it was uploaded.
    /// </summary>
    [IgnoreMember]
    public bool DateKnown => RecordedAt != null;
}

[MessagePackObject]
public class CallFindingView
{
    [Key(0)]
    public string Id { get; set; } = string.Empty;
    [Key(1)]
    public CallFindingKind Kind { get; set; }
    /// <summary>
    /// Where it came from, in words: "review", "measured" or "earlier analysis".
    /// </summary>
    [Key(2)]
    public string Origin { get; set; } = string.Empty;
    [Key(3)]
    public string Category { get; set; } = string.Empty;
    [Key(4)]
    public string Pattern { get; set; } = string.Empty;
    /// <summary>
    /// The quoted words: the learner's, or for a phrase the partner's.
    /// </summary>
    [Key(5)]
    public string Said { get; set; } = string.Empty;
    /// <summary>
    /// The natural version, or what a phrase means.
    /// </summary>
    [Key(6)]
    public string Better { get; set; } = string.Empty;
    [Key(7)]
    public string Explanation { get; set; } = string.Empty;
    [Key(8)]
    public int Severity { get; set; }
    /// <summary>
    /// Seconds into the call.
    /// </summary>
    [Key(9)]
    public double? Time { get; set; }
    [Key(10)]
    public CallQuoteState Quote { get; set; }
    [Key(11)]
    public CallFindingState State { get; set; }
    /// <summary>
    /// Why it was dropped or rejected.
    /// </summary>
    [Key(12)]
    public string Note { get; set; } = string.Empty;
    [Key(13)]
    public CallFindingFeedback Feedback { get; set; }
    /// <summary>
    /// For a correct use: the known issue it was about.
    /// </summary>
    [Key(14)]
    public string IssuePattern { get; set; } = string.Empty;
    /// <summary>
    /// Conversation: the skill (follow-up, reaction...). Fact: the topic (work, plans...).
    /// </summary>
    [Key(15)]
    public string Aspect { get; set; } = string.Empty;
    /// <summary>
    /// Conversation: advice on how this is done in Japanese rather than general conversation skill.
    /// </summary>
    [Key(16)]
    public bool Japanese { get; set; }
    [Key(17)]
    public bool WentWell { get; set; }
    /// <summary>
    /// Conversation: the partner's line the learner reacted to.
    /// </summary>
    [Key(18)]
    public string Context { get; set; } = string.Empty;
    /// <summary>
    /// Fact: about the partner rather than the learner.
    /// </summary>
    [Key(19)]
    public bool AboutPartner { get; set; }
    /// <summary>
    /// Fact: worth asking about next time.
    /// </summary>
    [Key(20)]
    public bool FollowUp { get; set; }
}

/// <summary>
/// One issue of the diagnosis with every finding this call added to it.
/// </summary>
[MessagePackObject]
public class CallStruggle
{
    [Key(0)]
    public string IssueId { get; set; } = string.Empty;
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
    /// Where the issue stands overall: active, improving or resolved; empty when the diagnosis no longer has it.
    /// </summary>
    [Key(5)]
    public string Status { get; set; } = string.Empty;
    /// <summary>
    /// Findings that count in this call.
    /// </summary>
    [Key(6)]
    public int TimesInCall { get; set; }
    /// <summary>
    /// Mistakes across everything the diagnosis has seen; null when it no longer has the issue.
    /// </summary>
    [Key(7)]
    public int? TimesOverall { get; set; }
    [Key(8)]
    public CallFindingView[] Findings { get; set; } = [];
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
public class CallFlowWindow
{
    [Key(0)]
    public double Start { get; set; }
    [Key(1)]
    public double LearnerSeconds { get; set; }
    [Key(2)]
    public double PartnerSeconds { get; set; }
}

/// <summary>
/// Who spoke how much and how the turns went.
/// </summary>
[MessagePackObject]
public class CallFlow
{
    /// <summary>
    /// The learner's lines are known; without that there is no partner side to compare with.
    /// </summary>
    [Key(0)]
    public bool Separated { get; set; }
    [Key(1)]
    public bool HasPartner { get; set; }
    [Key(2)]
    public double LearnerSeconds { get; set; }
    [Key(3)]
    public double PartnerSeconds { get; set; }
    [Key(4)]
    public int LearnerCharacters { get; set; }
    [Key(5)]
    public int PartnerCharacters { get; set; }
    [Key(6)]
    public int LearnerTurns { get; set; }
    [Key(7)]
    public int PartnerTurns { get; set; }
    [Key(8)]
    public double LearnerCharactersPerTurn { get; set; }
    [Key(9)]
    public double PartnerCharactersPerTurn { get; set; }
    [Key(10)]
    public double LongestLearnerTurnSeconds { get; set; }
    [Key(11)]
    public double? MedianResponseSeconds { get; set; }
    [Key(12)]
    public int LearnerOverlaps { get; set; }
    [Key(13)]
    public int LongPauses { get; set; }
    [Key(14)]
    public CallFlowWindow[] Windows { get; set; } = [];
    [Key(15)]
    public int LearnerQuestions { get; set; }
    [Key(16)]
    public int PartnerQuestions { get; set; }

    [IgnoreMember]
    public double? LearnerShare => HasPartner && LearnerSeconds + PartnerSeconds > 0 ? LearnerSeconds / (LearnerSeconds + PartnerSeconds) : null;

    [IgnoreMember]
    public double? LearnerCharacterShare => HasPartner && LearnerCharacters + PartnerCharacters > 0 ? (double)LearnerCharacters / (LearnerCharacters + PartnerCharacters) : null;
}

[MessagePackObject]
public class CallWordView
{
    [Key(0)]
    public string Lemma { get; set; } = string.Empty;
    [Key(1)]
    public string Reading { get; set; } = string.Empty;
    /// <summary>
    /// Noun, Verb, Adjective, AdjectivalNoun, Adverb, Adnominal, Conjunction or Interjection.
    /// </summary>
    [Key(2)]
    public string Class { get; set; } = string.Empty;
    [Key(3)]
    public int Learner { get; set; }
    [Key(4)]
    public int Partner { get; set; }
    /// <summary>
    /// The forms it was said in, to find it in the transcript.
    /// </summary>
    [Key(5)]
    public string[] Surfaces { get; set; } = [];
    /// <summary>
    /// The learner used it in this call and in no call before it.
    /// </summary>
    [Key(6)]
    public bool NewForLearner { get; set; }
}

[MessagePackObject]
public class CallVocabulary
{
    [Key(0)]
    public int LearnerWords { get; set; }
    [Key(1)]
    public int PartnerWords { get; set; }
    [Key(2)]
    public int LearnerDistinct { get; set; }
    [Key(3)]
    public int PartnerDistinct { get; set; }
    [Key(4)]
    public CallWordView[] Words { get; set; } = [];
    /// <summary>
    /// The voices are separated, so the learner and partner counts mean something.
    /// </summary>
    [Key(5)]
    public bool Separated { get; set; }
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
    [Key(5)]
    public CallFindingView[] Strengths { get; set; } = [];
    [Key(6)]
    public CallTranscriptLine[] Transcript { get; set; } = [];
    [Key(7)]
    public bool Delivered { get; set; }
    [Key(8)]
    public string ReviewSummary { get; set; } = string.Empty;
    [Key(9)]
    public string[] Priorities { get; set; } = [];
    [Key(10)]
    public CallFindingView[] CorrectUses { get; set; } = [];
    [Key(11)]
    public CallFindingView[] Phrases { get; set; } = [];
    /// <summary>
    /// Dropped by the consolidation or rejected by the quote check, kept visible so nothing disappears silently.
    /// </summary>
    [Key(12)]
    public CallFindingView[] FilteredOut { get; set; } = [];
    [Key(13)]
    public CallFlow? Flow { get; set; }
    [Key(14)]
    public CallVocabulary? Vocabulary { get; set; }
    /// <summary>
    /// Findings were copied from the diagnosis because this call was analyzed before they were stored on calls, so some
    /// may already have been lost.
    /// </summary>
    [Key(15)]
    public bool Legacy { get; set; }
    /// <summary>
    /// Feedback can be given: the analysis is finished.
    /// </summary>
    [Key(16)]
    public bool CanGiveFeedback { get; set; }
    [Key(17)]
    public string CoachingSummary { get; set; } = string.Empty;
    [Key(18)]
    public string[] CoachingTips { get; set; } = [];
    [Key(19)]
    public CallFindingView[] Conversation { get; set; } = [];
    [Key(20)]
    public CallFindingView[] PartnerFacts { get; set; } = [];
    [Key(21)]
    public CallFindingView[] LearnerFacts { get; set; } = [];
    /// <summary>
    /// The coaching and facts passes ran; a call analyzed before they existed can catch up with /calls catchup.
    /// </summary>
    [Key(22)]
    public bool CoachingDone { get; set; }
    [Key(23)]
    public bool FactsDone { get; set; }
    /// <summary>
    /// What each pass cost, in pipeline order; empty for calls analyzed before costs were recorded.
    /// </summary>
    [Key(24)]
    public CallCostView[] Costs { get; set; } = [];
}

[MessagePackObject]
public class CallCostView
{
    /// <summary>
    /// transcription, review, coaching, facts or merge.
    /// </summary>
    [Key(0)]
    public string Pass { get; set; } = string.Empty;
    [Key(1)]
    public int Requests { get; set; }
    [Key(2)]
    public long InputTokens { get; set; }
    /// <summary>
    /// Input tokens served from the provider's prompt cache, at a fraction of the price.
    /// </summary>
    [Key(3)]
    public long CachedTokens { get; set; }
    [Key(4)]
    public long OutputTokens { get; set; }
    /// <summary>
    /// Output tokens spent thinking.
    /// </summary>
    [Key(5)]
    public long ReasoningTokens { get; set; }
    [Key(6)]
    public double AudioSeconds { get; set; }
    [Key(7)]
    public double Cost { get; set; }
    /// <summary>
    /// Worked out from the price because the provider did not report it.
    /// </summary>
    [Key(8)]
    public bool Estimated { get; set; }
}

/// <summary>
/// One call as a point on the trends across calls.
/// </summary>
[MessagePackObject]
public class CallTrendPoint
{
    [Key(0)]
    public string CallId { get; set; } = string.Empty;
    [Key(1)]
    public string Name { get; set; } = string.Empty;
    [Key(2)]
    public DateTime Date { get; set; }
    [Key(3)]
    public double DurationSeconds { get; set; }
    [Key(4)]
    public double? LearnerShare { get; set; }
    [Key(5)]
    public double? MedianResponseSeconds { get; set; }
    [Key(6)]
    public double? FillersPer100 { get; set; }
    [Key(7)]
    public int LearnerWords { get; set; }
    [Key(8)]
    public int LearnerDistinct { get; set; }
    /// <summary>
    /// Words the learner used for the first time in this call.
    /// </summary>
    [Key(9)]
    public int NewWords { get; set; }
    [Key(10)]
    public int IssueCount { get; set; }
    [Key(11)]
    public double LearnerCharactersPerTurn { get; set; }
    /// <summary>
    /// The voices were recorded separately; without that the word and turn numbers include everyone.
    /// </summary>
    [Key(12)]
    public bool Separated { get; set; }
    [Key(13)]
    public int LearnerQuestions { get; set; }
}

[MessagePackObject]
public class InsightWord
{
    [Key(0)]
    public string Lemma { get; set; } = string.Empty;
    [Key(1)]
    public string Reading { get; set; } = string.Empty;
    [Key(2)]
    public string Class { get; set; } = string.Empty;
    [Key(3)]
    public int Learner { get; set; }
    [Key(4)]
    public int Partner { get; set; }
    /// <summary>
    /// Calls the learner used it in.
    /// </summary>
    [Key(5)]
    public int Calls { get; set; }
    [Key(6)]
    public DateTime? FirstUsed { get; set; }
}

[MessagePackObject]
public class InsightStruggle
{
    [Key(0)]
    public string IssueId { get; set; } = string.Empty;
    [Key(1)]
    public string Category { get; set; } = string.Empty;
    [Key(2)]
    public string Pattern { get; set; } = string.Empty;
    /// <summary>
    /// Findings that count, across all calls.
    /// </summary>
    [Key(3)]
    public int Times { get; set; }
    [Key(4)]
    public int Calls { get; set; }
    [Key(5)]
    public int Severity { get; set; }
    [Key(6)]
    public string Status { get; set; } = string.Empty;
    [Key(7)]
    public DateTime LastSeen { get; set; }
    [Key(8)]
    public CallFindingView? Example { get; set; }
    [Key(9)]
    public int CorrectUses { get; set; }
    /// <summary>
    /// Every time the mistake counted, newest call first and in call order within a call.
    /// </summary>
    [Key(10)]
    public InsightOccurrence[] Occurrences { get; set; } = [];
}

/// <summary>
/// One time a struggle showed up, with the call it was in.
/// </summary>
[MessagePackObject]
public class InsightOccurrence
{
    [Key(0)]
    public string CallId { get; set; } = string.Empty;
    [Key(1)]
    public string CallName { get; set; } = string.Empty;
    [Key(2)]
    public DateTime Date { get; set; }
    [Key(3)]
    public CallFindingView Finding { get; set; } = new();
}

[MessagePackObject]
public class InsightPhrase
{
    [Key(0)]
    public string Phrase { get; set; } = string.Empty;
    [Key(1)]
    public string Meaning { get; set; } = string.Empty;
    [Key(2)]
    public string CallId { get; set; } = string.Empty;
    [Key(3)]
    public string CallName { get; set; } = string.Empty;
    [Key(4)]
    public DateTime Date { get; set; }
}

/// <summary>
/// Everything the calls show together: trends, vocabulary and the struggles that keep coming back.
/// </summary>
[MessagePackObject]
public class CallInsights
{
    [Key(0)]
    public CallTrendPoint[] Calls { get; set; } = [];
    [Key(1)]
    public int LearnerWords { get; set; }
    [Key(2)]
    public int LearnerDistinct { get; set; }
    /// <summary>
    /// The learner's words, most used first.
    /// </summary>
    [Key(3)]
    public InsightWord[] Words { get; set; } = [];
    /// <summary>
    /// Words partners used again and again that the learner never said: understood, but not yet their own.
    /// </summary>
    [Key(4)]
    public InsightWord[] HeardNotSaid { get; set; } = [];
    [Key(5)]
    public InsightStruggle[] Struggles { get; set; } = [];
    [Key(6)]
    public InsightPhrase[] Phrases { get; set; } = [];
    /// <summary>
    /// Some calls lack separate voices; they are left out of the vocabulary and have no speaking share.
    /// </summary>
    [Key(7)]
    public int MixedCalls { get; set; }
}

[MessagePackObject]
public class PersonSummary
{
    [Key(0)]
    public string Name { get; set; } = string.Empty;
    [Key(1)]
    public int Calls { get; set; }
    [Key(2)]
    public DateTime? FirstTalked { get; set; }
    [Key(3)]
    public DateTime? LastTalked { get; set; }
    [Key(4)]
    public int Facts { get; set; }
    /// <summary>
    /// Facts worth asking about next time.
    /// </summary>
    [Key(5)]
    public int OpenThreads { get; set; }
    [Key(6)]
    public double TotalSeconds { get; set; }
}

[MessagePackObject]
public class PersonFactView
{
    /// <summary>
    /// The call finding it came from, for feedback.
    /// </summary>
    [Key(0)]
    public string Id { get; set; } = string.Empty;
    [Key(1)]
    public string CallId { get; set; } = string.Empty;
    [Key(2)]
    public string CallName { get; set; } = string.Empty;
    [Key(3)]
    public DateTime Date { get; set; }
    [Key(4)]
    public string Topic { get; set; } = string.Empty;
    [Key(5)]
    public string Text { get; set; } = string.Empty;
    [Key(6)]
    public string Quote { get; set; } = string.Empty;
    [Key(7)]
    public double? Time { get; set; }
    [Key(8)]
    public bool FollowUp { get; set; }
}

[MessagePackObject]
public class PersonCallView
{
    [Key(0)]
    public string CallId { get; set; } = string.Empty;
    [Key(1)]
    public DateTime Date { get; set; }
    [Key(2)]
    public double DurationSeconds { get; set; }
    [Key(3)]
    public int IssueCount { get; set; }
    [Key(4)]
    public double? LearnerShare { get; set; }
    [Key(5)]
    public int Facts { get; set; }
}

/// <summary>
/// Everything the calls taught about one person, to read before talking to them again.
/// </summary>
[MessagePackObject]
public class PersonDetail
{
    [Key(0)]
    public PersonSummary Summary { get; set; } = new();
    /// <summary>
    /// Newest first.
    /// </summary>
    [Key(1)]
    public PersonFactView[] Facts { get; set; } = [];
    [Key(2)]
    public PersonCallView[] Calls { get; set; } = [];
    /// <summary>
    /// Natural expressions they used.
    /// </summary>
    [Key(3)]
    public InsightPhrase[] Phrases { get; set; } = [];
}
