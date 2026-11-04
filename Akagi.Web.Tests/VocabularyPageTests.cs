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
/// The vocabulary page renders whatever reports the server offers, through generic stat, chart and table views.
/// </summary>
public class VocabularyPageTests : BunitContext
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
    private readonly HashSet<VocabularyRequest> _answered = [];

    public VocabularyPageTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        _client = new SocketClient(IPAddress.Loopback, 1, new User { GoogleId = "g", Name = "Tester", Email = "t" }, "circuit",
            NullLoggerFactory.Instance, [], null!);
        GC.SuppressFinalize(_client);
    }

    private VocabularyRequestTransmission Respond(Func<VocabularyRequestTransmission, VocabularyResponseTransmission> answer)
    {
        VocabularyRequest? request = null;
        for (int i = 0; i < 200 && request == null; i++)
        {
            request = _client.GetRequests<VocabularyRequest>().FirstOrDefault(r => !_answered.Contains(r));
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

    private static VocabCatalog Catalog() => new()
    {
        DictionaryReady = true,
        Partners = ["Rii"],
        Calls = [new VocabCallInfo { Id = "c1", Name = "Call with Rii", Date = new DateTime(2026, 10, 21, 0, 0, 0, DateTimeKind.Utc), Partner = "Rii" }],
        Reports =
        [
            new VocabReportInfo { Key = "summary", Title = "At a glance", Group = "Overview" },
            new VocabReportInfo { Key = "rely-on", Title = "Words you rely on", Group = "Compare" },
            new VocabReportInfo
            {
                Key = "chunks", Title = "Chunks", Group = "Chunks",
                Options = [new VocabOption { Key = "whose", Label = "Show", Default = "yours", Choices = [new VocabChoice { Value = "yours", Label = "Yours" }, new VocabChoice { Value = "theirs", Label = "Theirs" }] }]
            }
        ]
    };

    private static VocabReportResult Summary() => new()
    {
        Key = "summary",
        Stats = [new VocabStat { Label = "Your words", Value = "2264" }],
        Series =
        [
            new VocabSeries
            {
                Title = "Different words per call", Names = ["You", "Partners"], Unit = VocabUnit.Count,
                Points = [new VocabPoint { Label = "Call with Rii", CallId = "c1", Values = [366, 259] }]
            },
            new VocabSeries
            {
                Title = "Verb forms per 100 verbs", Names = ["You", "Partners"], Unit = VocabUnit.Decimal,
                Points = [new VocabPoint { Label = "past (た)", Values = [16.7, 26.5] }, new VocabPoint { Label = "て-form", Values = [3.6, 9.4] }]
            }
        ]
    };

    private static VocabReportResult Rely() => new()
    {
        Key = "rely-on",
        Tables =
        [
            new VocabTable
            {
                Title = "Words you rely on",
                Columns = [new VocabColumn { Label = "Word", Kind = VocabColumnKind.Word }, new VocabColumn { Label = "You", Kind = VocabColumnKind.Number }, new VocabColumn { Label = "First said", Kind = VocabColumnKind.Call }],
                Rows =
                [
                    new VocabRow { Cells = [new VocabCell { Text = "まあ", Link = "まあ" }, new VocabCell { Text = "307", Sort = 307 }, new VocabCell { Text = "Call with Rii", Link = "c1", Time = 120, Sort = 1 }] },
                    new VocabRow { Cells = [new VocabCell { Text = "あまり", Link = "あまり" }, new VocabCell { Text = "20", Sort = 20 }, new VocabCell { Text = "Call with Rii", Link = "c1", Sort = 2 }] }
                ],
                SortColumn = 1,
                Descending = true
            }
        ]
    };

    private IRenderedComponent<VocabularyPage> Open()
    {
        Services.AddSingleton<ISocketClientContainer>(new Sockets { Client = _client });
        IRenderedComponent<VocabularyPage> cut = Render<VocabularyPage>();
        Assert.Equal(VocabularyAction.Catalog, Respond(_ => new VocabularyResponseTransmission { Catalog = Catalog() }).Action);
        Assert.Equal("summary", Respond(_ => new VocabularyResponseTransmission { Report = Summary() }).ReportKey);
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("[data-report='summary']")));
        return cut.Settled();
    }

    [Fact]
    public void Renders_StatsAndBothKindsOfChart()
    {
        IRenderedComponent<VocabularyPage> cut = Open();

        Assert.Equal("2264", cut.Find("[data-stat='Your words']").TextContent.Trim());
        // Calls become columns with a readout; categories become labeled bars
        Assert.NotEmpty(cut.FindAll("[data-series='Different words per call'] [data-point='c1']"));
        Assert.Equal(2, cut.FindAll("[data-series='Verb forms per 100 verbs'] [data-category]").Count);
        Assert.Contains("26.5", cut.Find("[data-category='past (た)']").TextContent);
        Assert.Equal(["Overview", "Compare", "Chunks"], cut.FindAll("[data-reports] .list-group-header").Select(h => h.TextContent.Trim()));
    }

    [Fact]
    public void Tables_SortAndOpenWordsAndCalls()
    {
        IRenderedComponent<VocabularyPage> cut = Open();
        cut.Find("[data-report-key='rely-on']").Click();
        Respond(_ => new VocabularyResponseTransmission { Report = Rely() });
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("[data-word]")));
        cut.Settled();

        Assert.Equal(["まあ", "あまり"], cut.FindAll("[data-word]").Select(w => w.TextContent));
        Assert.Equal("Learn/Calls/c1?at=120", cut.Find("[data-table] a").GetAttribute("href"));
        cut.Find("[data-sort='1']").Click();
        Assert.Equal(["あまり", "まあ"], cut.FindAll("[data-word]").Select(w => w.TextContent));

        cut.Find("[data-word='まあ']").Click();
        VocabularyRequestTransmission asked = Respond(_ => new VocabularyResponseTransmission
        {
            Word = new VocabWordDetail
            {
                Lemma = "まあ", Class = "adverb", Meaning = "well", Learner = 307, Partner = 2, TotalLines = 1,
                Forms = [new VocabForm { Surface = "まあ", Learner = 307, Partner = 2 }],
                Timeline = new VocabSeries { Title = "Uses per call", Names = ["You"], Points = [new VocabPoint { Label = "c", CallId = "c1", Values = [307] }] },
                Lines = [new VocabLine { CallId = "c1", Time = 61, IsLearner = true, Speaker = "You", Text = "まあ、いいね。", Marks = ["まあ"], Date = DateTime.UtcNow }]
            }
        });

        Assert.Equal((VocabularyAction.Word, "まあ"), (asked.Action, asked.Lemma));
        cut.WaitForAssertion(() => Assert.Equal("307", cut.Find("[data-word-you]").TextContent.Trim()));
        Assert.Contains("<mark>まあ</mark>", cut.Find("[data-word-lines]").InnerHtml);
        Assert.Contains("?at=61", cut.Find("[data-word-lines] a").GetAttribute("href"));
        cut.Find("[data-word-close]").Click();
        Assert.Empty(cut.FindAll("[data-word-detail]"));
    }

    [Fact]
    public void FilterAndOptions_GoWithTheRequest()
    {
        IRenderedComponent<VocabularyPage> cut = Open();

        cut.Find("#vocab-partner").Change("Rii");
        VocabularyRequestTransmission filtered = Respond(_ => new VocabularyResponseTransmission { Report = Summary() });
        Assert.Equal(("summary", "Rii"), (filtered.ReportKey, filtered.Filter.Partner));

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("[data-report='summary']")));
        cut.Settled();
        cut.Find("#vocab-classes").Change("Verb");
        Assert.Equal(["Verb"], Respond(_ => new VocabularyResponseTransmission { Report = Summary() }).Filter.Classes);
        cut.Settled();

        cut.Find("[data-report-key='chunks']").Click();
        Respond(_ => new VocabularyResponseTransmission { Report = new VocabReportResult { Key = "chunks", Note = "none" } });
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("[data-option='whose']")));
        cut.Settled();
        cut.Find("[data-option='whose']").Change("theirs");
        VocabularyRequestTransmission option = Respond(_ => new VocabularyResponseTransmission { Report = new VocabReportResult { Key = "chunks" } });
        Assert.Equal(("chunks", "theirs", "Rii"), (option.ReportKey, option.Options["whose"], option.Filter.Partner));
    }

    [Fact]
    public void Csv_QuotesEveryCell()
    {
        string csv = VocabText.Csv(new VocabTable
        {
            Columns = [new VocabColumn { Label = "Word" }, new VocabColumn { Label = "Meaning" }],
            Rows = [new VocabRow { Cells = [new VocabCell { Text = "まあ" }, new VocabCell { Text = "just (e.g. \"wait\"); well" }] }]
        });

        Assert.Equal("\"Word\",\"Meaning\"\r\n\"まあ\",\"just (e.g. \"\"wait\"\"); well\"", csv);
    }

    [Fact]
    public void WithoutCalls_SaysWhereWordsComeFrom()
    {
        Services.AddSingleton<ISocketClientContainer>(new Sockets { Client = _client });
        IRenderedComponent<VocabularyPage> cut = Render<VocabularyPage>();
        Respond(_ => new VocabularyResponseTransmission { Catalog = new VocabCatalog() });

        cut.WaitForAssertion(() => Assert.Contains("No calls to read yet", cut.Markup));
    }
}
