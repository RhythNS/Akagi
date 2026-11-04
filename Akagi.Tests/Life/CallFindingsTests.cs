using Akagi.Life.Japanese;
using Akagi.Life.Japanese.Calls;
using Akagi.Receivers.Commands;
using Akagi.Receivers.Commands.Life;

namespace Akagi.Tests.Life;

public class CallQuoteTests
{
    private static CallJob TwoVoices() => new()
    {
        Partner = "Yuki",
        LearnerChannel = LearnerChannel.Left,
        LearnerSpeaker = "Me",
        Segments =
        [
            new TranscriptSegment { Start = 1, End = 3, Speaker = "Yuki", Text = "最近どう？元気にしてた？" },
            new TranscriptSegment { Start = 4, End = 8, Speaker = "Me", Text = "えーと、仕事がすごく忙しいです。" },
            new TranscriptSegment { Start = 9, End = 12, Speaker = "Me", Text = "でも、週末は休みます。" },
            new TranscriptSegment { Start = 13, End = 15, Speaker = "Yuki", Text = "そっか、無理しないでね。", Disputed = "無理 -> 無視" },
            new TranscriptSegment { Start = 16, End = 18, Speaker = "Me", Text = "ちかわ見ました。", Disputed = "ちかわ -> ちいかわ" }
        ],
        Chunks = [new CallChunk { Index = 0, Start = 1, End = 18 }]
    };

    private static CallFinding Check(CallJob job, string said, FindingKind kind = FindingKind.Mistake)
    {
        CallFinding finding = new() { Kind = kind, Said = said, ChunkIndex = 0 };
        CallQuotes.Check(job, finding);
        return finding;
    }

    [Fact]
    public void ExactQuotes_AreFound_IgnoringPunctuationAndScript()
    {
        CallFinding finding = Check(TwoVoices(), "仕事がすごく忙しいです");

        Assert.Equal(QuoteCheck.Found, finding.Quote);
        Assert.Equal(4, finding.Time);
        Assert.Equal(QuoteCheck.Found, Check(TwoVoices(), "エーと、仕事が").Quote);
    }

    [Fact]
    public void AQuoteOverTwoLines_IsFound()
    {
        Assert.Equal(QuoteCheck.Found, Check(TwoVoices(), "忙しいです。でも、週末").Quote);
    }

    [Fact]
    public void ALightlyEditedQuote_IsApproximate()
    {
        // The review dropped the filler and changed the ending
        Assert.Equal(QuoteCheck.Approximate, Check(TwoVoices(), "仕事がすごく忙しいんです").Quote);
    }

    [Fact]
    public void InventedWords_AndThePartnersWords_DoNotPass()
    {
        CallFinding invented = Check(TwoVoices(), "全然違う文章ですよね");
        CallFinding theirs = Check(TwoVoices(), "元気にしてた");

        Assert.Equal(QuoteCheck.NotFound, invented.Quote);
        Assert.Equal(QuoteCheck.OtherSpeaker, theirs.Quote);
        Assert.False(CallQuotes.Passes(invented));
        Assert.False(CallQuotes.Passes(theirs));
        Assert.Equal(FindingState.Rejected, theirs.State);
        Assert.Contains("partner", theirs.Note);
    }

    [Fact]
    public void Phrases_MustBeThePartners()
    {
        // The partner's line has a disputed word in it, so the phrase is only possible
        Assert.Equal(QuoteCheck.Disputed, Check(TwoVoices(), "無理しないでね", FindingKind.Phrase).Quote);
        Assert.Equal(QuoteCheck.Found, Check(TwoVoices(), "最近どう", FindingKind.Phrase).Quote);
        Assert.Equal(QuoteCheck.OtherSpeaker, Check(TwoVoices(), "週末は休みます", FindingKind.Phrase).Quote);
    }

    [Fact]
    public void WordsTheSecondHearingDisputes_AreOnlyPossible()
    {
        Assert.Equal(QuoteCheck.Disputed, Check(TwoVoices(), "ちかわ見ました").Quote);
    }

    [Fact]
    public void WithoutSeparateVoices_EveryLineMayBeTheLearners()
    {
        CallJob job = TwoVoices();
        job.LearnerChannel = LearnerChannel.Mixed;
        job.LearnerSpeaker = string.Empty;

        Assert.Equal(QuoteCheck.Found, Check(job, "元気にしてた").Quote);
    }
}

