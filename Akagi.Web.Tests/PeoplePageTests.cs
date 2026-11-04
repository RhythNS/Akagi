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
/// The people page against a client whose requests the test answers itself.
/// </summary>
public class PeoplePageTests : BunitContext
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

    public PeoplePageTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        _client = new SocketClient(IPAddress.Loopback, 1, new User { GoogleId = "g", Name = "Tester", Email = "t" }, "circuit",
            NullLoggerFactory.Instance, [], null!);
        GC.SuppressFinalize(_client);
        Services.AddSingleton<ISocketClientContainer>(new Sockets { Client = _client });
    }

    private CallAnalysisRequestTransmission Respond(Func<CallAnalysisRequestTransmission, CallAnalysisResponseTransmission> answer)
    {
        CallAnalysisRequest? request = null;
        for (int i = 0; i < 200 && request == null; i++)
        {
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

    private static readonly DateTime Earlier = DateTime.UtcNow.AddDays(-21);

    private static PersonSummary Rii() => new() { Name = "Rii", Calls = 2, Facts = 3, OpenThreads = 1, LastTalked = Earlier, FirstTalked = Earlier.AddDays(-30), TotalSeconds = 5400 };

    private static PersonDetail RiiDetail(bool withExam = true) => new()
    {
        Summary = Rii(),
        Facts =
        [
            .. withExam ? new[] { new PersonFactView { Id = "f1", CallId = "c2", CallName = "Call with Rii", Date = Earlier, Topic = "plans", Text = "Rii has a driving test in December.", Quote = "12月に免許の試験", FollowUp = true, Time = 300 } } : [],
            new PersonFactView { Id = "f2", CallId = "c2", CallName = "Call with Rii", Date = Earlier, Topic = "games", Text = "Rii plays Animal Crossing every evening.", Quote = "毎晩あつ森" },
            new PersonFactView { Id = "f3", CallId = "c1", CallName = "Call with Rii", Date = Earlier.AddDays(-30), Topic = "family", Text = "Rii's sister Mika is a nurse.", Quote = "妹は看護師" }
        ],
        Calls = [new PersonCallView { CallId = "c2", Date = Earlier, DurationSeconds = 3600, Facts = 2, LearnerShare = 0.6 }, new PersonCallView { CallId = "c1", Date = Earlier.AddDays(-30), DurationSeconds = 1800, Facts = 1 }],
        Phrases = [new InsightPhrase { Phrase = "無理しないでね", Meaning = "take it easy", CallId = "c2" }]
    };

    [Fact]
    public void ShowsWhatToAskAndEverythingKnown()
    {
        IRenderedComponent<PeoplePage> cut = Render<PeoplePage>();

        Assert.Equal(CallAnalysisAction.People, Respond(_ => new CallAnalysisResponseTransmission { People = [Rii(), new PersonSummary { Name = "Ken" }] }).Action);
        CallAnalysisRequestTransmission asked = Respond(_ => new CallAnalysisResponseTransmission { Person = RiiDetail() });

        Assert.Equal((CallAnalysisAction.Person, "Rii"), (asked.Action, asked.Name));
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("[data-person-detail='Rii']")));
        cut.Settled();
        Assert.Contains("talked 3 weeks ago", cut.Find("[data-person='Rii']").TextContent);
        Assert.Contains("1 to ask", cut.Find("[data-person='Rii']").TextContent);
        Assert.Contains("driving test in December", cut.Find("[data-open-threads]").TextContent);
        Assert.Contains("Mika is a nurse", cut.Find("[data-person-facts]").TextContent);
        Assert.Contains("無理しないでね", cut.Markup);

        cut.Find("[data-topic='games']").Click();
        Assert.Single(cut.FindAll("[data-person-facts] [data-fact]"));
        Assert.Contains("Animal Crossing", cut.Find("[data-person-facts]").TextContent);
    }

    [Fact]
    public void AWrongFact_IsTakenBackFromItsCall_AndThePersonReloads()
    {
        IRenderedComponent<PeoplePage> cut = Render<PeoplePage>(p => p.Add(x => x.Name, "Rii"));
        Respond(_ => new CallAnalysisResponseTransmission { People = [Rii()] });
        Respond(_ => new CallAnalysisResponseTransmission { Person = RiiDetail() });
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("[data-fact-wrong='f1']")));
        cut.Settled();

        cut.Find("[data-fact-wrong='f1']").Click();

        CallAnalysisRequestTransmission sent = Respond(_ => new CallAnalysisResponseTransmission { Detail = new CallAnalysisDetail() });
        Assert.Equal((CallAnalysisAction.Feedback, "c2", "f1", CallFindingFeedback.NotAMistake), (sent.Action, sent.CallId, sent.FindingId, sent.Feedback));
        Respond(_ => new CallAnalysisResponseTransmission { People = [Rii()] });
        Respond(_ => new CallAnalysisResponseTransmission { Person = RiiDetail(withExam: false) });
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("[data-fact='f1']")));
    }

    [Fact]
    public void WithoutPeople_ExplainsHowTheyAppear()
    {
        IRenderedComponent<PeoplePage> cut = Render<PeoplePage>();
        Respond(_ => new CallAnalysisResponseTransmission());

        cut.WaitForAssertion(() => Assert.Contains("Nobody here yet", cut.Markup));
    }
}
