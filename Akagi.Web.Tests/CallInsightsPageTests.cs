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
/// The speaking insights page against a client whose requests the test answers itself.
/// </summary>
public class CallInsightsPageTests : BunitContext
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

    public CallInsightsPageTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        _client = new SocketClient(IPAddress.Loopback, 1, new User { GoogleId = "g", Name = "Tester", Email = "t" }, "circuit",
            NullLoggerFactory.Instance, [], null!);
        GC.SuppressFinalize(_client);
    }

    private CallAnalysisRequestTransmission Respond(CallAnalysisResponseTransmission response)
    {
        CallAnalysisRequest? request = null;
        for (int i = 0; i < 200 && request == null; i++)
        {
            request = _client.GetRequests<CallAnalysisRequest>().FirstOrDefault();
            if (request == null)
            {
                Thread.Sleep(10);
            }
        }
        Assert.NotNull(request);
        request.Fulfill(response);
        return request.Transmission;
    }

    private static CallInsights Insights() => new()
    {
        Calls =
        [
            new CallTrendPoint { CallId = "a", Name = "Call with Yuki", Date = new DateTime(2026, 10, 20, 0, 0, 0, DateTimeKind.Utc), DurationSeconds = 600, Separated = true, LearnerShare = 0.4, LearnerDistinct = 120, NewWords = 120, FillersPer100 = 3.1, MedianResponseSeconds = 1.2, IssueCount = 9 },
            new CallTrendPoint { CallId = "b", Name = "Call with Ken", Date = new DateTime(2026, 10, 27, 0, 0, 0, DateTimeKind.Utc), DurationSeconds = 1800, Separated = true, LearnerShare = 0.6, LearnerDistinct = 150, NewWords = 40, FillersPer100 = 2.4, IssueCount = 5 },
            new CallTrendPoint { CallId = "c", Name = "Call", Date = new DateTime(2026, 10, 28, 0, 0, 0, DateTimeKind.Utc), DurationSeconds = 300 }
        ],
        LearnerWords = 900,
        LearnerDistinct = 160,
        MixedCalls = 1,
        Words =
        [
            new InsightWord { Lemma = "忙しい", Reading = "いそがしい", Class = "Adjective", Learner = 12, Partner = 3, Calls = 2, FirstUsed = new DateTime(2026, 10, 20, 0, 0, 0, DateTimeKind.Utc) },
            new InsightWord { Lemma = "仕事", Reading = "しごと", Class = "Noun", Learner = 8, Calls = 2 }
        ],
        HeardNotSaid = [new InsightWord { Lemma = "無理", Reading = "むり", Class = "AdjectivalNoun", Partner = 4 }],
        Struggles = [new InsightStruggle { IssueId = "i1", Category = "Register", Pattern = "too polite with friends", Times = 7, Calls = 2, Severity = 2, LastSeen = new DateTime(2026, 10, 27, 0, 0, 0, DateTimeKind.Utc), CorrectUses = 2,
            Example = new CallFindingView { Said = "楽しかったです", Better = "楽しかった" },
            Occurrences =
            [
                new InsightOccurrence { CallId = "b", CallName = "Call with Ken", Date = new DateTime(2026, 10, 27, 0, 0, 0, DateTimeKind.Utc),
                    Finding = new CallFindingView { Id = "f2", Said = "楽しかったです", Better = "楽しかった", Explanation = "Friends drop です.", Time = 95 } },
                new InsightOccurrence { CallId = "a", CallName = "Call with Yuki", Date = new DateTime(2026, 10, 20, 0, 0, 0, DateTimeKind.Utc),
                    Finding = new CallFindingView { Id = "f1", Said = "行きます", Better = "行く" } }
            ] }],
        Phrases = [new InsightPhrase { Phrase = "無理しないでね", Meaning = "take it easy", CallId = "a", CallName = "Call with Yuki", Date = new DateTime(2026, 10, 20, 0, 0, 0, DateTimeKind.Utc) }]
    };

    [Fact]
    public void ShowsTrendsVocabularyStrugglesAndPhrases()
    {
        Services.AddSingleton<ISocketClientContainer>(new Sockets { Client = _client });
        IRenderedComponent<CallInsightsPage> cut = Render<CallInsightsPage>();

        CallAnalysisRequestTransmission asked = Respond(new CallAnalysisResponseTransmission { Insights = Insights() });

        Assert.Equal(CallAnalysisAction.Insights, asked.Action);
        cut.WaitForAssertion(() => Assert.Equal("3", cut.Find("[data-tile='calls']").TextContent.Trim()));
        // Weighted by length: (0.4 * 600 + 0.6 * 1800) / 2400
        Assert.Equal("55%", cut.Find("[data-tile='share']").TextContent.Trim());
        Assert.Contains("recorded without separate voices", cut.Markup);
        Assert.Equal(3, cut.FindAll("[data-trend='Your share of the speaking time'] [data-point]").Count);
        Assert.Contains("too polite with friends", cut.Find("[data-insight-struggles]").TextContent);
        Assert.Contains("right 2×", cut.Find("[data-insight-struggles]").TextContent);
        Assert.Contains("無理しないでね", cut.Find("[data-phrase-book]").TextContent);
        Assert.NotEmpty(cut.FindAll("[data-insight-word='忙しい']"));
        Assert.Empty(cut.FindAll("[data-insight-word='無理']"));

        cut.Find("[data-insight-view='heard']").Click();

        Assert.NotEmpty(cut.FindAll("[data-insight-word='無理']"));
        Assert.Empty(cut.FindAll("[data-insight-word='忙しい']"));
    }

    [Fact]
    public void AColumn_TellsItsNumbersAndOpensItsCall()
    {
        Services.AddSingleton<ISocketClientContainer>(new Sockets { Client = _client });
        IRenderedComponent<CallInsightsPage> cut = Render<CallInsightsPage>();
        Respond(new CallAnalysisResponseTransmission { Insights = Insights() });
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("[data-trend]")));
        cut.Settled();

        cut.Find("[data-trend='Fillers per 100 characters'] [data-point='b']").MouseEnter();
        Assert.Contains("Call with Ken", cut.Find("[data-trend='Fillers per 100 characters']").TextContent);
        Assert.Contains("2.4", cut.Find("[data-trend='Fillers per 100 characters']").TextContent);

        cut.Find("[data-trend='Fillers per 100 characters'] [data-point='b']").Click();
        Assert.EndsWith("/Learn/Calls/b", Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>().Uri);
    }

    [Fact]
    public void AStruggle_OpensToEveryTimeAndItsCorrection()
    {
        Services.AddSingleton<ISocketClientContainer>(new Sockets { Client = _client });
        IRenderedComponent<CallInsightsPage> cut = Render<CallInsightsPage>();
        Respond(new CallAnalysisResponseTransmission { Insights = Insights() });
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("[data-struggle-toggle]")));
        Assert.Empty(cut.FindAll("[data-occurrence]"));

        cut.Find("[data-struggle-toggle='i1']").Click();

        Assert.Equal(["f2", "f1"], cut.FindAll("[data-occurrence]").Select(e => e.GetAttribute("data-occurrence")));
        Assert.Equal(["b", "a"], cut.FindAll("[data-occurrence-call]").Select(e => e.GetAttribute("data-occurrence-call")));
        string newest = cut.Find("[data-occurrence='f2']").TextContent;
        Assert.Contains("楽しかった", newest);
        Assert.Contains("Friends drop です.", newest);
        Assert.Equal("Learn/Calls/b?at=95", cut.Find("[data-occurrence='f2'] a").GetAttribute("href"));
        Assert.Contains("行く", cut.Find("[data-occurrence='f1']").TextContent);
        Assert.Equal("true", cut.Find("[data-struggle-toggle='i1']").GetAttribute("aria-expanded"));

        cut.Find("[data-struggle-toggle='i1']").Click();

        Assert.Empty(cut.FindAll("[data-occurrence]"));
    }

    [Fact]
    public void WithoutAConnection_SaysSo()
    {
        Services.AddSingleton<ISocketClientContainer>(new Sockets());

        IRenderedComponent<CallInsightsPage> cut = Render<CallInsightsPage>();

        cut.WaitForAssertion(() => Assert.Contains("Socket Service Offline", cut.Markup));
    }

}
