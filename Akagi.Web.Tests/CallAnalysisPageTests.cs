using Akagi.Bridge.Chat.Models;
using Akagi.Bridge.Chat.Transmissions.Requests;
using Akagi.Bridge.Chat.Transmissions.Responses;
using Akagi.Web.Models;
using Akagi.Web.Pages.Learning;
using Akagi.Web.Services.Sockets;
using Akagi.Web.Services.Sockets.Requests;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net;

namespace Akagi.Web.Tests;

/// <summary>
/// The call analysis page against a client whose requests the test answers itself, as the server would.
/// </summary>
public class CallAnalysisPageTests : BunitContext
{
    private sealed class Sockets : ISocketClientContainer
    {
        public SocketClient? Client { get; init; }
        public bool IsConnected => Client != null;
#pragma warning disable CS0067
        public event Action? ConnectionStateChanged;
#pragma warning restore CS0067
        public Task<SocketClient?> EnsureConnectedAsync() => Task.FromResult(Client);
        public Task TryConnectAsync() => Task.CompletedTask;
        public Task TryDisconnectAsync() => Task.CompletedTask;
    }

    private readonly SocketClient _client;
    private readonly HashSet<CallAnalysisRequest> _answered = [];

    public CallAnalysisPageTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        _client = new SocketClient(IPAddress.Loopback, 1, new User { GoogleId = "g", Name = "Tester", Email = "t" }, "circuit",
            NullLoggerFactory.Instance, [], null!);
        // Never connected; its finalizer would report the removal to a service that does not exist here
        GC.SuppressFinalize(_client);
    }

    private IRenderedComponent<CallAnalysisPage> RenderOnline()
    {
        Services.AddSingleton<ISocketClientContainer>(new Sockets { Client = _client });
        return Render<CallAnalysisPage>();
    }

    private CallAnalysisRequestTransmission Respond(Func<CallAnalysisRequestTransmission, CallAnalysisResponseTransmission> answer)
    {
        CallAnalysisRequest? request = null;
        for (int i = 0; i < 200 && request == null; i++)
        {
            // An answered request leaves the client only once its caller resumes
            request = _client.GetRequests<CallAnalysisRequest>().FirstOrDefault(r => !_answered.Contains(r));
            if (request == null)
            {
                Thread.Sleep(10);
            }
        }
        Assert.NotNull(request);
        _answered.Add(request);
        request.Fulfill(answer(request.Transmission));
        return request.Transmission;
    }

    private static CallAnalysisSummary Summary(string id, string partner, CallAnalysisState state = CallAnalysisState.Completed) => new()
    {
        Id = id,
        Partner = partner,
        State = state,
        CreatedAt = new DateTime(2026, 10, 27, 0, 0, 0, DateTimeKind.Utc),
        DurationSeconds = 1800,
        IssueCount = 4,
        ChunkCount = 3,
        ChunksAnalyzed = 3
    };

    private static CallFindingView Finding(string id, string said, string better = "", double? time = null,
        CallQuoteState quote = CallQuoteState.Found, CallFindingKind kind = CallFindingKind.Mistake) => new()
    {
        Id = id,
        Kind = kind,
        Origin = "review",
        Category = "Unnatural phrasing",
        Pattern = "すごく忙しい",
        Said = said,
        Better = better,
        Time = time,
        Quote = quote,
        State = CallFindingState.Kept
    };

    private static CallAnalysisDetail Detail(CallAnalysisSummary summary) => new()
    {
        Summary = summary,
        Register = "casual",
        Report = "Call analysis: the whole report.",
        CanGiveFeedback = summary.State == CallAnalysisState.Completed,
        ReviewSummary = "A relaxed call with good listening.",
        CoachingDone = true,
        FactsDone = true,
        CoachingSummary = "You answer fully but rarely ask back.",
        CoachingTips = ["Ask back: ユキは？"],
        Conversation =
        [
            new CallFindingView { Id = "m1", Kind = CallFindingKind.Conversation, Origin = "review", Aspect = "follow-up", Context = "最近どう？", Said = "えーと、忙しいです。", Better = "忙しいよ！ユキは？", Explanation = "Ask back.", State = CallFindingState.Kept, Time = 65 },
            new CallFindingView { Id = "m2", Kind = CallFindingKind.Conversation, Origin = "review", Aspect = "reaction", Japanese = true, WentWell = true, Said = "毎日残業です。", Explanation = "Natural.", State = CallFindingState.Kept, Time = 70 }
        ],
        PartnerFacts = [new CallFindingView { Id = "pf1", Kind = CallFindingKind.Fact, Origin = "review", Aspect = "plans", Pattern = "Yuki has an exam on Friday.", Said = "無理しないでね", AboutPartner = true, FollowUp = true, State = CallFindingState.Kept, Time = 0 }],
        LearnerFacts = [new CallFindingView { Id = "lf1", Kind = CallFindingKind.Fact, Origin = "review", Aspect = "work", Pattern = "Works overtime every day.", Said = "毎日残業です", State = CallFindingState.Kept, Time = 70 }],
        Priorities = ["Drop です with friends", "Vary 思う"],
        Struggles =
        [
            new CallStruggle
            {
                IssueId = "a1", Category = "Unnatural phrasing", Pattern = "すごく忙しい", Explanation = "Sounds stiff.", Severity = 3,
                Status = "active", TimesInCall = 2, TimesOverall = 5,
                Findings = [Finding("f1", "すごく忙しいです", "めっちゃ忙しい", 65), Finding("f2", "毎日残業です", "毎日残業", 70, CallQuoteState.Possibly)]
            },
            new CallStruggle { IssueId = "b2", Category = "Particle", Pattern = "に vs で", Severity = 2, Status = "improving", TimesInCall = 1, TimesOverall = 3 }
        ],
        Speech = new CallSpeech
        {
            LearnerOnly = true,
            FillersPer100 = 2.5,
            Fillers = [new CallFiller { Form = "えーと", Count = 9, IsHabit = true }],
            PoliteRatio = 0.8,
            ExpectedRegister = "casual",
            RegisterMismatch = "Too polite for a friend.",
            Grammar = [new CallGrammarUse { Pattern = "～わけではない", Meaning = "it is not that", Count = 2 }]
        },
        Strengths = [new CallFindingView { Id = "s1", Kind = CallFindingKind.Strength, Origin = "review", Pattern = "Natural back-channeling", State = CallFindingState.Kept }],
        CorrectUses = [new CallFindingView { Id = "c1", Kind = CallFindingKind.Correct, Origin = "review", Said = "公園で遊んだ", IssuePattern = "に vs で", State = CallFindingState.Kept, Time = 0 }],
        Phrases = [new CallFindingView { Id = "p1", Kind = CallFindingKind.Phrase, Origin = "review", Said = "無理しないでね", Better = "take it easy", State = CallFindingState.Kept }],
        FilteredOut = [new CallFindingView { Id = "x1", Origin = "review", Category = "Particle", Pattern = "invented", Said = "誰も言っていない", State = CallFindingState.Rejected, Quote = CallQuoteState.NotFound, Note = "The quoted words are not in the transcript." }],
        Flow = new CallFlow
        {
            Separated = true,
            HasPartner = true,
            LearnerSeconds = 600,
            PartnerSeconds = 400,
            LearnerCharacters = 3000,
            PartnerCharacters = 1000,
            LearnerTurns = 40,
            PartnerTurns = 41,
            LearnerCharactersPerTurn = 75,
            PartnerCharactersPerTurn = 24.4,
            MedianResponseSeconds = 0.8,
            LongPauses = 3,
            Windows = [new CallFlowWindow { Start = 0, LearnerSeconds = 180, PartnerSeconds = 100 }, new CallFlowWindow { Start = 300, LearnerSeconds = 120, PartnerSeconds = 150 }]
        },
        Vocabulary = new CallVocabulary
        {
            Separated = true,
            LearnerWords = 3,
            LearnerDistinct = 2,
            PartnerWords = 2,
            PartnerDistinct = 2,
            Words =
            [
                new CallWordView { Lemma = "忙しい", Reading = "いそがしい", Class = "Adjective", Learner = 2, Partner = 1, Surfaces = ["忙しい"], NewForLearner = true },
                new CallWordView { Lemma = "残業", Reading = "ざんぎょう", Class = "Noun", Learner = 1, Surfaces = ["残業"] },
                new CallWordView { Lemma = "無理", Reading = "むり", Class = "AdjectivalNoun", Partner = 1, Surfaces = ["無理"] }
            ]
        },
        Transcript =
        [
            new CallTranscriptLine { Start = 0, Speaker = "Yuki", Text = "最近どう？無理しないでね。" },
            new CallTranscriptLine { Start = 65, Speaker = "Me", Text = "えーと、忙しいです。", IsLearner = true, Disputed = "忙しい -> 忙しく" },
            new CallTranscriptLine { Start = 70, Speaker = "Me", Text = "毎日残業です。", IsLearner = true }
        ]
    };

    [Fact]
    public void WithoutAConnection_OffersARetry()
    {
        Services.AddSingleton<ISocketClientContainer>(new Sockets());

        IRenderedComponent<CallAnalysisPage> cut = Render<CallAnalysisPage>();

        cut.WaitForAssertion(() => Assert.Contains("Socket Service Offline", cut.Markup));
    }

    [Fact]
    public void WithoutCalls_ExplainsHowToGetOne()
    {
        IRenderedComponent<CallAnalysisPage> cut = RenderOnline();
        Respond(_ => new CallAnalysisResponseTransmission());

        cut.WaitForAssertion(() => Assert.Contains("No calls analyzed yet", cut.Markup));
    }

    [Fact]
    public void OpensTheNewestFinishedCall_AndShowsEverythingItFound()
    {
        CallAnalysisSummary running = Summary("run", "Ken", CallAnalysisState.Analyzing);
        CallAnalysisSummary done = Summary("done", "Yuki");
        IRenderedComponent<CallAnalysisPage> cut = RenderOnline();

        Respond(_ => new CallAnalysisResponseTransmission { Calls = [running, done] });
        CallAnalysisRequestTransmission asked = Respond(_ => new CallAnalysisResponseTransmission { Detail = Detail(done) });

        Assert.Equal(CallAnalysisAction.Detail, asked.Action);
        Assert.Equal("done", asked.CallId);
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("[data-call-detail='done']")));
        Assert.Contains("Call with Ken", cut.Markup);
        Assert.Contains("めっちゃ忙しい", cut.Markup);
        Assert.Contains("Too polite for a friend.", cut.Markup);
        Assert.Contains("～わけではない", cut.Markup);
        Assert.Contains("Natural back-channeling", cut.Markup);
        Assert.Contains("A relaxed call with good listening.", cut.Find("[data-review]").TextContent);
        Assert.Contains("無理しないでね", cut.Find("[data-phrases]").TextContent);
        Assert.Contains("公園で遊んだ", cut.Find("[data-went-well]").TextContent);
        Assert.Equal("2", cut.Find("[data-stat='struggles']").TextContent.Trim());
        Assert.Equal("60%", cut.Find("[data-stat='share']").TextContent.Trim());
        Assert.Equal("0.8 s", cut.Find("[data-stat='response']").TextContent.Trim());
        Assert.Contains("possibly", cut.Find("[data-finding='f2']").TextContent);

        // The report and the filtered findings stay folded until asked for
        Assert.DoesNotContain("Call analysis: the whole report.", cut.Markup);
        cut.Find("[data-report-toggle]").Click();
        Assert.Contains("Call analysis: the whole report.", cut.Markup);
        Assert.DoesNotContain("誰も言っていない", cut.Markup);
        cut.Find("[data-filtered-toggle]").Click();
        Assert.Contains("The quoted words are not in the transcript.", cut.Find("[data-filtered]").TextContent);

        cut.Find("[data-category='Particle']").Click();
        Assert.Empty(cut.FindAll("[data-struggle='a1']"));
        Assert.NotEmpty(cut.FindAll("[data-struggle='b2']"));

        Assert.Empty(cut.FindAll("[data-transcript]"));
        cut.Find("[data-transcript-toggle]").Click();
        Assert.Contains("01:05", cut.Find("[data-transcript]").TextContent);
        Assert.Contains("忙しい -> 忙しく", cut.Find("[data-transcript]").TextContent);
    }

    private IRenderedComponent<CallAnalysisPage> OpenDone()
    {
        IRenderedComponent<CallAnalysisPage> cut = RenderOnline();
        Respond(_ => new CallAnalysisResponseTransmission { Calls = [Summary("done", "Yuki")] });
        Respond(_ => new CallAnalysisResponseTransmission { Detail = Detail(Summary("done", "Yuki")) });
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("[data-call-detail='done']")));
        return cut.Settled();
    }

    [Fact]
    public void AFindingsTime_OpensTheTranscriptAtItsLine()
    {
        IRenderedComponent<CallAnalysisPage> cut = OpenDone();

        // The page finishes its async load in a last render; a click racing it is simply repeated, as jumping is idempotent
        cut.WaitForAssertion(() =>
        {
            cut.Find("[data-jump='f2']").Click();
            Assert.Equal("2", cut.Find(".call-line-target").GetAttribute("data-line"));
        });
    }

    [Fact]
    public void AWord_ShowsOnlyTheLinesWithIt()
    {
        IRenderedComponent<CallAnalysisPage> cut = OpenDone();
        Assert.Contains("new", cut.Find("[data-word='忙しい']").TextContent);
        Assert.Empty(cut.FindAll("[data-word='無理']"));

        cut.WaitForAssertion(() =>
        {
            cut.Find("[data-word='残業']").Click();
            IReadOnlyList<AngleSharp.Dom.IElement> lines = cut.FindAll("[data-line]");
            Assert.Equal("2", Assert.Single(lines).GetAttribute("data-line"));
            Assert.Contains("<mark>残業</mark>", lines[0].InnerHtml);
        });

        cut.Find("[data-vocab-view='heard']").Click();
        Assert.NotEmpty(cut.FindAll("[data-word='無理']"));
    }

    [Fact]
    public void Feedback_IsSentAndTheAnswerReplacesTheCall()
    {
        IRenderedComponent<CallAnalysisPage> cut = OpenDone();

        cut.Find("[data-finding='f1'] [data-feedback='not-a-mistake']").Click();

        CallAnalysisDetail updated = Detail(Summary("done", "Yuki"));
        updated.Struggles[0].Findings[0].Feedback = CallFindingFeedback.NotAMistake;
        updated.Struggles[0].TimesInCall = 1;
        updated.Summary.IssueCount = 3;
        CallAnalysisRequestTransmission sent = Respond(_ => new CallAnalysisResponseTransmission { Detail = updated });

        Assert.Equal((CallAnalysisAction.Feedback, "done", "f1", CallFindingFeedback.NotAMistake), (sent.Action, sent.CallId, sent.FindingId, sent.Feedback));
        cut.WaitForAssertion(() => Assert.Contains("you said: not a mistake", cut.Find("[data-finding='f1']").TextContent));
        Assert.Equal("3", cut.Find("[data-stat='issues']").TextContent.Trim());

        // Clicking the active choice again takes it back
        cut.Find("[data-finding='f1'] [data-feedback='not-a-mistake']").Click();
        CallAnalysisRequestTransmission undone = Respond(_ => new CallAnalysisResponseTransmission { Detail = Detail(Summary("done", "Yuki")) });
        Assert.Equal(CallFindingFeedback.None, undone.Feedback);
    }

    [Fact]
    public void ARunningCall_TakesNoFeedback()
    {
        Services.AddSingleton<ISocketClientContainer>(new Sockets { Client = _client });
        IRenderedComponent<CallAnalysisPage> cut = Render<CallAnalysisPage>(p => p.Add(x => x.CallId, "run"));
        Respond(_ => new CallAnalysisResponseTransmission { Calls = [Summary("run", "Ken", CallAnalysisState.Consolidating)] });
        Respond(_ => new CallAnalysisResponseTransmission { Detail = Detail(Summary("run", "Ken", CallAnalysisState.Consolidating)) });

        cut.WaitForAssertion(() => Assert.Contains("Merging and checking the findings.", cut.Markup));
        Assert.Empty(cut.FindAll("[data-feedback]"));
    }

    [Fact]
    public void ACallInTheAddress_IsTheOneShown()
    {
        Services.AddSingleton<ISocketClientContainer>(new Sockets { Client = _client });
        IRenderedComponent<CallAnalysisPage> cut = Render<CallAnalysisPage>(p => p.Add(x => x.CallId, "run"));

        Respond(_ => new CallAnalysisResponseTransmission { Calls = [Summary("done", "Yuki"), Summary("run", "Ken", CallAnalysisState.Analyzing)] });
        CallAnalysisRequestTransmission asked = Respond(_ => new CallAnalysisResponseTransmission { Detail = Detail(Summary("run", "Ken", CallAnalysisState.Analyzing)) });

        Assert.Equal("run", asked.CallId);
        cut.WaitForAssertion(() => Assert.Contains("Reviewed 3 of 3 parts.", cut.Markup));
    }

    [Fact]
    public void Coaching_FiltersByScope_AndFactsLinkToThePerson()
    {
        IRenderedComponent<CallAnalysisPage> cut = OpenDone();

        Assert.Contains("You answer fully but rarely ask back.", cut.Find("[data-coaching]").TextContent);
        Assert.Contains("忙しいよ！ユキは？", cut.Find("[data-finding='m1']").TextContent);
        Assert.Contains("Yuki:", cut.Find("[data-finding='m1']").TextContent);
        Assert.Contains("Not helpful", cut.Find("[data-finding='m1']").TextContent);
        cut.Find("[data-scope='japanese']").Click();
        Assert.Empty(cut.FindAll("[data-finding='m1']"));
        Assert.NotEmpty(cut.FindAll("[data-finding='m2']"));

        Assert.Equal("Learn/People/Yuki", cut.Find("[data-person-link]").GetAttribute("href"));
        Assert.Contains("ask next time", cut.Find("[data-finding='pf1']").TextContent);
        Assert.Contains("Akagi remembers these", cut.Find("[data-facts]").TextContent);
        Assert.Empty(cut.FindAll("[data-finding='pf1'] [data-feedback='useful']"));
        Assert.Empty(cut.FindAll("[data-catchup]"));

        cut.Find("[data-finding='pf1'] [data-feedback='not-a-mistake']").Click();
        CallAnalysisRequestTransmission sent = Respond(_ => new CallAnalysisResponseTransmission { Detail = Detail(Summary("done", "Yuki")) });
        Assert.Equal(("pf1", CallFindingFeedback.NotAMistake), (sent.FindingId, sent.Feedback));
    }

    [Fact]
    public void AnOlderCall_OffersToCatchUp()
    {
        IRenderedComponent<CallAnalysisPage> cut = RenderOnline();
        CallAnalysisDetail older = Detail(Summary("done", "Yuki"));
        older.CoachingDone = false;
        older.FactsDone = false;
        Respond(_ => new CallAnalysisResponseTransmission { Calls = [Summary("done", "Yuki")] });
        Respond(_ => new CallAnalysisResponseTransmission { Detail = older });

        cut.WaitForAssertion(() => Assert.Contains("/calls catchup done", cut.Find("[data-catchup]").TextContent));
        Assert.Empty(cut.FindAll("[data-coaching]"));
        Assert.Empty(cut.FindAll("[data-facts]"));
    }
}
