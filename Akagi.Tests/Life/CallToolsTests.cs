using Akagi.Bridge.Chat.Models;
using Akagi.Bridge.Chat.Transmissions.Requests;
using Akagi.Bridge.Chat.Transmissions.Responses;
using Akagi.Communication.SocketComs.Transmissions;
using Akagi.Life.Japanese;
using Akagi.Life.Japanese.Calls;
using Akagi.Life.Japanese.Mining;
using Akagi.Life.Japanese.Vocabulary;
using Akagi.Tests.Characters;
using Akagi.Tests.Support;
using Akagi.Users;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using System.IO.Compression;
using System.Text;

namespace Akagi.Tests.Life;

internal class InMemoryMinedSentenceDatabase : InMemoryDatabase<MinedSentence>, IMinedSentenceDatabase
{
    public InMemoryMinedSentenceDatabase() : base("mined_sentences")
    {
    }

    public Task<List<MinedSentence>> GetForUser(string userId) => Task.FromResult(Items.Where(m => m.UserId == userId).ToList());

    public async Task<int> DeleteForCallAsync(string userId, string callId)
    {
        List<MinedSentence> mined = [.. Items.Where(m => m.UserId == userId && m.CallId == callId)];
        foreach (MinedSentence sentence in mined)
        {
            await DeleteDocumentByIdAsync(sentence.Id!);
        }
        return mined.Count;
    }
}

/// <summary>
/// Pretends to be ffmpeg: writes a small file where the clip or track would go and remembers what was asked.
/// </summary>
internal class FakeClipper(bool available = true) : IAudioClipper
{
    public List<(double Start, double End, MiningVoices Voices)> Clips { get; } = [];
    public List<MiningVoices> Tracks { get; } = [];

    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken) => Task.FromResult(available);

    public Task ClipAsync(string source, double start, double end, LearnerChannel learner, MiningVoices voices, string output, CancellationToken cancellationToken)
    {
        Clips.Add((start, end, voices));
        return File.WriteAllBytesAsync(output, [1, 2, 3], cancellationToken);
    }

    public Task ExtractAsync(string source, LearnerChannel learner, MiningVoices voices, string output, CancellationToken cancellationToken)
    {
        Tracks.Add(voices);
        return File.WriteAllBytesAsync(output, [4, 5, 6], cancellationToken);
    }
}

internal class FakeAdvisor : IStudyAdvisor
{
    public CallInsights? Seen { get; private set; }

    public Task<string> WriteAsync(User user, CallInsights insights, CancellationToken cancellationToken)
    {
        Seen = insights;
        return Task.FromResult("Focus on すごく忙しい.");
    }
}

/// <summary>
/// Deleting calls, the insights timeframe, focus struggles, recommendations, per call metrics, expression search and
/// sentence mining.
/// </summary>
public sealed class CallToolsTests : IDisposable
{
    private const string UserId = "6adcc28c4f351a3450e30ab6";

    private readonly InMemoryCallJobDatabase _jobs = new();
    private readonly InMemoryJapaneseDiagnosisDatabase _diagnoses = new();
    private readonly InMemoryPersonProfileDatabase _people = new();
    private readonly InMemoryMemoryDatabase _memories = new();
    private readonly InMemoryCompanionStateDatabase _companions = new();
    private readonly InMemoryMinedSentenceDatabase _mined = new();
    private readonly CallMemory _memory;
    private readonly string _storage = Path.Combine(Path.GetTempPath(), "akagi-calltools-" + Guid.NewGuid().ToString("N"));

    public CallToolsTests()
    {
        _memory = new CallMemory(_people, _memories, _companions, null, NullLogger<CallMemory>.Instance);
        Directory.CreateDirectory(_storage);
    }

    public void Dispose()
    {
        if (Directory.Exists(_storage))
        {
            Directory.Delete(_storage, recursive: true);
        }
    }

    private CallAnalysisRequestHandler Handler(IStudyAdvisor? advisor = null) =>
        new(_jobs, _diagnoses, _people, _memory, NullLogger<CallAnalysisRequestHandler>.Instance, _mined,
            Options.Create(new CallAnalysisOptions { StorageDirectory = _storage }), advisor);

