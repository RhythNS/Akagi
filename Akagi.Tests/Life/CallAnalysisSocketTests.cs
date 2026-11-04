using Akagi.Bridge.Chat.Models;
using Akagi.Bridge.Chat.Transmissions.Requests;
using Akagi.Bridge.Chat.Transmissions.Responses;
using Akagi.Communication.SocketComs.Transmissions;
using Akagi.Characters.Companions;
using Akagi.Life.Japanese;
using Akagi.Tests.Characters;
using Akagi.Life.Japanese.Calls;
using Akagi.Tests.Support;
using MessagePack;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Bson;

namespace Akagi.Tests.Life;

/// <summary>
/// What the website's call analysis pages get from the server.
/// </summary>
public class CallAnalysisSocketTests
{
    private const string UserId = "6adcc28c4f351a3450e30ab6";
    private const string OtherUserId = "6adcc28c4f351a3450e30ab7";

    private readonly InMemoryCallJobDatabase _jobs = new();
    private readonly InMemoryJapaneseDiagnosisDatabase _diagnoses = new();
    private readonly InMemoryPersonProfileDatabase _people = new();
    private readonly InMemoryMemoryDatabase _memories = new();
    private readonly InMemoryCompanionStateDatabase _companions = new();
    private readonly CallMemory _memory;
    private readonly CallAnalysisRequestHandler _handler;

    public CallAnalysisSocketTests()
    {
        _memory = new CallMemory(_people, _memories, _companions, null, NullLogger<CallMemory>.Instance);
        _handler = new CallAnalysisRequestHandler(_jobs, _diagnoses, _people, _memory, NullLogger<CallAnalysisRequestHandler>.Instance);
    }

    private CallJob AddJob(string userId, string partner, DateTime recordedAt, CallJobStatus status = CallJobStatus.Completed, string learnerText = "えーと、仕事がすごく忙しいです。えーと、毎日残業です。")
    {
        CallJob job = new()
        {
            Id = ObjectId.GenerateNewId().ToString(),
            UserId = userId,
            Title = $"{recordedAt:yyyy-MM-dd} {partner} [left]",
            Partner = partner,
            LearnerChannel = LearnerChannel.Left,
            LearnerSpeaker = CallJob.DefaultLearnerLabel,
            RecordedAt = recordedAt,
            CreatedAt = recordedAt.AddHours(1),
            UpdatedAt = recordedAt.AddHours(2),
            Status = status,
            Report = "Call analysis: done.",
            Segments =
            [
                new TranscriptSegment { Start = 0, End = 4, Speaker = partner, Text = "最近どう？仕事は大変？" },
                new TranscriptSegment { Start = 5, End = 9, Speaker = "Me", Text = learnerText, Disputed = "残業 -> 残業中" },
                new TranscriptSegment { Start = 10, End = 14, Speaker = "Me", Text = "えーと、でも休むに越したことはないです。" },
                new TranscriptSegment { Start = 15, End = 17, Speaker = partner, Text = "無理しないでね。" }
            ],
            Chunks = [new CallChunk { Index = 0, Start = 0, End = 17, Analyzed = true, IssuesRecorded = 2 }]
        };
        _jobs.SaveDocumentAsync(job).Wait();
        return job;
    }

    private Task<CallAnalysisResponseTransmission> Send(CallAnalysisAction action, string callId = "", string userId = UserId,
        string findingId = "", CallFindingFeedback feedback = CallFindingFeedback.None, string name = "") =>
        _handler.ProcessAsync(userId, new CallAnalysisRequestTransmission { RequestId = "r1", Action = action, CallId = callId, FindingId = findingId, Feedback = feedback, Name = name });

