using Akagi.Bridge.Chat.Models;
using Akagi.Bridge.Chat.Transmissions.Requests;
using Akagi.Bridge.Chat.Transmissions.Responses;
using Akagi.Web.Models;
using Akagi.Web.Pages.Learning;
using Akagi.Web.Pages.Learning.Context;
using Akagi.Web.Services.Sockets;
using Akagi.Web.Services.Sockets.Requests;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net;
using System.Text.Json;

namespace Akagi.Web.Tests;

/// <summary>
/// What the context builder writes, and the page that puts it together.
/// </summary>
public class ContextBuilderTests : BunitContext
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

    private const string Module = "./Pages/Learning/ContextBuilderPage.razor.js";
    private static readonly DateTime CallDay = new(2026, 10, 27, 0, 0, 0, DateTimeKind.Utc);

    private readonly SocketClient _client;
    private readonly CancellationTokenSource _stop = new();
    private readonly List<CallAnalysisRequestTransmission> _asked = [];

    public ContextBuilderTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        _client = new SocketClient(IPAddress.Loopback, 1, new User { GoogleId = "g", Name = "Tester", Email = "t" }, "circuit",
            NullLoggerFactory.Instance, [], null!);
        GC.SuppressFinalize(_client);
        Services.AddSingleton<ISocketClientContainer>(new Sockets { Client = _client });
    }

    protected override void Dispose(bool disposing)
    {
        _stop.Cancel();
        base.Dispose(disposing);
    }

    /// <summary>
    /// Answers every request the page makes, the way the bot would, until the test ends.
    /// </summary>
    private void AnswerLikeTheBot()
    {
        _ = Task.Run(async () =>
        {
            while (!_stop.IsCancellationRequested)
            {
                foreach (CallAnalysisRequest request in _client.GetRequests<CallAnalysisRequest>())
                {
                    lock (_asked)
                    {
                        _asked.Add(request.Transmission);
                    }
                    request.Fulfill(request.Transmission.Action switch
                    {
                        CallAnalysisAction.List => new CallAnalysisResponseTransmission { Calls = [Detail().Summary] },
                        CallAnalysisAction.People => new CallAnalysisResponseTransmission { People = [new PersonSummary { Name = "Ken", Calls = 1, Facts = 1 }] },
                        CallAnalysisAction.Detail => new CallAnalysisResponseTransmission { Detail = Detail() },
                        CallAnalysisAction.Insights => new CallAnalysisResponseTransmission { Insights = Insights() },
                        _ => new CallAnalysisResponseTransmission { Error = "unexpected" }
                    });
                }
                foreach (VocabularyRequest request in _client.GetRequests<VocabularyRequest>())
                {
                    request.Fulfill(new VocabularyResponseTransmission { Catalog = new VocabCatalog() });
                }
                await Task.Delay(5);
            }
        });
    }

    private static CallAnalysisDetail Detail() => new()
    {
        Summary = new CallAnalysisSummary { Id = "k1", Partner = "Ken", State = CallAnalysisState.Completed, RecordedAt = CallDay, DurationSeconds = 1800, IssueCount = 2 },
        Register = "casual",
        ReviewSummary = "A relaxed call; you kept it going well.",
        Priorities = ["Drop です with friends"],
        Struggles =
        [
            new CallStruggle
            {
                IssueId = "i1", Category = "Register", Pattern = "too polite with friends", Severity = 2, Status = "improving", TimesInCall = 2, TimesOverall = 7,
                Findings =
                [
                    new CallFindingView { Id = "f1", Kind = CallFindingKind.Mistake, Said = "楽しかったです", Better = "楽しかった", Explanation = "Friends drop です.", Time = 95 },
                    new CallFindingView { Id = "f2", Kind = CallFindingKind.Mistake, Said = "行きます", Better = "行く", Feedback = CallFindingFeedback.NotAMistake }
                ]
            }
        ],
        Conversation = [new CallFindingView { Kind = CallFindingKind.Conversation, Aspect = "follow-up", Japanese = true, Context = "週末どうだった？", Said = "よかった", Better = "よかったよ、ケンは？", Explanation = "Ask back." }],
        PartnerFacts = [new CallFindingView { Kind = CallFindingKind.Fact, Aspect = "plans", Pattern = "Ken moves to Osaka in spring.", FollowUp = true }],
        Transcript = [new CallTranscriptLine { Start = 3, Speaker = "Ken", Text = "もしもし" }, new CallTranscriptLine { Start = 5, Speaker = "Me", Text = "もしもし、元気？", IsLearner = true }]
    };

    private static CallInsights Insights() => new()
    {
        Calls = [new CallTrendPoint { CallId = "k1", Name = "Call with Ken", Date = CallDay, DurationSeconds = 1800, Separated = true, LearnerShare = 0.5, LearnerDistinct = 120, IssueCount = 2 }],
        Words =
        [
            new InsightWord { Lemma = "忙しい", Reading = "いそがしい", Class = "Adjective", Learner = 12, Calls = 1 },
            new InsightWord { Lemma = "仕事", Reading = "しごと", Class = "Noun", Learner = 8, Calls = 1 },
            new InsightWord { Lemma = "うん", Class = "Interjection", Learner = 80, Calls = 1 }
        ],
        Struggles =
        [
            new InsightStruggle
            {
                Category = "Register", Pattern = "too polite with friends", Times = 7, Calls = 2, Severity = 2, LastSeen = CallDay,
                Example = new CallFindingView { Said = "楽しかったです", Better = "楽しかった" },
                Occurrences = [new InsightOccurrence { CallId = "k1", CallName = "Call with Ken", Date = CallDay, Finding = new CallFindingView { Said = "行きます", Better = "行く", Time = 20 } }]
            }
        ]
    };

    [Fact]
    public void ACall_WritesWhatCountsAndLeavesTheTranscriptOutUntilAskedFor()
    {
        ContextBlock block = ContextBlock.Create(ContextKind.Call, "k1", "Call");

        string text = ContextWriter.Call(Detail(), block);

        Assert.Contains("Date: 2026-10-27", text);
        Assert.Contains("Politeness the situation called for: casual", text);
        Assert.Contains("1. Drop です with friends", text);
        Assert.Contains("[Register, noticeable] too polite with friends (2x in this call, 7x overall, status improving)", text);
        Assert.Contains("[01:35] said \"楽しかったです\" -> better \"楽しかった\". Friends drop です.", text);
        // The user said it was not a mistake
        Assert.DoesNotContain("行きます", text);
        Assert.Contains("Ken: 週末どうだった？", text);
        Assert.Contains("Try: よかったよ、ケンは？", text);
        Assert.Contains("Ken moves to Osaka in spring. - worth asking about next time", text);
        Assert.DoesNotContain("もしもし", text);

        block.Sections.Add("transcript");
        block.Sections.Remove("coaching");
        text = ContextWriter.Call(Detail(), block);

        Assert.Contains("[00:05] Me: もしもし、元気？", text);
        Assert.DoesNotContain("Conversation coaching", text);
    }

    [Fact]
    public void Insights_KeepToContentWordsAndTheLimit()
    {
        ContextBlock block = ContextBlock.Create(ContextKind.Insights, string.Empty, "Insights");
        block.Limit = 1;

        string text = ContextWriter.Insights(Insights(), block);

        Assert.Contains("| 2026-10-27 | Call with Ken | 30 min | 50% | 120 |", text);
        Assert.Contains("(top 1 of 2)", text);
        Assert.Contains("忙しい (いそがしい, i-adjective): I said it 12x", text);
        Assert.DoesNotContain("仕事", text);
        Assert.DoesNotContain("うん", text);
        Assert.Contains("Example: said \"楽しかったです\"", text);

        block.Sections.Add("occurrences");
        text = ContextWriter.Insights(Insights(), block);

        Assert.Contains("- 2026-10-27, Call with Ken: [00:20] said \"行きます\" -> better \"行く\"", text);
    }

    [Fact]
    public void Lessons_ShowEveryAnswerAndWhatWasRight()
    {
        LearningOverview overview = new()
        {
            Struggles = [new LearningStruggle { Kind = StruggleKind.Grammar, Title = "て-form", Detail = "mixes up ている and てある" }],
            Recent =
            [
                new LearningLesson
                {
                    Topic = "Casual speech", Mode = LessonMode.MultipleChoice, Status = LessonStatus.Finished, CreatedAt = CallDay, Score = 0,
                    Questions = [new LearningQuestion { Concept = "casual past", Prompt = "Pick the casual one", Options = ["楽しかったです", "楽しかった"], ChosenOption = 0, CorrectOption = 1, Verdict = LessonVerdict.Wrong, Feedback = "です makes it polite." }]
                }
            ]
        };

        string text = ContextWriter.Lessons(overview, ContextBlock.Create(ContextKind.Lessons, string.Empty, "Lessons"));

        Assert.Contains("- [Grammar] て-form: mixes up ている and てある", text);
        Assert.Contains("1. (casual past) Pick the casual one", text);
        Assert.Contains("My answer: a) 楽しかったです (wrong)", text);
        Assert.Contains("Right answer: b) 楽しかった", text);
        Assert.Contains("Tutor: です makes it polite.", text);
    }

    [Fact]
    public void AVocabularyTable_IsSortedAsTheReportChoseAndCut()
    {
        VocabReportResult result = new()
        {
            Stats = [new VocabStat { Label = "MATTR", Value = "0.71", Hint = "higher is more varied" }],
            Tables =
            [
                new VocabTable
                {
                    Title = "Your words", SortColumn = 1, Descending = true,
                    Columns = [new VocabColumn { Label = "Word" }, new VocabColumn { Label = "Count", Kind = VocabColumnKind.Number }],
                    Rows =
                    [
                        new VocabRow { Cells = [new VocabCell { Text = "仕事" }, new VocabCell { Text = "3", Sort = 3 }] },
                        new VocabRow { Cells = [new VocabCell { Text = "忙しい" }, new VocabCell { Text = "9", Sort = 9 }] },
                        new VocabRow { Cells = [new VocabCell { Text = "a|b" }, new VocabCell { Text = "1", Sort = 1 }] }
                    ]
                }
            ]
        };
        ContextBlock block = ContextBlock.Create(ContextKind.Vocabulary, "words", "Words");
        block.Limit = 2;

        string text = ContextWriter.Vocabulary(null, result, block);

        Assert.Contains("- MATTR: 0.71 (higher is more varied)", text);
        Assert.Contains("### Your words (top 2 of 3)", text);
        Assert.True(text.IndexOf("| 忙しい | 9 |", StringComparison.Ordinal) < text.IndexOf("| 仕事 | 3 |", StringComparison.Ordinal));
        Assert.DoesNotContain("a\\|b", text);
    }

    [Fact]
    public void Compose_PutsEveryBlockUnderItsHeading()
    {
        string text = ContextWriter.Compose([("About me", "I learn Japanese."), ("Call with Ken", "### Overview\nDate: 2026-10-27")], new DateTime(2026, 11, 5));

        Assert.Equal("# Context from Akagi, exported 2026-11-05\n\n## About me\n\nI learn Japanese.\n\n## Call with Ken\n\n### Overview\nDate: 2026-10-27\n", text);
    }

    [Fact]
    public void ThePage_StartsWithANote_AddsACall_AndDownloadsWhatIsIncluded()
    {
        BunitJSModuleInterop module = JSInterop.SetupModule(Module);
        module.Mode = JSRuntimeMode.Loose;
        AnswerLikeTheBot();
        IRenderedComponent<ContextBuilderPage> cut = Render<ContextBuilderPage>();

        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("[data-block-kind='Note']")));
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("[data-add='call:k1']")));

        cut.Find("[data-add='call:k1']").Click();

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("[data-block-kind='Call'] [data-block-size]")));
        Assert.Contains("Call with Ken on 2026-10-27", cut.Find("[data-block-kind='Call'] [data-block-title]").TextContent);
        Assert.NotEmpty(cut.FindAll("[data-added='call:k1']"));
        Assert.Equal("2", cut.Find("[data-summary='blocks']").TextContent);

        cut.Find("[data-block-kind='Call'] [data-section='transcript']").Click();
        cut.Find("[data-context-download]").Click();

        string file = (string)module.VerifyInvoke("download").Arguments[1]!;
        Assert.Contains("## About me and this file", file);
        Assert.Contains("## Call with Ken on 2026-10-27", file);
        Assert.Contains("[00:05] Me: もしもし、元気？", file);
        Assert.True(file.IndexOf("About me", StringComparison.Ordinal) < file.IndexOf("Call with Ken", StringComparison.Ordinal));

        // Moved to the top and the note left out
        cut.Find("[data-block-kind='Call'] [data-block-up]").Click();
        cut.Find("[data-block-kind='Note'] [data-block-include]").Change(false);
        cut.Find("[data-context-download]").Click();

        file = (string)module.VerifyInvoke("download", 2)[1].Arguments[1]!;
        Assert.DoesNotContain("About me", file);
        Assert.Contains("## Call with Ken", file);
        Assert.Equal("1", cut.Find("[data-summary='blocks']").TextContent);

        // What is in the context is kept for the next visit, without the text
        string stored = (string)module.Invocations["save"].Last().Arguments[1]!;
        List<ContextBlock> blocks = JsonSerializer.Deserialize<List<ContextBlock>>(stored)!;
        Assert.Equal([ContextKind.Call, ContextKind.Note], blocks.Select(b => b.Kind));
        Assert.False(blocks[1].Included);
        Assert.Contains("transcript", blocks[0].Sections);
    }

    [Fact]
    public void ThePage_RestoresTheLastContextAndAddsFromALink()
    {
        BunitJSModuleInterop module = JSInterop.SetupModule(Module);
        module.Mode = JSRuntimeMode.Loose;
        List<ContextBlock> stored = [ContextBlock.Create(ContextKind.Insights, string.Empty, "Insights")];
        module.Setup<string?>("load", "akagi.context.v1").SetResult(JsonSerializer.Serialize(stored));
        AnswerLikeTheBot();
        Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>().NavigateTo("Learn/Context?add=call:k1");

        IRenderedComponent<ContextBuilderPage> cut = Render<ContextBuilderPage>();

        cut.WaitForAssertion(() => Assert.Equal(["Insights", "Call"], cut.FindAll("[data-block]").Select(b => b.GetAttribute("data-block-kind")!)));
        Assert.Empty(cut.FindAll("[data-block-kind='Note']"));
        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll("[data-block-size]").Count));
        lock (_asked)
        {
            Assert.Contains(_asked, a => a.Action == CallAnalysisAction.Detail && a.CallId == "k1");
            Assert.Contains(_asked, a => a.Action == CallAnalysisAction.Insights);
        }
    }
}