    private CallJob Job(string partner, DateTime recordedAt, params (string Speaker, string Text)[] lines)
    {
        string id = ObjectId.GenerateNewId().ToString();
        string folder = Path.Combine(_storage, id);
        Directory.CreateDirectory(folder);
        string source = Path.Combine(folder, "call.mp3");
        File.WriteAllBytes(source, [0]);
        double time = 0;
        CallJob job = new()
        {
            Id = id,
            UserId = UserId,
            Title = $"{recordedAt:yyyy-MM-dd} {partner}",
            Partner = partner,
            LearnerChannel = LearnerChannel.Left,
            LearnerSpeaker = CallJob.DefaultLearnerLabel,
            RecordedAt = recordedAt,
            CreatedAt = recordedAt.AddHours(1),
            Status = CallJobStatus.Completed,
            SourcePath = source,
            Segments = [.. lines.Select(l =>
            {
                TranscriptSegment segment = new() { Start = time, End = time + 4, Speaker = l.Speaker, Text = l.Text };
                time += 5;
                return segment;
            })]
        };
        job.Stats = CallStats.Compute(job);
        _jobs.SaveDocumentAsync(job).Wait();
        return job;
    }

    private static CallFinding Mistake(string issueId, string pattern, string said, int severity = 2) => new()
    {
        Kind = FindingKind.Mistake,
        Category = IssueCategory.UnnaturalPhrasing,
        Pattern = pattern,
        Said = said,
        Better = "めっちゃ忙しい",
        Severity = severity,
        IssueId = issueId,
        State = FindingState.Kept,
        Time = 5
    };

    private static DateTime Day(int month, int day) => new(2026, month, day, 12, 0, 0, DateTimeKind.Utc);

    // Deleting calls

    [Fact]
    public async Task Delete_TakesBackDiagnosisFactsPersonAndMiningAndRemovesFiles()
    {
        CallJob job = Job("Yuki", Day(10, 20), ("Me", "えーと、仕事がすごく忙しいです。"), ("Yuki", "無理しないでね。"));
        job.DeliveryCharacterId = "6ae1ebfa5a533276d716f834";
        JapaneseDiagnosis diagnosis = new() { UserId = UserId };
        CallFinding mistake = Mistake(string.Empty, "すごく忙しい", "すごく忙しいです");
        CallFinding strength = new() { Kind = FindingKind.Strength, Pattern = "natural aizuchi", State = FindingState.Kept };
        CallFinding fact = new() { Kind = FindingKind.Fact, Subject = FactSubject.Partner, Pattern = "Yuki has an exam on Friday.", State = FindingState.Kept };
        CallFinding learnerFact = new() { Kind = FindingKind.Fact, Subject = FactSubject.Learner, Pattern = "Works overtime.", State = FindingState.Kept };
        job.Findings = [mistake, strength, fact, learnerFact];
        foreach (CallFinding finding in job.Findings)
        {
            CallDiagnosis.Apply(diagnosis, job, finding, DateTime.UtcNow);
            await _memory.ApplyAsync(job, finding, DateTime.UtcNow);
        }
        await _memory.RecordCallAsync(job);
        await _diagnoses.SaveDocumentAsync(diagnosis);
        await _jobs.SaveDocumentAsync(job);
        await _mined.SaveDocumentAsync(new MinedSentence { UserId = UserId, CallId = job.Id!, Text = "無理しないでね。" });
        Assert.Single(diagnosis.Issues);
        Assert.Single(_memories.Items);

        CallAnalysisResponseTransmission response = await Handler().ProcessAsync(UserId, new CallAnalysisRequestTransmission { Action = CallAnalysisAction.Delete, CallId = job.Id! });

        Assert.Null(response.Error);
        Assert.Empty(_jobs.Items);
        Assert.Empty(response.Calls);
        Assert.Empty(_diagnoses.Items.Single().Issues);
        Assert.Empty(_diagnoses.Items.Single().Strengths);
        Assert.Empty(_memories.Items);
        Assert.Empty(_people.Items);
        Assert.Empty(_mined.Items);
        Assert.False(Directory.Exists(Path.Combine(_storage, job.Id!)));
        Assert.Contains("was deleted", response.Message);
        Assert.Contains("1 mined sentence", response.Message);
    }

    [Fact]
    public async Task Delete_KeepsThePersonWhenOtherCallsRemain_AndRefusesARunningCall()
    {
        CallJob first = Job("Yuki", Day(10, 20), ("Me", "うん。"), ("Yuki", "そう。"));
        CallJob second = Job("Yuki", Day(10, 27), ("Me", "うん。"), ("Yuki", "そう。"));
        await _memory.RecordCallAsync(first);
        await _memory.RecordCallAsync(second);
        CallJob running = Job("Ken", Day(11, 1), ("Me", "うん。"));
        running.Status = CallJobStatus.Analyzing;

        CallAnalysisResponseTransmission deleted = await Handler().ProcessAsync(UserId, new CallAnalysisRequestTransmission { Action = CallAnalysisAction.Delete, CallId = first.Id! });
        CallAnalysisResponseTransmission refused = await Handler().ProcessAsync(UserId, new CallAnalysisRequestTransmission { Action = CallAnalysisAction.Delete, CallId = running.Id! });

        Assert.Null(deleted.Error);
        Assert.Equal([second.Id], _people.Items.Single().Calls.Select(c => c.CallId));
        Assert.Contains("finished", refused.Error);
        Assert.Contains(_jobs.Items, j => j.Id == running.Id);
    }

