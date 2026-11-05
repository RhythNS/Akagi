using Akagi.Bridge.Chat.Models;
using Akagi.Bridge.Chat.Transmissions.Requests;
using Akagi.Bridge.Chat.Transmissions.Responses;
using Akagi.Web.Models;
using Akagi.Web.Pages.Learning;
using Akagi.Web.Pages.Learning.Context;
using Akagi.Web.Services.Sockets;
using Akagi.Web.Services.Sockets.Requests;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
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
        Transcript =
        [
            new CallTranscriptLine { Start = 3, Speaker = "Ken", Text = "もしもし" },
            new CallTranscriptLine { Start = 5, Speaker = "Me", Text = "もしもし、元気？", IsLearner = true },
            new CallTranscriptLine { Start = 90, Speaker = "Ken", Text = "週末どうだった？" },
            new CallTranscriptLine { Start = 95, Speaker = "Me", Text = "うん、楽しかったです", IsLearner = true },
            new CallTranscriptLine { Start = 100, Speaker = "Ken", Text = "よかったね" }
        ]
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
                // The bot takes the example from the occurrences
                Example = new CallFindingView { Id = "o2", Said = "楽しかったです", Better = "楽しかった", Time = 95 },
                Occurrences =
                [
                    new InsightOccurrence { CallId = "k1", CallName = "Call with Ken", Date = CallDay, Finding = new CallFindingView { Id = "o1", Said = "行きます", Better = "行く", Time = 5 } },
                    new InsightOccurrence { CallId = "k1", CallName = "Call with Ken", Date = CallDay, Finding = new CallFindingView { Id = "o2", Said = "楽しかったです", Better = "楽しかった", Time = 95 } }
                ]
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
        Assert.Contains("Example: [01:35] said \"楽しかったです\"", text);
        Assert.DoesNotContain("行きます", text);

        block.Sections.Add("occurrences");
        text = ContextWriter.Insights(Insights(), block);

        Assert.Contains("- 2026-10-27, Call with Ken: [00:05] said \"行きます\" -> better \"行く\"", text);
        Assert.Contains("- 2026-10-27, Call with Ken: [01:35] said \"楽しかったです\"", text);
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
    public void ACall_QuotesTranscriptLinesAroundAnExample_AndLeavesOutWhatWasDropped()
    {
        ContextBlock block = ContextBlock.Create(ContextKind.Call, "k1", "Call");
        block.SetAround("f:f1", 1);

        string text = ContextWriter.Call(Detail(), block);

        Assert.Contains("    Transcript around it:\n    > [01:30] Ken: 週末どうだった？\n    > [01:35] Me: うん、楽しかったです   <- here\n    > [01:40] Ken: よかったね\n", text);
        Assert.DoesNotContain("もしもし", text);

        // A wider default reaches every example; the one set by hand keeps its own number
        block.Around = 2;
        text = ContextWriter.Call(Detail(), block);
        Assert.DoesNotContain("もしもし、元気？", text);
        block.SetAround("f:f1", 2);
        Assert.Empty(block.AroundFor);
        text = ContextWriter.Call(Detail(), block);
        Assert.Contains("> [00:05] Me: もしもし、元気？", text);

        // Dropping the example keeps the mistake, dropping the mistake drops both
        block.Pick("f:f1", false);
        text = ContextWriter.Call(Detail(), block);
        Assert.Contains("too polite with friends", text);
        Assert.DoesNotContain("楽しかったです", text);
        block.Pick("issue:i1", false);
        text = ContextWriter.Call(Detail(), block);
        Assert.DoesNotContain("Mistakes in this call", text);
    }

    [Fact]
    public void Insights_ListEveryExampleToPick_AndQuoteTheCallsTheyCameFrom()
    {
        ContextBlock block = ContextBlock.Create(ContextKind.Insights, string.Empty, "Insights");

        ContextItemGroup group = Assert.Single(ContextItems.For(block, Insights()));
        ContextItem struggle = Assert.Single(group.Items);
        // Only the example is in until every occurrence is asked for or one is picked by hand
        Assert.Equal([false, true], struggle.Children.Select(c => c.Default));
        Assert.All(struggle.Children, c => Assert.True(c.CanQuote));

        block.Pick("f:o1", true, false);
        block.Around = 1;
        string text = ContextWriter.Insights(Insights(), block, id => id == "k1" ? Detail() : null);

        Assert.Contains("   - 2026-10-27, Call with Ken: [00:05] said \"行きます\"", text);
        Assert.Contains("     > [00:05] Me: もしもし、元気？   <- here", text);
        Assert.Contains("     > [01:35] Me: うん、楽しかったです   <- here", text);
        Assert.Equal((3, 3), ContextItems.Count(block, [group]));

        // Without the call loaded the examples are still written, just without the transcript
        text = ContextWriter.Insights(Insights(), block);
        Assert.Contains("said \"行きます\"", text);
        Assert.DoesNotContain("<- here", text);
    }

    [Fact]
    public void Lessons_LeaveOutDroppedLessonsAndWeakSpots()
    {
        LearningOverview overview = new()
        {
            Struggles = [new LearningStruggle { Id = "w1", Kind = StruggleKind.Grammar, Title = "て-form" }, new LearningStruggle { Id = "w2", Kind = StruggleKind.Vocabulary, Title = "counters" }],
            Recent = [new LearningLesson { Id = "l1", Topic = "Casual speech", CreatedAt = CallDay }, new LearningLesson { Id = "l2", Topic = "Keigo", CreatedAt = CallDay }]
        };
        ContextBlock block = ContextBlock.Create(ContextKind.Lessons, string.Empty, "Lessons");
        block.Pick("weak:w2", false);
        block.Pick("lesson:l1", false);

        string text = ContextWriter.Lessons(overview, block);

        Assert.Contains("て-form", text);
        Assert.DoesNotContain("counters", text);
        Assert.Contains("### Some of my recent lessons", text);
        Assert.Contains("Keigo", text);
        Assert.DoesNotContain("Casual speech", text);
    }

    [Fact]
    public void Compose_PutsEveryBlockUnderItsHeading()
    {
        string text = ContextWriter.Compose([("About me", "I learn Japanese."), ("Call with Ken", "### Overview\nDate: 2026-10-27")], new DateTime(2026, 11, 5));

        Assert.Equal("# Context from Akagi, exported 2026-11-05\n\n## About me\n\nI learn Japanese.\n\n## Call with Ken\n\n### Overview\nDate: 2026-10-27\n", text);
    }

    [Fact]
    public async Task ThePage_StartsWithANote_AddsACall_AndDownloadsWhatIsIncluded()
    {
        BunitJSModuleInterop module = JSInterop.SetupModule(Module);
        module.Mode = JSRuntimeMode.Loose;
        AnswerLikeTheBot();
        IRenderedComponent<ContextBuilderPage> cut = Render<ContextBuilderPage>();

        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("[data-block-kind='Note']")));
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("[data-add='call:k1']")));

        await cut.Find("[data-add='call:k1']").ClickAsync(new MouseEventArgs());

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("[data-block-kind='Call'] [data-block-size]")));
        Assert.Contains("Call with Ken on 2026-10-27", cut.Find("[data-block-kind='Call'] [data-block-title]").TextContent);
        Assert.NotEmpty(cut.FindAll("[data-added='call:k1']"));
        Assert.Equal("2", cut.Find("[data-summary='blocks']").TextContent);

        await cut.Find("[data-block-kind='Call'] [data-section='transcript']").ClickAsync(new MouseEventArgs());
        await cut.Find("[data-context-download]").ClickAsync(new MouseEventArgs());

        string file = (string)module.VerifyInvoke("download").Arguments[1]!;
        Assert.Contains("## About me and this file", file);
        Assert.Contains("## Call with Ken on 2026-10-27", file);
        Assert.Contains("[00:05] Me: もしもし、元気？", file);
        Assert.True(file.IndexOf("About me", StringComparison.Ordinal) < file.IndexOf("Call with Ken", StringComparison.Ordinal));

        // Moved to the top and the note left out
        await cut.Find("[data-block-kind='Call'] [data-block-up]").ClickAsync(new MouseEventArgs());
        await cut.Find("[data-block-kind='Note'] [data-block-include]").ChangeAsync(new ChangeEventArgs { Value = false });
        await cut.Find("[data-context-download]").ClickAsync(new MouseEventArgs());

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
    public async Task ThePage_PicksItemsOneByOne_AndQuotesLinesAroundOneExample()
    {
        BunitJSModuleInterop module = JSInterop.SetupModule(Module);
        module.Mode = JSRuntimeMode.Loose;
        module.Setup<string?>("load", "akagi.context.v1").SetResult(JsonSerializer.Serialize(new List<ContextBlock> { ContextBlock.Create(ContextKind.Call, "k1", "Call") }));
        AnswerLikeTheBot();
        IRenderedComponent<ContextBuilderPage> cut = Render<ContextBuilderPage>();

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("[data-block-pick]")));
        Assert.Contains("(4 of 4)", cut.Find("[data-block-pick]").TextContent);
        await cut.Find("[data-block-pick]").ClickAsync(new MouseEventArgs());

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("[data-item='f:f1'] [data-around-more]")));
        await cut.Find("[data-item='f:f1'] [data-around-more]").ClickAsync(new MouseEventArgs());        Assert.Equal("1", cut.Find("[data-item='f:f1'] [data-around]").GetAttribute("data-around"));
        await cut.Find("[data-context-download]").ClickAsync(new MouseEventArgs());
        string file = (string)module.VerifyInvoke("download").Arguments[1]!;
        Assert.Contains("> [01:35] Me: うん、楽しかったです   <- here", file);

        await cut.Find("[data-item='issue:i1'] [data-item-pick]").ChangeAsync(new ChangeEventArgs { Value = false });
        Assert.Contains("(2 of 4)", cut.Find("[data-block-pick]").TextContent);
        // The examples of a dropped mistake cannot be picked on their own
        Assert.True(cut.Find("[data-item='f:f1'] [data-item-pick]").HasAttribute("disabled"));
        await cut.Find("[data-context-download]").ClickAsync(new MouseEventArgs());
        file = (string)module.VerifyInvoke("download", 2)[1].Arguments[1]!;
        Assert.DoesNotContain("too polite with friends", file);
        Assert.Contains("Ken moves to Osaka", file);

        ContextBlock saved = JsonSerializer.Deserialize<List<ContextBlock>>((string)module.Invocations["save"].Last().Arguments[1]!)!.Single();
        Assert.Equal(new Dictionary<string, bool> { ["issue:i1"] = false }, saved.Picks);
        Assert.Equal(new Dictionary<string, int> { ["f:f1"] = 1 }, saved.AroundFor);
    }

    [Fact]
    public async Task ThePage_LoadsTheCallsInsightsQuoteFrom()
    {
        BunitJSModuleInterop module = JSInterop.SetupModule(Module);
        module.Mode = JSRuntimeMode.Loose;
        ContextBlock insights = ContextBlock.Create(ContextKind.Insights, string.Empty, "Insights");
        insights.Around = 1;
        module.Setup<string?>("load", "akagi.context.v1").SetResult(JsonSerializer.Serialize(new List<ContextBlock> { insights }));
        AnswerLikeTheBot();
        IRenderedComponent<ContextBuilderPage> cut = Render<ContextBuilderPage>();

        // The insights come first, then the call their example is from
        cut.WaitForAssertion(() =>
        {
            lock (_asked)
            {
                Assert.Contains(_asked, a => a.Action == CallAnalysisAction.Detail && a.CallId == "k1");
            }
            Assert.NotEmpty(cut.FindAll("[data-block-size]"));
            Assert.Empty(cut.FindAll("[data-context-summary] .spinner-border"));
        });
        await cut.Find("[data-context-download]").ClickAsync(new MouseEventArgs());

        Assert.Contains("> [01:35] Me: うん、楽しかったです   <- here", (string)module.VerifyInvoke("download").Arguments[1]!);
        // The call was only loaded on the side, not added
        Assert.Single(cut.FindAll("[data-block]"));
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