    /// <summary>
    /// A call analyzed the current way: findings on the call, counted in the diagnosis.
    /// </summary>
    private async Task<(CallJob Job, JapaneseDiagnosis Diagnosis)> Analyzed()
    {
        CallJob job = AddJob(UserId, "Yuki", new DateTime(2026, 10, 20, 0, 0, 0, DateTimeKind.Utc));
        JapaneseDiagnosis diagnosis = new() { UserId = UserId };
        job.Review = new CallReview { Summary = "Lively call.", Priorities = ["Casual endings"] };
        CallFinding first = new() { Kind = FindingKind.Mistake, Category = IssueCategory.UnnaturalPhrasing, Pattern = "すごく忙しい", Said = "すごく忙しいです", Better = "めっちゃ忙しい", Explanation = "Stiff.", Severity = 3, State = FindingState.Kept, Time = 5, Quote = QuoteCheck.Found };
        CallFinding second = new() { Kind = FindingKind.Mistake, Category = IssueCategory.UnnaturalPhrasing, Pattern = "すごく忙しい", Said = "毎日残業です", Better = "毎日残業", Severity = 2, State = FindingState.Kept, Time = 5, Quote = QuoteCheck.Disputed };
        CallFinding dropped = new() { Kind = FindingKind.Mistake, Category = IssueCategory.Particle, Pattern = "は vs が", Said = "仕事が", State = FindingState.Dropped, Note = "natives say this too" };
        CallFinding phrase = new() { Kind = FindingKind.Phrase, Said = "無理しないでね", Better = "take it easy", State = FindingState.Kept, Time = 15 };
        CallFinding strength = new() { Kind = FindingKind.Strength, Pattern = "uses ～に越したことはない", Said = "休むに越したことはない", State = FindingState.Kept };
        CallFinding missed = new() { Kind = FindingKind.Conversation, Aspect = "follow-up", Context = "仕事は大変？", Said = "すごく忙しいです", Better = "うん、Yukiは？", State = FindingState.Kept, Time = 5 };
        CallFinding good = new() { Kind = FindingKind.Conversation, Aspect = "reaction", Japanese = true, WentWell = true, Said = "休むに越したことはない", State = FindingState.Kept, Time = 10 };
        CallFinding exam = new() { Kind = FindingKind.Fact, Subject = FactSubject.Partner, Aspect = "plans", Pattern = "Yuki has an exam on Friday.", Said = "無理しないでね", FollowUp = true, State = FindingState.Kept, Time = 15 };
        CallFinding overtime = new() { Kind = FindingKind.Fact, Subject = FactSubject.Learner, Aspect = "work", Pattern = "Works overtime every day.", Said = "毎日残業です", State = FindingState.Kept };
        job.Findings = [first, second, dropped, phrase, strength, missed, good, exam, overtime];
        job.DeliveryCharacterId = "6ae1ebfa5a533276d716f834";
        job.Coaching = new CallCoaching { Summary = "You answer well but rarely ask back.", Tips = ["Ask back with ～は？"] };
        job.FactsChecked = true;
        foreach (CallFinding finding in job.Findings)
        {
            CallDiagnosis.Apply(diagnosis, job, finding, DateTime.UtcNow);
            await _memory.ApplyAsync(job, finding, DateTime.UtcNow);
        }
        await _diagnoses.SaveDocumentAsync(diagnosis);
        await _jobs.SaveDocumentAsync(job);
        return (job, diagnosis);
    }

    [Fact]
    public async Task List_ShowsOnlyTheUsersCalls_NewestFirst_WithTheirSpeakingShare()
    {
        CallJob older = AddJob(UserId, "Yuki", new DateTime(2026, 10, 20, 0, 0, 0, DateTimeKind.Utc));
        CallJob newer = AddJob(UserId, "Ken", new DateTime(2026, 10, 27, 0, 0, 0, DateTimeKind.Utc), CallJobStatus.Analyzing);
        AddJob(OtherUserId, "Someone", new DateTime(2026, 10, 28, 0, 0, 0, DateTimeKind.Utc));

        CallAnalysisResponseTransmission response = await Send(CallAnalysisAction.List);

        Assert.Equal("r1", response.RequestId);
        Assert.Equal([newer.Id, older.Id], response.Calls.Select(c => c.Id));
        Assert.Equal(CallAnalysisState.Analyzing, response.Calls[0].State);
        Assert.Equal(17, response.Calls[1].DurationSeconds);
        Assert.Equal(8.0 / 14.0, response.Calls[1].LearnerShare!.Value, 3);
        // Statistics are computed once and kept, but only on finished calls; a running one belongs to the pipeline
        Assert.NotNull(_jobs.Items.Single(j => j.Id == older.Id).Stats);
    }