    [Fact]
    public void UndoMeasurements_TakesBackGrammarCountsAndTheSnapshot()
    {
        CallJob job = Job("Yuki", Day(10, 20), ("Me", "行かざるを得ない。でも休むに越したことはない。えーと、まあ、そうだね。"));
        JapaneseDiagnosis diagnosis = new() { UserId = UserId };
        SpeechReport report = SpeechAnalysis.Analyze(job.LearnerText(), job.Register);
        foreach (PatternMatch match in report.Patterns)
        {
            diagnosis.RecordPatternUse(match.Pattern.Key, match.Count + 1, DateTime.UtcNow);
        }
        diagnosis.AddSnapshot(new SpeechSnapshot { Source = job.SourceLabel, Characters = report.Fillers.JapaneseCharacters });
        diagnosis.AddSnapshot(new SpeechSnapshot { Source = "call with Ken 2026-10-01", Characters = 500 });

        int changes = CallRemoval.UndoMeasurements(diagnosis, job);

        Assert.True(changes > 0);
        Assert.All(diagnosis.PatternUsage, u => Assert.Equal(1, u.Count));
        Assert.Equal(["call with Ken 2026-10-01"], diagnosis.Snapshots.Select(s => s.Source));
    }

    // Insights: timeframe, focus and recommendations

    [Fact]
    public void Insights_Timeframe_ShowsOnlyCallsInsideButCountsNewWordsAgainstAllCalls()
    {
        Job("Yuki", Day(10, 1), ("Me", "映画を見た。"), ("Yuki", "そうなんだ。"));
        Job("Yuki", Day(10, 20), ("Me", "映画を見た。本を読んだ。"), ("Yuki", "いいね。"));
        Job("Yuki", Day(11, 3), ("Me", "本を読んだ。"), ("Yuki", "えらい。"));

        CallInsights insights = CallAnalysisViews.Insights(_jobs.Items.ToList(), null, Day(10, 10), Day(10, 30));

        CallTrendPoint call = Assert.Single(insights.Calls);
        Assert.Equal(Day(10, 20), call.Date);
        Assert.Equal(2, insights.HiddenCalls);
        // 映画 and 見る were said before the timeframe; 本 and 読む are new in this call
        Assert.Equal(2, call.NewWords);
        Assert.Equal(Day(10, 1), insights.Words.Single(w => w.Lemma == "映画").FirstUsed);
    }

    [Fact]
    public void Insights_FocusStruggle_FollowsItsRateBeforeAndSinceFocusing()
    {
        CallJob before = Job("Yuki", Day(10, 1), ("Me", "仕事がすごく忙しいです。すごく忙しいです。"), ("Yuki", "大変だね。"));
        before.Findings = [Mistake("abc123", "すごく忙しい", "すごく忙しいです"), Mistake("abc123", "すごく忙しい", "すごく忙しいです")];
        CallJob after = Job("Yuki", Day(10, 20), ("Me", "仕事が忙しい。でも楽しいよ。週末は休む。"), ("Yuki", "いいね。"));
        JapaneseDiagnosis diagnosis = new() { UserId = UserId };
        diagnosis.Issues.Add(new DiagnosisIssue { Id = "abc123", Category = IssueCategory.UnnaturalPhrasing, Pattern = "すごく忙しい", Focus = true, FocusSince = Day(10, 10) });

        CallInsights insights = CallAnalysisViews.Insights(_jobs.Items.ToList(), diagnosis, Day(10, 15), null);

        InsightStruggle focus = Assert.Single(insights.FocusStruggles);
        Assert.Equal("すごく忙しい", focus.Pattern);
        // Not once in the timeframe, which is the good news, and still listed
        Assert.Empty(insights.Struggles);
        Assert.Equal([after.Id], focus.Trend.Select(t => t.CallId));
        Assert.Equal(0, focus.Trend[0].Rate);
        Assert.True(focus.RateBefore > 0);
        Assert.Equal(0, focus.RateSince);
        Assert.Contains(insights.Advice, a => a.Kind == "progress" && a.Title.Contains("going down"));
    }