public class CallConsolidationTests
{
    private static T With<T>(T command, params (string Name, string Value)[] values) where T : Command
    {
        Argument[] arguments = command.GetDefaultArguments();
        foreach ((string name, string value) in values)
        {
            arguments.Single(a => a.Name == name).Value = value;
        }
        command.Arguments = arguments;
        return command;
    }

    [Fact]
    public void ChunkTools_BecomeFindings()
    {
        ChunkReview review = ToolCallReviewer.ReadChunk(
        [
            With(new NoteCallMistakeCommand(), ("Category", "particles"), ("Pattern", "に vs で"), ("Said", "家に勉強"), ("Better", "家で勉強"), ("Severity", "7")),
            With(new NoteCallCorrectUseCommand(), ("IssueId", "[abc123]"), ("Said", "公園で遊んだ")),
            With(new NoteCallPhraseCommand(), ("Phrase", "無理しないでね"), ("Meaning", "take it easy")),
            With(new NoteCallStrengthCommand(), ("Strength", "natural aizuchi")),
            With(new NoteCallSummaryCommand(), ("Summary", "Work stress."))
        ], 2);

        Assert.Equal("Work stress.", review.Summary);
        CallFinding mistake = review.Findings[0];
        Assert.Equal((IssueCategory.Particle, 3, 2), (mistake.Category, mistake.Severity, mistake.ChunkIndex));
        Assert.Equal("abc123", review.Findings[1].IssueId);
        Assert.Equal((FindingKind.Phrase, "無理しないでね", "take it easy"), (review.Findings[2].Kind, review.Findings[2].Said, review.Findings[2].Better));
        Assert.Equal(FindingKind.Strength, review.Findings[3].Kind);
    }

    [Fact]
    public void Consolidation_GroupsDropsAndKeepsTheRest()
    {
        JapaneseDiagnosis diagnosis = new();
        (DiagnosisIssue known, _) = diagnosis.RecordIssue(IssueCategory.Particle, "に vs で", "", null, 2, DateTime.UtcNow);
        CallFinding a = new() { Kind = FindingKind.Mistake, Pattern = "に/で", Category = IssueCategory.Other };
        CallFinding b = new() { Kind = FindingKind.Mistake, Pattern = "location particle" };
        CallFinding c = new() { Kind = FindingKind.Mistake, Pattern = "not really wrong" };
        CallFinding d = new() { Kind = FindingKind.Mistake, Pattern = "forgotten" };
        CallFinding rejected = new() { Kind = FindingKind.Mistake, Pattern = "invented", State = FindingState.Rejected };
        CallJob job = new() { Findings = [a, b, c, d, rejected] };

        Consolidation consolidation = ToolCallReviewer.ReadConsolidation(
        [
            With(new GroupCallFindingsCommand(), ("FindingIds", $"[{a.Id}], {b.Id}"), ("IssueId", known.Id), ("Category", "Particle"), ("Pattern", "に vs で"),
                ("Explanation", "Place of an action takes で."), ("Severity", "2")),
            With(new DropCallFindingsCommand(), ("FindingIds", c.Id), ("Reason", "natives say this too")),
            With(new WriteCallReviewCommand(), ("Summary", "Good."), ("Priorities", "1. Particles\n- Keigo\n3) Fillers\n4. Too many"))
        ]);
        CallReview review = CallAnalysisPipeline.ApplyConsolidation(job, consolidation, diagnosis);

        Assert.Equal(["Particles", "Keigo", "Fillers"], review.Priorities);
        Assert.False(review.Fallback);
        Assert.All(new[] { a, b }, f => Assert.Equal((FindingState.Kept, known.Id, "に vs で", IssueCategory.Particle), (f.State, f.IssueId, f.Pattern, f.Category)));
        Assert.Equal((FindingState.Dropped, "natives say this too"), (c.State, c.Note));
        Assert.Equal(FindingState.Kept, d.State);
        Assert.Equal(FindingState.Rejected, rejected.State);
    }

    [Fact]
    public void LegacyCalls_CopyWhatTheDiagnosisStillHas_Once()
    {
        CallJob job = new()
        {
            Partner = "Yuki",
            Status = CallJobStatus.Completed,
            CreatedAt = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc),
            LearnerSpeaker = "Me",
            Segments = [new TranscriptSegment { Start = 30, Speaker = "Me", Text = "家に勉強します" }]
        };
        JapaneseDiagnosis diagnosis = new();
        diagnosis.RecordIssue(IssueCategory.Particle, "に vs で", "Place of action.",
            new IssueExample { Said = "家に勉強します", Better = "家で勉強します", Source = job.SourceLabel }, 2, DateTime.UtcNow);
        diagnosis.RecordIssue(IssueCategory.Particle, "other", "", new IssueExample { Said = "x", Source = "chat" }, 2, DateTime.UtcNow);