    [Fact]
    public async Task Detail_ShowsTheCallsOwnFindings_GroupedByIssue()
    {
        (CallJob job, JapaneseDiagnosis diagnosis) = await Analyzed();

        CallAnalysisDetail detail = (await Send(CallAnalysisAction.Detail, job.Id!)).Detail!;

        CallStruggle struggle = Assert.Single(detail.Struggles);
        Assert.Equal(("Unnatural phrasing", "すごく忙しい", 3, 2, 2), (struggle.Category, struggle.Pattern, struggle.Severity, struggle.TimesInCall, struggle.TimesOverall));
        Assert.Equal(diagnosis.Issues.Single(i => i.Pattern == "すごく忙しい").Id, struggle.IssueId);
        Assert.Equal(CallQuoteState.Possibly, struggle.Findings[1].Quote);
        Assert.Equal("natives say this too", Assert.Single(detail.FilteredOut).Note);
        Assert.Equal("無理しないでね", Assert.Single(detail.Phrases).Said);
        Assert.Equal("uses ～に越したことはない", Assert.Single(detail.Strengths).Pattern);
        Assert.Equal(("Lively call.", "Casual endings"), (detail.ReviewSummary, detail.Priorities.Single()));
        Assert.True(detail.CanGiveFeedback);
        Assert.False(detail.Legacy);

        Assert.True(detail.Flow!.Separated);
        Assert.Equal(2, detail.Flow.PartnerTurns);
        Assert.Equal(1, detail.Flow.MedianResponseSeconds);
        Assert.Contains(detail.Vocabulary!.Words, w => w.Lemma == "忙しい" && w.Learner == 1 && w.NewForLearner);
        Assert.Contains(detail.Vocabulary.Words, w => w.Lemma == "無理" && w.Partner == 1 && w.Learner == 0);
        Assert.Contains(detail.Speech!.Fillers, f => f.Form == "えーと" && f.Count == 3);
        Assert.Contains(detail.Speech.Grammar, g => g.Count > 0);
        Assert.True(detail.Transcript[1].IsLearner);
    }

    [Fact]
    public async Task Feedback_TakesAFindingOutOfTheDiagnosis_AndBack()
    {
        (CallJob job, JapaneseDiagnosis diagnosis) = await Analyzed();
        string findingId = job.Findings[0].Id;

        CallAnalysisResponseTransmission response = await Send(CallAnalysisAction.Feedback, job.Id!, findingId: findingId, feedback: CallFindingFeedback.NotAMistake);

        Assert.Null(response.Error);
        CallStruggle struggle = response.Detail!.Struggles.Single();
        Assert.Equal((1, 1), (struggle.TimesInCall, struggle.TimesOverall));
        Assert.Equal(CallFindingFeedback.NotAMistake, struggle.Findings.Single(f => f.Id == findingId).Feedback);
        Assert.Equal(1, _diagnoses.Items.Single().Issues.Single(i => i.Pattern == "すごく忙しい").Count);
        Assert.Contains("1 issue(s) noted", _jobs.Items.Single(j => j.Id == job.Id).Report);

        response = await Send(CallAnalysisAction.Feedback, job.Id!, findingId: findingId, feedback: CallFindingFeedback.Useful);

        Assert.Equal(2, response.Detail!.Struggles.Single().TimesOverall);
    }

    [Fact]
    public async Task Feedback_IsRefusedForDroppedFindingsAndRunningCalls()
    {
        (CallJob job, _) = await Analyzed();
        CallJob running = AddJob(UserId, "Ken", new DateTime(2026, 10, 27, 0, 0, 0, DateTimeKind.Utc), CallJobStatus.Analyzing);

        Assert.NotNull((await Send(CallAnalysisAction.Feedback, job.Id!, findingId: job.Findings[2].Id, feedback: CallFindingFeedback.NotAMistake)).Error);
        Assert.NotNull((await Send(CallAnalysisAction.Feedback, running.Id!, findingId: "x", feedback: CallFindingFeedback.NotAMistake)).Error);
    }

    [Fact]
    public async Task Detail_ForAnOldCall_CopiesWhatTheDiagnosisStillHas()
    {
        CallJob job = AddJob(UserId, "Yuki", new DateTime(2026, 10, 20, 0, 0, 0, DateTimeKind.Utc));
        JapaneseDiagnosis diagnosis = new() { UserId = UserId };
        diagnosis.RecordIssue(IssueCategory.UnnaturalPhrasing, "すごく忙しい", "Sounds stiff here.",
            new IssueExample { Said = "すごく忙しいです", Better = "めっちゃ忙しい", Source = job.SourceLabel }, 3, DateTime.UtcNow);
        diagnosis.RecordIssue(IssueCategory.Particle, "に vs で", "Place of action takes で.",
            new IssueExample { Said = "家に勉強する", Better = "家で勉強する", Source = "chat" }, 2, DateTime.UtcNow);
        await _diagnoses.SaveDocumentAsync(diagnosis);

        CallAnalysisDetail detail = (await Send(CallAnalysisAction.Detail, job.Id!)).Detail!;

        Assert.True(detail.Legacy);
        CallFindingView finding = Assert.Single(Assert.Single(detail.Struggles).Findings);
        Assert.Equal(("earlier analysis", CallQuoteState.Found, 5.0), (finding.Origin, finding.Quote, finding.Time));
        CallJob stored = _jobs.Items.Single(j => j.Id == job.Id);
        Assert.True(stored.LegacyImported);
        Assert.Single(stored.Findings);
    }