    [Fact]
    public void Advise_SuggestsRecurringStrugglesAndWordHabits()
    {
        for (int day = 1; day <= 3; day++)
        {
            CallJob job = Job("Yuki", Day(10, day * 5),
                ("Me", string.Join("", Enumerable.Repeat("まあ、仕事は忙しい。", 8))),
                ("Yuki", "そうなんだ。やっぱり大変だね。やっぱり休んだ方がいいよ。"));
            job.Findings = [Mistake("abc123", "すごく忙しい", "すごく忙しいです", severity: 3), Mistake("abc123", "すごく忙しい", "すごく忙しいです", severity: 3)];
        }
        JapaneseDiagnosis diagnosis = new() { UserId = UserId };
        diagnosis.Issues.Add(new DiagnosisIssue { Id = "abc123", Category = IssueCategory.UnnaturalPhrasing, Pattern = "すごく忙しい" });

        CallInsights insights = CallAnalysisViews.Insights(_jobs.Items.ToList(), diagnosis);

        InsightAdvice focus = Assert.Single(insights.Advice, a => a.Kind == "focus");
        Assert.Equal("abc123", focus.IssueId);
        Assert.Contains("6× in your last 3 calls", focus.Detail);
        Assert.Contains(insights.Advice, a => a.Kind == "use-less" && a.Word == "まあ");
        Assert.Contains(insights.Advice, a => a.Kind == "use-more" && a.Word == "やっぱり");
    }

    [Fact]
    public async Task IgnoreAdvice_HidesTheSuggestionUntilShownAgain()
    {
        for (int day = 1; day <= 3; day++)
        {
            Job("Yuki", Day(10, day * 5), ("Me", "仕事は忙しい。"), ("Yuki", "そうなんだ。やっぱり大変だね。やっぱり休んだ方がいいよ。"));
        }
        CallAnalysisRequestHandler handler = Handler();

        CallAnalysisResponseTransmission ignored = await handler.ProcessAsync(UserId,
            new CallAnalysisRequestTransmission { Action = CallAnalysisAction.IgnoreAdvice, AdviceKey = "use-more:やっぱり", Ignore = true });
        CallAnalysisResponseTransmission again = await handler.ProcessAsync(UserId,
            new CallAnalysisRequestTransmission { Action = CallAnalysisAction.IgnoreAdvice, AdviceKey = "use-more:やっぱり", Ignore = true });
        int stored = _diagnoses.Items.Single().IgnoredAdvice.Count;
        CallAnalysisResponseTransmission shown = await handler.ProcessAsync(UserId,
            new CallAnalysisRequestTransmission { Action = CallAnalysisAction.IgnoreAdvice, AdviceKey = "use-more:やっぱり", Ignore = false });

        Assert.DoesNotContain(ignored.Insights!.Advice, a => a.Key == "use-more:やっぱり");
        InsightAdvice hidden = Assert.Single(ignored.Insights.IgnoredAdvice);
        Assert.Equal(("use-more", "Try using: やっぱり", "やっぱり"), (hidden.Kind, hidden.Title, hidden.Word));
        Assert.Equal(1, stored);
        Assert.Single(again.Insights!.IgnoredAdvice);
        Assert.Contains(shown.Insights!.Advice, a => a.Key == "use-more:やっぱり");
        Assert.Empty(shown.Insights.IgnoredAdvice);
    }

    [Fact]
    public void Tokenizer_ReadsTopicWaNeAsParticles_NotTheVerbHaneru()
    {
        Assert.DoesNotContain(JapaneseTokenizer.Tokenize("それはね、今日はね楽しかった。"), t => t.Lemma == "はねる");
        Assert.Equal(["それ", "は", "ね", "、"], JapaneseTokenizer.Tokenize("それはね、").Select(t => t.Surface));
        Assert.Contains(JapaneseTokenizer.Tokenize("ボールがはねた。"), t => t.Lemma == "はねる");
        Assert.Contains(JapaneseTokenizer.Tokenize("魚がはねてる。"), t => t.Lemma == "はねる");
    }

    [Fact]
    public void Metrics_CompareTheCallWithTheAverageOfTheOthers()
    {
        CallJob quiet = Job("Yuki", Day(10, 1), ("Me", "うん。"), ("Yuki", "最近どう？仕事は大変？週末は何してた？"));
        Job("Yuki", Day(10, 5), ("Me", "週末は映画を見たよ。Yukiは？"), ("Yuki", "私は寝てた。"));
        Job("Yuki", Day(10, 9), ("Me", "仕事は大変だけど楽しい。どう思う？"), ("Yuki", "いいね。"));

        CallMetric[] metrics = CallAnalysisViews.Metrics(quiet, _jobs.Items.ToList());

        CallMetric questions = metrics.Single(m => m.Key == "questions");
        Assert.Equal(0, questions.Value);
        Assert.Equal(1, questions.Average);
        Assert.Equal(2, questions.AverageOver);
        Assert.True(questions.HigherIsBetter);
        Assert.Contains(metrics, m => m.Key == "share" && m.Format == "percent");
        Assert.Contains(metrics, m => m.Key == "issues" && m.HigherIsBetter == false);
    }