        Assert.True(CallDiagnosis.ImportLegacy(job, diagnosis));
        Assert.False(CallDiagnosis.ImportLegacy(job, diagnosis));

        CallFinding finding = Assert.Single(job.Findings);
        Assert.Equal((FindingOrigin.Legacy, FindingState.Kept, true, 30.0), (finding.Origin, finding.State, finding.Applied, finding.Time));

        // Taking a legacy finding back removes its example by its words
        CallDiagnosis.SetFeedback(diagnosis, job, finding, FindingFeedback.NotAMistake, DateTime.UtcNow);
        Assert.DoesNotContain(diagnosis.Issues, i => i.Pattern == "に vs で");
    }
}

public class CallStatsTests
{
    [Fact]
    public void Tokenizer_GivesDictionaryFormsAndSkipsGrammar()
    {
        List<JapaneseToken> tokens = JapaneseTokenizer.Tokenize("えーと、仕事がめっちゃ忙しくて、昨日も残業しちゃった。");

        Assert.Contains(tokens, t => t.Surface == "忙しく" && t.Lemma == "忙しい" && t.Class == WordClass.Adjective);
        Assert.Contains(tokens, t => t.Lemma == "めっちゃ" && t.Class == WordClass.Adverb);
        Assert.Contains(tokens, t => t.Lemma == "残業" && t.Class == WordClass.Noun);
        Assert.Null(tokens.Single(t => t.Surface == "が").Class);
        Assert.Null(tokens.Single(t => t.Surface == "えーと").Class);
        Assert.Equal("いそがしい", JapaneseTokenizer.ReadingOf("忙しい"));
    }

    [Fact]
    public void Stats_MeasureShareTurnsAnswersAndWords()
    {
        CallJob job = new()
        {
            LearnerChannel = LearnerChannel.Left,
            LearnerSpeaker = "Me",
            Partner = "Yuki",
            Segments =
            [
                new TranscriptSegment { Start = 0, End = 4, Speaker = "Yuki", Text = "最近忙しい？" },
                new TranscriptSegment { Start = 5, End = 9, Speaker = "Me", Text = "うん、仕事が忙しい。" },
                new TranscriptSegment { Start = 12, End = 14, Speaker = "Me", Text = "でも楽しいよ。" },
                new TranscriptSegment { Start = 13.5, End = 16, Speaker = "Yuki", Text = "そっか、よかった。" },
                new TranscriptSegment { Start = 15, End = 17, Speaker = "Me", Text = "うん。" }
            ]
        };

        CallStats stats = CallStats.Compute(job);

        Assert.True(stats.Separated);
        Assert.Equal(4 + 2 + 2, stats.LearnerSeconds);
        Assert.Equal(4 + 2.5, stats.PartnerSeconds);
        Assert.Equal(8 / 14.5, stats.LearnerShare!.Value, 3);
        Assert.Equal((2, 2), (stats.LearnerTurns, stats.PartnerTurns));
        Assert.Equal(1, stats.LongPauses);
        Assert.Equal(1, stats.LearnerOverlaps);
        Assert.Equal(1, stats.MedianResponseSeconds);
        Assert.Single(stats.Windows);

        CallWord busy = stats.Words.Single(w => w.Lemma == "忙しい");
        Assert.Equal((1, 1), (busy.Learner, busy.Partner));
        Assert.Equal(2, stats.Words.Single(w => w.Lemma == "うん").Learner);
        Assert.Equal(WordClass.Interjection, stats.Words.Single(w => w.Lemma == "うん").Class);
    }

    [Fact]
    public void Stats_WithoutSeparateVoices_HaveNoShare()
    {
        CallJob job = new() { Segments = [new TranscriptSegment { Start = 0, End = 3, Text = "今日は楽しかった。" }] };

        CallStats stats = CallStats.Compute(job);

        Assert.False(stats.Separated);
        Assert.Null(stats.LearnerShare);
        Assert.Equal(1, stats.Words.Single(w => w.Lemma == "楽しい").Learner);
    }
}
