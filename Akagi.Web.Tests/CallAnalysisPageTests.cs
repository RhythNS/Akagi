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

    private static CallAnalysisDetail Detail(CallAnalysisSummary summary) => new()
    {
        Summary = summary,
        Register = "casual",
        Report = "Call analysis: the whole report.",
        Struggles =
        [
            new CallStruggle
            {
                Id = "a1", Category = "Unnatural phrasing", Pattern = "すごく忙しい", Explanation = "Sounds stiff.", Severity = 3,
                Status = "active", TimesInCall = 2, TimesOverall = 5,
                Examples = [new CallStruggleExample { Said = "すごく忙しいです", Better = "めっちゃ忙しい" }]
            },
            new CallStruggle { Id = "b2", Category = "Particle", Pattern = "に vs で", Severity = 2, Status = "improving", TimesInCall = 1, TimesOverall = 3 }
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
        Strengths = ["Natural back-channeling"],
        Transcript =
        [
            new CallTranscriptLine { Start = 0, Speaker = "Yuki", Text = "最近どう？" },
            new CallTranscriptLine { Start = 65, Speaker = "Me", Text = "えーと、忙しいです。", IsLearner = true, Disputed = "忙しい -> 忙しく" }
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
        Assert.Contains("Call analysis: the whole report.", cut.Markup);
        Assert.Equal("2", cut.Find("[data-stat='struggles']").TextContent.Trim());

        cut.Find("[data-category='Particle']").Click();
        Assert.Empty(cut.FindAll("[data-struggle='a1']"));
        Assert.NotEmpty(cut.FindAll("[data-struggle='b2']"));

        Assert.Empty(cut.FindAll("[data-transcript]"));
        cut.Find("[data-transcript-toggle]").Click();
        Assert.Contains("01:05", cut.Find("[data-transcript]").TextContent);
        Assert.Contains("忙しい -> 忙しく", cut.Find("[data-transcript]").TextContent);
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
}