    [Fact]
    public async Task SetFocus_MarksTheStruggleAndAnswersWithInsights()
    {
        Job("Yuki", Day(10, 1), ("Me", "うん。"), ("Yuki", "そう。"));
        JapaneseDiagnosis diagnosis = new() { UserId = UserId };
        diagnosis.Issues.Add(new DiagnosisIssue { Id = "abc123", Pattern = "それも" });
        await _diagnoses.SaveDocumentAsync(diagnosis);

        CallAnalysisResponseTransmission on = await Handler().ProcessAsync(UserId, new CallAnalysisRequestTransmission { Action = CallAnalysisAction.SetFocus, IssueId = "abc123", Focus = true });
        DateTime? since = diagnosis.Issues[0].FocusSince;
        CallAnalysisResponseTransmission unknown = await Handler().ProcessAsync(UserId, new CallAnalysisRequestTransmission { Action = CallAnalysisAction.SetFocus, IssueId = "nope", Focus = true });

        Assert.True(diagnosis.Issues[0].Focus);
        Assert.NotNull(since);
        Assert.Equal("それも", Assert.Single(on.Insights!.FocusStruggles).Pattern);
        Assert.NotNull(unknown.Error);
    }

    [Fact]
    public async Task Advice_IsWrittenFromTheInsightsAndKept()
    {
        Job("Yuki", Day(10, 1), ("Me", "うん。"), ("Yuki", "そう。"));
        FakeAdvisor advisor = new();

        CallAnalysisResponseTransmission response = await Handler(advisor).ProcessAsync(UserId,
            new CallAnalysisRequestTransmission { Action = CallAnalysisAction.Advice }, new User { Id = UserId, Name = "Rhyth" });

        Assert.Null(response.Error);
        Assert.Equal("Focus on すごく忙しい.", response.Insights!.WrittenAdvice);
        Assert.Equal("Focus on すごく忙しい.", _diagnoses.Items.Single().Advice);
        Assert.Single(advisor.Seen!.Calls);
    }

    [Fact]
    public void Focus_PutsPickedStrugglesFirstForTheTutorAndCompanion()
    {
        DateTime now = DateTime.UtcNow;
        JapaneseDiagnosis diagnosis = new() { UserId = UserId };
        diagnosis.Issues.Add(new DiagnosisIssue { Id = "a", Pattern = "frequent", Count = 20, Severity = 3, LastSeen = now });
        diagnosis.Issues.Add(new DiagnosisIssue { Id = "b", Pattern = "picked", Count = 1, Severity = 1, LastSeen = now.AddDays(-60), Status = IssueStatus.Resolved, Focus = true });

        Assert.Equal(["b", "a"], diagnosis.Focus(2, now).Select(i => i.Id));
        Assert.Contains("- [he chose to focus on this] [b] UnnaturalPhrasing: picked", diagnosis.Describe(now, 2, withExamples: false));
    }

    [Fact]
    public void StudyAdvisor_DescribesFocusAndRecurringStruggles()
    {
        CallInsights insights = new()
        {
            Calls = [new CallTrendPoint { Name = "Call with Yuki", Date = Day(10, 1), LearnerShare = 0.5, FillersPer100 = 4.2, Separated = true, LearnerQuestions = 3, IssueCount = 7 }],
            FocusStruggles = [new InsightStruggle { Pattern = "それも", Category = "Unnatural phrasing", Focus = true, RateBefore = 2.5, RateSince = 1.0 }],
            Struggles = [new InsightStruggle { Pattern = "は vs が", Category = "Particle", Severity = 2, Times = 4, Calls = 2, Example = new CallFindingView { Said = "私が", Better = "私は" } }],
            Advice = [new InsightAdvice { Kind = "use-less", Title = "Use less: まあ", Detail = "You said it 50×." }]
        };

        string text = StudyAdvisor.Describe(insights);

        Assert.Contains("[focus] それも (Unnatural phrasing), 2.5 per 1,000 characters before focusing, 1.0 since", text);
        Assert.Contains("は vs が (Particle, severity 2): 4x in 2 call(s) e.g. 「私が」 -> 「私は」", text);
        Assert.Contains("Use less: まあ", text);
        Assert.Contains("50%, 4.2, 3, 7", text);
    }

    // Expression search

