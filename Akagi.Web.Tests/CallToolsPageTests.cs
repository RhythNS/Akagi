using Akagi.Bridge.Chat.Models;
using Akagi.Bridge.Chat.Transmissions.Requests;
using Akagi.Bridge.Chat.Transmissions.Responses;
using Akagi.Web.Models;
using Akagi.Web.Pages.Learning;
using Akagi.Web.Pages.Learning.Components;
using Akagi.Web.Services.Sockets;
using Akagi.Web.Services.Sockets.Requests;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net;

namespace Akagi.Web.Tests;

/// <summary>
/// The timeframe, focus struggles and advice on the insights page, deleting and mining on the call page, the mining
/// page and the expression search, against a client whose requests the test answers itself.
/// </summary>
public class CallToolsPageTests : BunitContext
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
    private readonly HashSet<IRequest> _answered = [];

    public CallToolsPageTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        _client = new SocketClient(IPAddress.Loopback, 1, new User { GoogleId = "g", Name = "Tester", Email = "t" }, "circuit",
            NullLoggerFactory.Instance, [], null!);
        GC.SuppressFinalize(_client);
        Services.AddSingleton<ISocketClientContainer>(new Sockets { Client = _client });
    }

    private TTransmission Respond<TRequest, TResponse, TTransmission>(Func<TRequest, TTransmission> transmission, Func<TTransmission, TResponse> answer)
        where TRequest : Request<TResponse, TTransmission>
        where TTransmission : Akagi.Bridge.Chat.Transmissions.Transmission
    {
        TRequest? request = null;
        for (int i = 0; i < 300 && request == null; i++)
        {
            request = _client.GetRequests<TRequest>().FirstOrDefault(r => !_answered.Contains(r));
            if (request == null)
            {
                Thread.Sleep(10);
            }
        }
        Assert.NotNull(request);
        _answered.Add(request);
        TTransmission sent = transmission(request);
        request.Fulfill(answer(sent));
        return sent;
    }

    private CallAnalysisRequestTransmission RespondCall(Func<CallAnalysisRequestTransmission, CallAnalysisResponseTransmission> answer) =>
        Respond<CallAnalysisRequest, CallAnalysisResponseTransmission, CallAnalysisRequestTransmission>(r => r.Transmission, answer);

    private MiningRequestTransmission RespondMining(Func<MiningRequestTransmission, MiningResponseTransmission> answer) =>
        Respond<MiningRequest, MiningResponseTransmission, MiningRequestTransmission>(r => r.Transmission, answer);

    private static readonly DateTime October = new(2026, 10, 20, 0, 0, 0, DateTimeKind.Utc);

    private static CallInsights Insights(bool focused = false) => new()
    {
        Calls = [new CallTrendPoint { CallId = "a", Name = "Call with Yuki", Date = October, DurationSeconds = 600, Separated = true, LearnerShare = 0.4 }],
        Struggles =
        [
            new InsightStruggle { IssueId = "i1", Category = "Unnatural phrasing", Pattern = "それも as a starter", Times = 6, Calls = 2, Severity = 2, LastSeen = October, Focus = focused,
                Occurrences = [new InsightOccurrence { CallId = "a", CallName = "Call with Yuki", Date = October, Finding = new CallFindingView { Id = "f1", Said = "それも" } }] }
        ],
        FocusStruggles = focused
            ? [new InsightStruggle { IssueId = "i1", Category = "Unnatural phrasing", Pattern = "それも as a starter", Focus = true, FocusSince = October, RateBefore = 3.0, RateSince = 1.0, Times = 1,
                Trend = [new StruggleTrendPoint { CallId = "a", Name = "Call with Yuki", Date = October, Times = 1, Rate = 1.0 }] }]
            : [],
        Advice =
        [
            new InsightAdvice { Kind = "focus", Title = "Focus on: それも as a starter", Detail = "6× in your last 2 calls.", IssueId = "i1" },
            new InsightAdvice { Kind = "use-less", Title = "Use less: まあ", Detail = "You said it 199×.", Word = "まあ" }
        ]
    };

    // Insights

    [Fact]
    public async Task Insights_TimeframeAsksForTheLastDays()
    {
        IRenderedComponent<CallInsightsPage> cut = Render<CallInsightsPage>();
        CallAnalysisRequestTransmission first = RespondCall(_ => new CallAnalysisResponseTransmission { Insights = Insights() });
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("[data-range='30']")));
        cut.Settled();

        Task click = cut.Find("[data-range='30']").ClickAsync(new());
        CallAnalysisRequestTransmission asked = RespondCall(_ => new CallAnalysisResponseTransmission { Insights = Insights() });
        await click;

        Assert.Null(first.From);
        Assert.Equal(CallAnalysisAction.Insights, asked.Action);
        Assert.Equal(DateTime.Today.AddDays(-29), asked.From!.Value.ToLocalTime());
        Assert.Null(asked.To);
    }

    [Fact]
    public async Task Insights_FocusButtonSendsTheStruggleAndShowsItsTrend()
    {
        IRenderedComponent<CallInsightsPage> cut = Render<CallInsightsPage>();
        RespondCall(_ => new CallAnalysisResponseTransmission { Insights = Insights() });
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("[data-focus-toggle='i1']")));
        cut.Settled();
        Assert.Empty(cut.FindAll("[data-focus-card]"));

        Task click = cut.Find("[data-focus-toggle='i1']").ClickAsync(new());
        CallAnalysisRequestTransmission asked = RespondCall(_ => new CallAnalysisResponseTransmission { Insights = Insights(focused: true) });
        await click;

        Assert.Equal(CallAnalysisAction.SetFocus, asked.Action);
        Assert.Equal("i1", asked.IssueId);
        Assert.True(asked.Focus);
        cut.WaitForAssertion(() => Assert.Contains("Going down", cut.Find("[data-focus='i1'] [data-focus-change]").TextContent));
        Assert.NotEmpty(cut.FindAll("[data-focus-badge]"));
    }

    [Fact]
    public async Task Insights_AdviceOffersFocusWordLinksAndAWrittenPlan()
    {
        IRenderedComponent<CallInsightsPage> cut = Render<CallInsightsPage>();
        RespondCall(_ => new CallAnalysisResponseTransmission { Insights = Insights() });
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("[data-advice-kind]")));
        cut.Settled();

        Assert.Contains("Learn/Vocabulary?expression=", cut.Find("[data-advice-word='まあ']").GetAttribute("href"));
        Assert.NotEmpty(cut.FindAll("[data-advice-focus='i1']"));

        Task click = cut.Find("[data-write-advice]").ClickAsync(new());
        CallAnalysisRequestTransmission asked = RespondCall(_ =>
        {
            CallInsights written = Insights();
            written.WrittenAdvice = "1. Drop それも at the start.";
            written.WrittenAdviceAt = DateTime.UtcNow;
            return new CallAnalysisResponseTransmission { Insights = written };
        });
        await click;

        Assert.Equal(CallAnalysisAction.Advice, asked.Action);
        cut.WaitForAssertion(() => Assert.Contains("Drop それも", cut.Find("[data-written-advice-text]").TextContent));
    }

    // Call page

    private static CallAnalysisSummary Summary(string id, string partner) => new()
    {
        Id = id,
        Partner = partner,
        State = CallAnalysisState.Completed,
        CreatedAt = October,
        DurationSeconds = 600
    };

    private static CallAnalysisDetail Detail(string id, string partner) => new()
    {
        Summary = Summary(id, partner),
        CanGiveFeedback = true,
        HasAudio = true,
        Transcript =
        [
            new CallTranscriptLine { Start = 0, Speaker = "Me", Text = "えーと、それもいいね。", IsLearner = true },
            new CallTranscriptLine { Start = 5, Speaker = partner, Text = "やっぱり休んだ方がいいよ。" }
        ],
        Metrics =
        [
            new CallMetric { Key = "questions", Label = "Questions you asked", Value = 2, Average = 6, AverageOver = 3, Format = "whole", HigherIsBetter = true },
            new CallMetric { Key = "fillers", Label = "Fillers per 100 characters", Value = 2.0, Average = 4.0, AverageOver = 3, Format = "decimal", HigherIsBetter = false }
        ]
    };

    private IRenderedComponent<CallAnalysisPage> RenderCall()
    {
        IRenderedComponent<CallAnalysisPage> cut = Render<CallAnalysisPage>(p => p.Add(c => c.CallId, "a"));
        RespondCall(_ => new CallAnalysisResponseTransmission { Calls = [Summary("a", "Yuki"), Summary("b", "Ken")] });
        RespondMining(_ => new MiningResponseTransmission());
        RespondCall(_ => new CallAnalysisResponseTransmission { Detail = Detail("a", "Yuki") });
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("[data-call-metrics]")));
        cut.Settled();
        return cut;
    }

    [Fact]
    public void CallPage_ComparesTheCallWithTheOthers()
    {
        IRenderedComponent<CallAnalysisPage> cut = RenderCall();

        Assert.Contains("▼ 67%", cut.Find("[data-metric='questions'] [data-metric-change]").TextContent);
        Assert.Contains("text-red", cut.Find("[data-metric='questions'] [data-metric-change]").ClassName);
        Assert.Contains("text-green", cut.Find("[data-metric='fillers'] [data-metric-change]").ClassName);
        Assert.Contains("average 6 over 3 calls", cut.Find("[data-metric='questions']").TextContent);
    }

    [Fact]
    public async Task CallPage_DeleteAsksFirstThenDeletesAndMovesOn()
    {
        IRenderedComponent<CallAnalysisPage> cut = RenderCall();

        await cut.Find("[data-delete-call]").ClickAsync(new());
        Assert.Contains("cannot be undone", cut.Find("[data-delete-confirm]").TextContent);

        Task click = cut.Find("[data-delete-yes]").ClickAsync(new());
        CallAnalysisRequestTransmission asked = RespondCall(_ => new CallAnalysisResponseTransmission { Calls = [Summary("b", "Ken")], Message = "Call with Yuki was deleted." });
        CallAnalysisRequestTransmission next = RespondCall(_ => new CallAnalysisResponseTransmission { Detail = Detail("b", "Ken") });
        await click;

        Assert.Equal("b", next.CallId);
        Assert.EndsWith("/Learn/Calls/b", Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>().Uri);

        Assert.Equal(CallAnalysisAction.Delete, asked.Action);
        Assert.Equal("a", asked.CallId);
        cut.WaitForAssertion(() => Assert.Contains("was deleted", cut.Find("[data-call-message]").TextContent));
        Assert.Single(cut.FindAll("[data-call]"));
    }

    [Fact]
    public async Task CallPage_MinesATranscriptLine()
    {
        IRenderedComponent<CallAnalysisPage> cut = RenderCall();
        await cut.Find("[data-transcript-toggle]").ClickAsync(new());

        Task click = cut.Find("[data-mine-line='1']").ClickAsync(new());
        MiningRequestTransmission asked = RespondMining(r => new MiningResponseTransmission
        {
            Sentences = [new MinedSentenceView { Id = "m1", CallId = "a", Start = r.Time, Text = r.Text }]
        });
        await click;

        Assert.Equal(MiningAction.Add, asked.Action);
        Assert.Equal(5, asked.Time);
        Assert.Equal("やっぱり休んだ方がいいよ。", asked.Text);
        cut.WaitForAssertion(() => Assert.Contains("Mined", cut.Find("[data-mine-line='1']").TextContent));
        Assert.Contains("Mine", cut.Find("[data-mine-line='0']").TextContent);
    }

    [Fact]
    public async Task CallPage_SubtitlesAskForTheChosenVoices()
    {
        IRenderedComponent<CallAnalysisPage> cut = RenderCall();
        await cut.Find("[data-subtitles-open]").ClickAsync(new());

        Task click = cut.Find("[data-subtitles='partner']").ClickAsync(new());
        MiningRequestTransmission asked = RespondMining(_ => new MiningResponseTransmission { FileName = "yuki.zip", Data = [1, 2] });
        await click;

        Assert.Equal(MiningAction.ExportCall, asked.Action);
        Assert.Equal("a", asked.CallId);
        Assert.Equal(MiningVoiceChoice.Partner, asked.Voices);
        Assert.Contains(JSInterop.Invocations, i => i.Identifier == "downloadStream");
    }

    // Mining page

    [Fact]
    public async Task MiningPage_ListsSentencesAndExportsTheSelectedOnes()
    {
        IRenderedComponent<MiningPage> cut = Render<MiningPage>();
        RespondMining(_ => new MiningResponseTransmission
        {
            Sentences =
            [
                new MinedSentenceView { Id = "m1", CallId = "a", CallName = "Call with Yuki", Date = October, Start = 65, Speaker = "Yuki", Text = "やっぱり休んだ方がいいよ。", Target = "やっぱり" },
                new MinedSentenceView { Id = "m2", CallId = "a", CallName = "Call with Yuki", Date = October, Start = 70, Speaker = "Yuki", Text = "無理しないで。" }
            ]
        });
        RespondCall(_ => new CallAnalysisResponseTransmission { Calls = [Summary("a", "Yuki")] });
        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll("[data-sentence]").Count));
        cut.Settled();
        Assert.Contains("<mark>やっぱり</mark>", cut.Find("[data-sentence='m1']").InnerHtml);

        await cut.Find("[data-sentence='m2'] input[type=checkbox]").ChangeAsync(new() { Value = true });
        Task click = cut.Find("[data-export-clips]").ClickAsync(new());
        MiningRequestTransmission asked = RespondMining(_ => new MiningResponseTransmission { FileName = "akagi-mining.zip", Data = [1] });
        await click;

        Assert.Equal(MiningAction.ExportClips, asked.Action);
        Assert.Equal(["m2"], asked.Ids);
        Assert.Contains(JSInterop.Invocations, i => i.Identifier == "downloadStream");
    }

    // Expression search

    [Fact]
    public void ExpressionVerdict_ComparesYouWithPartners()
    {
        Assert.Equal("You say it 3 times as often as your partners.",
            VocabExpressionPanel.Verdict(new VocabExpressionResult { Learner = 30, Partner = 10, LearnerPerHour = 30, PartnerPerHour = 10 }));
        Assert.Equal("Your partners use it and you never do: a good one to make your own.",
            VocabExpressionPanel.Verdict(new VocabExpressionResult { Learner = 0, Partner = 10, LearnerPerHour = 0, PartnerPerHour = 10 }));
        Assert.Null(VocabExpressionPanel.Verdict(new VocabExpressionResult { Learner = 1, Partner = 1, LearnerPerHour = 1, PartnerPerHour = 1 }));
    }

    [Fact]
    public void ExpressionPanel_ShowsCountsPerCallAndMineableLines()
    {
        VocabExpressionResult result = new()
        {
            Query = "それも",
            Learner = 12,
            Partner = 2,
            LearnerPerHour = 24,
            PartnerPerHour = 3,
            Calls = [new VocabExpressionCall { CallId = "a", Name = "Call with Yuki", Date = October, Learner = 12, Partner = 2, HasPartner = true }],
            Lines = [new VocabLine { CallId = "a", CallName = "Call with Yuki", Date = October, Time = 65, Speaker = "Yuki", Text = "それもそうだね。", Marks = ["それも"] }],
            TotalLines = 1
        };
        VocabLine? mined = null;

        IRenderedComponent<VocabExpressionPanel> cut = Render<VocabExpressionPanel>(p => p
            .Add(c => c.Result, result)
            .Add(c => c.OnMine, line => mined = line));

        Assert.Equal("12", cut.Find("[data-expression-you]").TextContent.Trim());
        Assert.Contains("8 times as often", cut.Find("[data-expression-verdict]").TextContent);
        Assert.NotEmpty(cut.FindAll("[data-expression-call='a']"));
        Assert.Contains("<mark>それも</mark>", cut.Find("[data-lines]").InnerHtml);
        Assert.Contains("Learn/Calls/a?at=65", cut.Find("[data-lines] a").GetAttribute("href"));

        cut.Find("[data-mine='a']").Click();
        Assert.Equal(65, mined!.Time);
    }
}