    [Fact]
    public async Task Insights_GatherWordsTrendsStrugglesAndPhrasesAcrossCalls()
    {
        (CallJob first, _) = await Analyzed();
        CallJob second = AddJob(UserId, "Ken", new DateTime(2026, 10, 27, 0, 0, 0, DateTimeKind.Utc), learnerText: "最近、仕事が忙しい。週末は映画を見た。");

        CallInsights insights = (await Send(CallAnalysisAction.Insights)).Insights!;

        Assert.Equal([first.Id, second.Id], insights.Calls.Select(c => c.CallId));
        Assert.True(insights.Calls[1].NewWords > 0);
        Assert.All(insights.Calls, c => Assert.True(c.Separated));
        Assert.NotNull(insights.Calls[0].FillersPer100);
        InsightWord busy = insights.Words.Single(w => w.Lemma == "忙しい");
        Assert.Equal((2, 2), (busy.Learner, busy.Calls));
        Assert.Equal(first.RecordedAt, busy.FirstUsed);
        // Both partners said 無理 or 大変, the learner never did
        Assert.Contains(insights.HeardNotSaid, w => w.Lemma == "無理" && w.Partner == 2);
        Assert.DoesNotContain(insights.HeardNotSaid, w => w.Lemma == "仕事");
        InsightStruggle struggle = Assert.Single(insights.Struggles);
        Assert.Equal((2, 1), (struggle.Times, struggle.Calls));
        Assert.Equal("無理しないでね", Assert.Single(insights.Phrases).Phrase);
    }

    [Fact]
    public async Task Detail_ShowsCoachingAndFacts()
    {
        (CallJob job, _) = await Analyzed();

        CallAnalysisDetail detail = (await Send(CallAnalysisAction.Detail, job.Id!)).Detail!;

        Assert.True(detail.CoachingDone && detail.FactsDone);
        Assert.Equal(("You answer well but rarely ask back.", "Ask back with ～は？"), (detail.CoachingSummary, detail.CoachingTips.Single()));
        // What to improve comes first
        Assert.Equal(["follow-up", "reaction"], detail.Conversation.Select(c => c.Aspect));
        Assert.Equal(("仕事は大変？", false), (detail.Conversation[0].Context, detail.Conversation[0].Japanese));
        Assert.True(detail.Conversation[1].Japanese && detail.Conversation[1].WentWell);
        CallFindingView exam = Assert.Single(detail.PartnerFacts);
        Assert.True(exam.AboutPartner && exam.FollowUp);
        Assert.Equal("Works overtime every day.", Assert.Single(detail.LearnerFacts).Pattern);
        Assert.Equal(2, detail.Flow!.PartnerQuestions);
    }

    [Fact]
    public async Task People_ListThePartners_AndTheirPageHoldsEverything()
    {
        (CallJob job, _) = await Analyzed();

        PersonSummary person = Assert.Single((await Send(CallAnalysisAction.People)).People);
        Assert.Equal(("Yuki", 1, 1, 1), (person.Name, person.Calls, person.Facts, person.OpenThreads));

        PersonDetail detail = (await Send(CallAnalysisAction.Person, name: "yuki")).Person!;
        PersonFactView fact = Assert.Single(detail.Facts);
        Assert.Equal(("Yuki has an exam on Friday.", "plans", job.Id, "Call with Yuki", 15.0), (fact.Text, fact.Topic, fact.CallId, fact.CallName, fact.Time));
        Assert.Equal(job.Id, Assert.Single(detail.Calls).CallId);
        Assert.Equal("無理しないでね", Assert.Single(detail.Phrases).Phrase);
        Assert.NotNull((await Send(CallAnalysisAction.Person, name: "Nobody")).Error);
    }