    [Fact]
    public void ExpressionSearch_CountsYouAndPartnersPerCallWithEveryLine()
    {
        Job("Yuki", Day(10, 1), ("Me", "それも、それもいいね。"), ("Yuki", "それもそうだね。"), ("Me", "うん。"));
        Job("Ken", Day(10, 5), ("Me", "今日は暑い。"), ("Ken", "それもありだね。"));
        List<CallTokens> calls = [.. _jobs.Items.OrderBy(j => j.RecordedAt).Select(CallTokens.Build)];

        VocabExpressionResult result = ExpressionSearch.Run(new VocabularyView(calls, new VocabFilter(), new StubWordDictionary()), "それも");

        Assert.Equal(2, result.Learner);
        Assert.Equal(2, result.Partner);
        Assert.Equal([2, 0], result.Calls.Select(c => c.Learner));
        Assert.Equal([1, 1], result.Calls.Select(c => c.Partner));
        Assert.Equal(3, result.TotalLines);
        // Newest call first
        Assert.Equal("Ken", result.Lines[0].Speaker);
        Assert.All(result.Lines, l => Assert.Equal(["それも"], l.Marks));
        Assert.NotNull(result.LearnerPerHour);
    }

    [Fact]
    public void ExpressionSearch_IgnoresCaseAndWidth()
    {
        Assert.Equal((2, "OK"), ExpressionSearch.Count("OK、ok!", "ＯＫ"));
        Assert.Equal((0, null), ExpressionSearch.Count("そうだね", "それも"));
    }

    // Sentence mining

    private MiningService Mining(IAudioClipper? clipper = null) =>
        new(_jobs, _mined, clipper ?? new FakeClipper(), _storage, NullLogger.Instance);

    [Fact]
    public async Task Mining_AddFindsTheLineAndDoesNotAddTwice()
    {
        CallJob job = Job("Yuki", Day(10, 1), ("Me", "えーと。"), ("Yuki", "やっぱり休んだ方がいいよ。無理しないで。"));
        MiningService mining = Mining();

        (MinedSentence? first, string? error) = await mining.AddAsync(UserId, job.Id!, 5, "やっぱり休んだ方がいいよ。", "やっぱり");
        (MinedSentence? again, _) = await mining.AddAsync(UserId, job.Id!, 5, "やっぱり休んだ方がいいよ。", "やっぱり");
        (MinedSentence? missing, string? missingError) = await mining.AddAsync(UserId, job.Id!, 300, "nothing", "");
        await MiningService.WaitForClipAsync(first!.Id!);

        Assert.Null(error);
        Assert.Equal("Yuki", first.Speaker);
        Assert.False(first.IsLearner);
        Assert.Equal(9, first.End);
        Assert.Same(first, again);
        Assert.Single(_mined.Items);
        Assert.Null(missing);
        Assert.NotNull(missingError);
    }

    [Fact]
    public async Task Mining_AddCutsTheClipInTheBackgroundFromTheSpeakersChannel()
    {
        CallJob job = Job("Yuki", Day(10, 1), ("Me", "えーと、それもいいね。"), ("Yuki", "やっぱり休んだ方がいいよ。"));
        FakeClipper clipper = new();
        MiningService mining = Mining(clipper);

        (MinedSentence? partner, _) = await mining.AddAsync(UserId, job.Id!, 5, "やっぱり休んだ方がいいよ。", "やっぱり");
        (MinedSentence? own, _) = await mining.AddAsync(UserId, job.Id!, 0, "えーと、それもいいね。", "");
        await MiningService.WaitForClipAsync(partner!.Id!);
        await MiningService.WaitForClipAsync(own!.Id!);

        MinedSentence cut = (await mining.GetAsync(UserId, partner.Id!))!;
        Assert.Equal(MinedClipState.Ready, cut.State);
        Assert.Equal(MinedClipState.Ready, (await mining.GetAsync(UserId, own.Id!))!.State);
        Assert.True(File.Exists(Path.Combine(_storage, job.Id!, "mined", partner.Id + ".mp3")));
        (double Start, double End, MiningVoices Voices) clip = Assert.Single(clipper.Clips, c => c.Voices == MiningVoices.Partner);
        Assert.Equal(5 - MiningExport.LeadSeconds, clip.Start, 3);
        Assert.Contains(clipper.Clips, c => c.Voices == MiningVoices.Learner);
        Assert.Equal(clip.End - clip.Start, cut.ClipSeconds, 3);
    }

    [Fact]
    public async Task Mining_WithoutARecordingThereIsNoAudio_AndWithoutFfmpegItFails()
    {
        CallJob job = Job("Yuki", Day(10, 1), ("Me", "えーと。"), ("Yuki", "やっぱりね。"));
        (MinedSentence? failed, _) = await Mining(new FakeClipper(available: false)).AddAsync(UserId, job.Id!, 5, "やっぱりね。", "");
        await MiningService.WaitForClipAsync(failed!.Id!);
        File.Delete(job.SourcePath);
        (MinedSentence? text, _) = await Mining().AddAsync(UserId, job.Id!, 0, "えーと。", "");

        Assert.Equal(MinedClipState.Failed, failed.State);
        Assert.Contains("ffmpeg", failed.Error);
        Assert.Equal(MinedClipState.NoAudio, text!.State);
        (MiningFile? audio, string? audioError) = await Mining().DownloadAsync(UserId, text.Id!, MiningFileKind.Audio);
        Assert.Null(audio);
        Assert.NotNull(audioError);
        (MiningFile? anki, _) = await Mining().DownloadAsync(UserId, text.Id!, MiningFileKind.Anki);
        using ZipArchive zip = new(new MemoryStream(anki!.Data));
        Assert.Equal([MiningExport.CsvFileName, MiningExport.ReadmeFileName], zip.Entries.Select(e => e.Name));
    }

    [Fact]
    public async Task Mining_RetryCutsAFailedClipAgain()
    {
        CallJob job = Job("Yuki", Day(10, 1), ("Me", "えーと。"), ("Yuki", "やっぱりね。"));
        (MinedSentence? sentence, _) = await Mining(new FakeClipper(available: false)).AddAsync(UserId, job.Id!, 5, "やっぱりね。", "");
        await MiningService.WaitForClipAsync(sentence!.Id!);

        await Mining().RetryAsync(UserId, sentence.Id!);
        await MiningService.WaitForClipAsync(sentence.Id!);

        Assert.Equal(MinedClipState.Ready, sentence.State);
        Assert.Empty(sentence.Error);
    }

    [Fact]
    public async Task Mining_DownloadsTheClipSubtitlesTimedToItAndAnAnkiCard()
    {
        CallJob job = Job("Yuki", Day(10, 1), ("Me", "えーと。"), ("Yuki", "やっぱり休んだ方がいいよ。"));
        MiningService mining = Mining();
        (MinedSentence? sentence, _) = await mining.AddAsync(UserId, job.Id!, 5, "やっぱり休んだ方がいいよ。", "やっぱり");
        await MiningService.WaitForClipAsync(sentence!.Id!);

        (MiningFile? audio, _) = await mining.DownloadAsync(UserId, sentence.Id!, MiningFileKind.Audio);
        (MiningFile? srt, _) = await mining.DownloadAsync(UserId, sentence.Id!, MiningFileKind.Subtitles);
        (MiningFile? anki, _) = await mining.DownloadAsync(UserId, sentence.Id!, MiningFileKind.Anki);

        Assert.StartsWith("akagi_yuki_20261001_0005_", audio!.FileName);
        Assert.Equal([1, 2, 3], audio.Data);
        Assert.Equal(Path.ChangeExtension(audio.FileName, ".srt"), srt!.FileName);
        // The clip starts a quarter second before the line, which runs four seconds
        Assert.Equal($"1{Environment.NewLine}00:00:00,250 --> 00:00:04,250{Environment.NewLine}やっぱり休んだ方がいいよ。{Environment.NewLine}{Environment.NewLine}",
            Encoding.UTF8.GetString(srt.Data));
        using ZipArchive zip = new(new MemoryStream(anki!.Data));
        Assert.Equal([MiningExport.CsvFileName, MiningExport.ReadmeFileName, audio.FileName], zip.Entries.Select(e => e.Name));
        string csv = new StreamReader(zip.GetEntry(MiningExport.CsvFileName)!.Open()).ReadToEnd();
        Assert.Contains("#columns:Sentence,Target,Audio,Source", csv);
        Assert.Contains($"やっぱり休んだ方がいいよ。,やっぱり,[sound:{audio.FileName}],\"Call with Yuki, 2026-10-01 00:05\"", csv);
    }

    [Fact]
    public async Task Mining_RemoveDeletesTheClip_AndOthersCannotSeeIt()
    {
        CallJob job = Job("Yuki", Day(10, 1), ("Me", "えーと。"), ("Yuki", "やっぱりね。"));
        MiningService mining = Mining();
        (MinedSentence? sentence, _) = await mining.AddAsync(UserId, job.Id!, 5, "やっぱりね。", "");
        await MiningService.WaitForClipAsync(sentence!.Id!);
        string clip = mining.ClipPath(sentence)!;

        Assert.Null(await mining.GetAsync("6adcc28c4f351a3450e30ab7", sentence.Id!));
        Assert.False(await mining.RemoveAsync("6adcc28c4f351a3450e30ab7", sentence.Id!));
        Assert.True(await mining.RemoveAsync(UserId, sentence.Id!));

        Assert.False(File.Exists(clip));
        Assert.Empty(_mined.Items);
    }