    [Fact]
    public async Task AWrongFact_LeavesThePersonAndTheMemories()
    {
        (CallJob job, _) = await Analyzed();
        CallFinding exam = job.Findings.Single(f => f.Pattern == "Yuki has an exam on Friday.");
        CallFinding overtime = job.Findings.Single(f => f.Pattern == "Works overtime every day.");
        Assert.Single(_memories.Items);
        Assert.Contains("Yuki has an exam on Friday.", _companions.Items.Single().FindPerson("Yuki")!.Notes);

        await Send(CallAnalysisAction.Feedback, job.Id!, findingId: exam.Id, feedback: CallFindingFeedback.NotAMistake);
        await Send(CallAnalysisAction.Feedback, job.Id!, findingId: overtime.Id, feedback: CallFindingFeedback.NotAMistake);

        Assert.Empty(_people.Items.Single().Facts);
        Assert.Empty(_memories.Items);
        Assert.Empty(_companions.Items.Single().FindPerson("Yuki")!.Notes);

        CallAnalysisDetail detail = (await Send(CallAnalysisAction.Feedback, job.Id!, findingId: exam.Id, feedback: CallFindingFeedback.None)).Detail!;
        Assert.Single(_people.Items.Single().Facts);
        Assert.Equal(CallFindingFeedback.None, detail.PartnerFacts.Single().Feedback);
    }

    [Fact]
    public async Task SetDate_MovesTheCall_AndRefusesARunningOne()
    {
        (CallJob job, JapaneseDiagnosis _) = await Analyzed();
        CallJob later = AddJob(UserId, "Ken", new DateTime(2026, 10, 27, 0, 0, 0, DateTimeKind.Utc));
        CallJob running = AddJob(UserId, "Ken", new DateTime(2026, 10, 28, 0, 0, 0, DateTimeKind.Utc), CallJobStatus.Analyzing);

        CallAnalysisResponseTransmission response = await _handler.ProcessAsync(UserId,
            new CallAnalysisRequestTransmission { Action = CallAnalysisAction.SetDate, CallId = job.Id!, Date = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc) });

        Assert.Null(response.Error);
        Assert.Equal(new DateTime(2026, 11, 1), response.Detail!.Summary.Date);
        Assert.True(response.Detail.Summary.DateKnown);
        Assert.Equal(job.Id, response.Calls[0].Id);
        Assert.Contains("call with Yuki 2026-11-01", _jobs.Items.Single(j => j.Id == job.Id).Report);
        Assert.All(_diagnoses.Items.Single().Issues.SelectMany(i => i.Examples), e => Assert.Equal("call with Yuki 2026-11-01", e.Source));
        Assert.Equal(new DateTime(2026, 11, 1), _people.Items.Single().Facts.Single().Date);

        Assert.NotNull((await _handler.ProcessAsync(UserId,
            new CallAnalysisRequestTransmission { Action = CallAnalysisAction.SetDate, CallId = running.Id!, Date = DateTime.UtcNow })).Error);
        Assert.NotNull((await _handler.ProcessAsync(UserId,
            new CallAnalysisRequestTransmission { Action = CallAnalysisAction.SetDate, CallId = later.Id! })).Error);
    }

    [Fact]
    public async Task Detail_RefusesSomeoneElsesCall()
    {
        CallJob foreign = AddJob(OtherUserId, "Someone", new DateTime(2026, 10, 28, 0, 0, 0, DateTimeKind.Utc));

        CallAnalysisResponseTransmission response = await Send(CallAnalysisAction.Detail, foreign.Id!);

        Assert.Null(response.Detail);
        Assert.NotNull(response.Error);
    }

    [Fact]
    public async Task DetailAndInsights_SurviveTheWire()
    {
        (CallJob job, _) = await Analyzed();

        CallAnalysisResponseTransmission detail = await Send(CallAnalysisAction.Detail, job.Id!);
        CallAnalysisResponseTransmission insights = await Send(CallAnalysisAction.Insights);
        CallAnalysisResponseTransmission detailCopy = MessagePackSerializer.Deserialize<CallAnalysisResponseTransmission>(MessagePackSerializer.Serialize(detail));
        CallAnalysisResponseTransmission insightsCopy = MessagePackSerializer.Deserialize<CallAnalysisResponseTransmission>(MessagePackSerializer.Serialize(insights));

        Assert.Equal(job.Id, detailCopy.Detail!.Summary.Id);
        Assert.Equal(4, detailCopy.Detail.Transcript.Length);
        Assert.Equal(detail.Detail!.Vocabulary!.Words.Length, detailCopy.Detail.Vocabulary!.Words.Length);
        Assert.Equal(2, detailCopy.Detail.Struggles[0].Findings.Length);
        Assert.Equal(insights.Insights!.Words.Length, insightsCopy.Insights!.Words.Length);
    }

    [Theory]
    [InlineData("UnnaturalPhrasing", "Unnatural phrasing")]
    [InlineData("Particle", "Particle")]
    [InlineData("GrammarPattern", "Grammar pattern")]
    public void Humanize_SplitsCategoryNames(string name, string expected)
    {
        Assert.Equal(expected, CallAnalysisViews.Humanize(name));
    }
}