    [Fact]
    public async Task Mining_ExportCallHasSubtitlesAndAudio_OrOnlySubtitlesWithoutARecording()
    {
        CallJob job = Job("Yuki", Day(10, 1), ("Me", "えーと。"), ("Yuki", "やっぱりね。"));
        FakeClipper clipper = new();
        MiningService mining = Mining(clipper);

        (MiningFile? file, _) = await mining.ExportCallAsync(UserId, job.Id!, MiningVoices.Partner, CancellationToken.None);
        File.Delete(job.SourcePath);
        (MiningFile? textOnly, _) = await mining.ExportCallAsync(UserId, job.Id!, MiningVoices.Both, CancellationToken.None);

        using ZipArchive zip = new(new MemoryStream(file!.Data));
        Assert.Equal(["yuki_2026-10-01_partner.srt", "yuki_2026-10-01_partner.mp3"], zip.Entries.Select(e => e.Name));
        string srt = new StreamReader(zip.Entries[0].Open()).ReadToEnd();
        Assert.Equal($"1{Environment.NewLine}00:00:05,000 --> 00:00:09,000{Environment.NewLine}やっぱりね。{Environment.NewLine}{Environment.NewLine}", srt);
        Assert.Equal([MiningVoices.Partner], clipper.Tracks);

        using ZipArchive subtitles = new(new MemoryStream(textOnly!.Data));
        Assert.Equal(["yuki_2026-10-01.srt", MiningExport.ReadmeFileName], subtitles.Entries.Select(e => e.Name));
        string both = new StreamReader(subtitles.Entries[0].Open()).ReadToEnd();
        Assert.Contains("Me: えーと。", both);
        Assert.Contains("Yuki: やっぱりね。", both);
    }

    [Fact]
    public void AnkiCsv_QuotesFieldsWithCommasAndQuotes()
    {
        MinedSentence sentence = new() { Text = "そう、\"いい\"ね", Target = "#tag", CallName = "Call", Date = Day(10, 1), Start = 65 };

        string csv = MiningExport.AnkiCsv([(sentence, null)]);

        Assert.EndsWith("\"そう、\"\"いい\"\"ね\",\"#tag\",,\"Call, 2026-10-01 01:05\"" + Environment.NewLine, csv);
    }

    [Theory]
    [InlineData(1, 0, "pan=mono|c0=c1")]
    [InlineData(2, 0, "pan=mono|c0=c0")]
    [InlineData(1, 2, "pan=mono|c0=c0")]
    [InlineData(0, 0, null)]
    [InlineData(1, 1, null)]
    public void ChannelFilter_PicksTheRightSide(int learner, int voices, string? filter)
    {
        string[] arguments = AudioClipper.ChannelFilter((LearnerChannel)learner, (MiningVoices)voices);

        Assert.Equal(filter == null ? [] : ["-af", filter], arguments);
    }

    [Fact]
    public async Task MiningHandler_AddsGetsListsAndRemoves()
    {
        CallJob job = Job("Yuki", Day(10, 1), ("Me", "えーと。"), ("Yuki", "やっぱりね。"));
        MiningRequestHandler handler = new(Mining(), NullLogger<MiningRequestHandler>.Instance);

        MiningResponseTransmission added = await handler.ProcessAsync(UserId, new MiningRequestTransmission { Action = MiningAction.Add, CallId = job.Id!, Time = 5, Text = "やっぱりね。" });
        await MiningService.WaitForClipAsync(added.Sentence!.Id);
        MiningResponseTransmission got = await handler.ProcessAsync(UserId, new MiningRequestTransmission { Action = MiningAction.Get, Id = added.Sentence.Id });
        MiningResponseTransmission listed = await handler.ProcessAsync(UserId, new MiningRequestTransmission { Action = MiningAction.List });
        MiningResponseTransmission removed = await handler.ProcessAsync(UserId, new MiningRequestTransmission { Action = MiningAction.Remove, Id = added.Sentence.Id });
        MiningResponseTransmission gone = await handler.ProcessAsync(UserId, new MiningRequestTransmission { Action = MiningAction.Get, Id = added.Sentence.Id });

        Assert.Equal("やっぱりね。", added.Sentence.Text);
        Assert.Equal(MinedClipState.Ready, got.Sentence!.State);
        Assert.Single(listed.Sentences);
        Assert.Null(removed.Error);
        Assert.Null(gone.Sentence);
        Assert.NotNull(gone.Error);
    }
}
