using Akagi.Bridge.Chat.Models;
using Akagi.Bridge.Chat.Transmissions.Requests;
using Akagi.Bridge.Chat.Transmissions.Responses;
using Akagi.Communication.SocketComs.Transmissions;
using Akagi.Life.Japanese;
using Akagi.Life.Japanese.Calls;
using Akagi.Life.Japanese.Vocabulary;
using Akagi.Life.Japanese.Vocabulary.Reports;
using Akagi.Utils;
using MessagePack;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Bson;

namespace Akagi.Tests.Life;

/// <summary>
/// A dictionary that knows a few words, for reports that show meanings and bands.
/// </summary>
internal sealed class StubWordDictionary : IWordDictionary
{
    public bool Ready => true;

    public WordInfo? Lookup(string lemma, string reading, WordClass? wordClass = null) => lemma switch
    {
        "忙しい" => new WordInfo("busy", 9_000),
        "まあ" => new WordInfo("well", WordInfo.Common),
        "美味しい" => new WordInfo("delicious", WordInfo.Common),
        _ => null
    };
}

public class VocabularyTests
{
    private const string UserId = "6adcc28c4f351a3450e30ab6";

    internal static CallJob Call(string partner, DateTime date, params (string Speaker, double Start, string Text)[] lines) => new()
    {
        Id = ObjectId.GenerateNewId().ToString(),
        UserId = UserId,
        Partner = partner,
        LearnerChannel = LearnerChannel.Left,
        LearnerSpeaker = "Me",
        RecordedAt = date,
        CreatedAt = date,
        UpdatedAt = date,
        Status = CallJobStatus.Completed,
        Segments = [.. lines.Select(l => new TranscriptSegment { Speaker = l.Speaker, Start = l.Start, End = l.Start + 2, Text = l.Text })]
    };

    private static readonly DateTime First = new(2026, 10, 21, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Second = new(2026, 10, 27, 0, 0, 0, DateTimeKind.Utc);

    private static List<CallTokens> TwoCalls() =>
    [
        CallTokens.Build(Call("Rii", First,
            ("Rii", 0, "今日は美味しいラーメンを食べた。美味しい店だった。"),
            ("Me", 3, "まあ、いいね。まあ、私は忙しいです。"),
            ("Rii", 6, "えー、忙しいの？仕事が忙しい？"),
            ("Me", 9, "まあ、そうそう。昨日も働いていた。"))),
        CallTokens.Build(Call("Rii", Second,
            ("Rii", 0, "映画を見に行こう。"),
            ("Me", 3, "まあ、いいね。ラーメンも食べたいよね。"),
            ("Me", 6, "ラーメンは美味しいかな。"),
            ("Rii", 9, "美味しいよ！")))
    ];

    private static VocabularyView View(VocabFilter? filter = null) => new(TwoCalls(), filter ?? new VocabFilter(), new StubWordDictionary());

    [Fact]
    public void Tokens_KnowTheirSentenceSpeakerAndTime()
    {
        CallTokens call = TwoCalls()[0];

        Assert.Equal(8, call.Sentences.Count);
        VocabSentence question = call.Sentences.Single(s => call.Text(s) == "えー、忙しいの？");
        Assert.True(question.Question);
        Assert.False(question.Learner);
        Assert.Equal(6, question.Time);
        Assert.Contains(call.Tokens, t => t.Learner && t.Token.Lemma == "忙しい" && t.Time == 3);
        Assert.Equal("Call with Rii", call.Name);
    }

    [Fact]
    public void View_CountsBothSides_AndFirstUses()
    {
        VocabularyView view = View();

        WordCount tasty = view.Counts["美味しい"];
        Assert.Equal((1, 3), (tasty.Learner, tasty.Partner));
        Assert.Equal(Second, tasty.FirstSaid!.Value.Call.Date);
        Assert.Equal(First, tasty.FirstHeard!.Value.Call.Date);
        Assert.Equal(4, view.Counts["まあ"].Learner);
        Assert.Equal(2, view.Counts["まあ"].LearnerCalls);
        // Grammar is not vocabulary
        Assert.False(view.Counts.ContainsKey("は"));
        Assert.False(view.Counts.ContainsKey("た"));
    }

    [Fact]
    public void Filter_SelectsCallsClassesAndSearch()
    {
        Assert.Single(View(new VocabFilter { From = Second }).Calls);
        Assert.Empty(View(new VocabFilter { Partner = "Ken" }).Calls);
        Assert.All(View(new VocabFilter { Classes = ["Verb"] }).Counts.Values, w => Assert.Equal(WordClass.Verb, w.Class));
        Assert.Equal(["美味しい"], View(new VocabFilter { Search = "delicious" }).Listed.Select(w => w.Lemma));
        Assert.DoesNotContain(View(new VocabFilter { MinCount = 3 }).Listed, w => w.Total < 3);
    }

    [Fact]
    public void Keyness_IsSignedLogLikelihood()
    {
        Assert.Equal(0, VocabularyView.Keyness(10, 10, 1000, 1000), 6);
        Assert.True(VocabularyView.Keyness(30, 2, 1000, 1000) > 10.83);
        Assert.True(VocabularyView.Keyness(2, 30, 1000, 1000) < -10.83);
        // The same counts mean less when the other side said far more
        Assert.True(VocabularyView.Keyness(10, 10, 1000, 4000) > VocabularyView.Keyness(10, 10, 1000, 1000));
    }

    [Fact]
    public void Mattr_DoesNotPunishLength()
    {
        List<string> varied = [.. Enumerable.Range(0, 200).Select(i => "w" + (i % 40))];
        List<string> repetitive = [.. Enumerable.Range(0, 200).Select(i => "w" + (i % 5))];

        Assert.Equal(0.8, VocabularyView.Mattr(varied)!.Value, 2);
        Assert.Equal(0.1, VocabularyView.Mattr(repetitive)!.Value, 2);
        Assert.Equal(VocabularyView.Mattr([.. varied.Take(100)])!.Value, VocabularyView.Mattr(varied)!.Value, 2);
        Assert.Null(VocabularyView.Mattr([]));
    }

    [Theory]
    [InlineData("昨日も働いていた。", "働く", new[] { "ている (ongoing)", "past (た)" })]
    [InlineData("ラーメンも食べたい。", "食べる", new[] { "want (たい)" })]
    [InlineData("映画を見に行こう。", "行く", new[] { "volitional (よう)" })]
    [InlineData("雨が降ったら行かない。", "降る", new[] { "conditional (たら)" })]
    [InlineData("全部食べちゃった。", "食べる", new[] { "てしまう / ちゃう", "past (た)" })]
    [InlineData("行きます。", "行く", new[] { "polite (ます)" })]
    [InlineData("見てから寝る。", "見る", new[] { "て-form" })]
    [InlineData("毎日走る。", "走る", new[] { "dictionary form" })]
    public void VerbForms_AreReadFromWhatFollows(string sentence, string verb, string[] forms)
    {
        CallTokens call = CallTokens.Build(Call("Rii", First, ("Me", 0, sentence)));
        int index = call.Tokens.FindIndex(t => t.Token.Lemma == verb && t.Token.Pos == "動詞");

        Assert.Equal([.. forms.Order()], VerbFormsReport.Classify(call.Tokens, index, call.Sentences[0].End).Order());
    }

    [Theory]
    [InlineData("それは美味しいよね。", "〜よね")]
    [InlineData("どうかな？", "〜かな")]
    [InlineData("行きたいけど。", "〜けど")]
    [InlineData("一週間で。", "〜で")]
    [InlineData("そうです。", "〜です")]
    [InlineData("ラーメン。", "noun")]
    [InlineData("うん。", "reaction only")]
    public void Endings_NameHowASentenceEnds(string sentence, string ending)
    {
        CallTokens call = CallTokens.Build(Call("Rii", First, ("Me", 0, sentence)));

        Assert.Equal(ending, EndingsReport.Ending(call.Tokens, call.Sentences.Single()));
    }

    [Fact]
    public void Chunks_KeepTheLongestRecurringSequence()
    {
        List<CallTokens> calls = [CallTokens.Build(Call("Rii", First,
            ("Me", 0, "まあそうだね。"), ("Me", 2, "まあそうだね。"), ("Me", 4, "まあそうだね。"), ("Rii", 6, "そうだね。")))];

        Dictionary<string, (int Learner, int Partner, string Example)> chunks = ChunksReport.Count(new VocabularyView(calls, new VocabFilter(), new StubWordDictionary()));

        Assert.Equal((3, 0), (chunks["まあそうだね"].Learner, chunks["まあそうだね"].Partner));
        // そうだね also stands alone once, so it is kept with all four of its uses
        Assert.Equal((3, 1), (chunks["そうだね"].Learner, chunks["そうだね"].Partner));
        Assert.False(chunks.ContainsKey("そうだ"));
        Assert.False(chunks.ContainsKey("まあそう"));
    }

    [Fact]
    public void Reports_ComparingSides_FindRelianceAndTheirWords()
    {
        VocabularyView view = View();

        VocabReportResult rely = new RelyOnReport().Run(view, new Dictionary<string, string>());
        VocabReportResult theirs = new PartnersUseReport().Run(view, new Dictionary<string, string> { ["show"] = "never" });
        VocabReportResult uptake = new UptakeReport().Run(view, new Dictionary<string, string>());

        Assert.Equal("まあ", rely.Tables[0].Rows[0].Cells[0].Text);
        Assert.Equal("well", rely.Tables[0].Rows[0].Cells[3].Text);
        Assert.All(theirs.Tables[0].Rows, r => Assert.Equal("0", r.Cells[8].Text));
        // Rii said 美味しい in the first call, you only in the second
        VocabRow picked = Assert.Single(uptake.Tables[0].Rows, r => r.Cells[0].Text == "美味しい");
        Assert.Contains("2026-10-21", picked.Cells[5].Text);
        Assert.Contains("2026-10-27", picked.Cells[6].Text);
    }

    [Fact]
    public void EveryReport_RunsAndSurvivesTheWire()
    {
        VocabularyView view = View();
        foreach (VocabularyReport report in TypeUtils.GetNonAbstractTypesExtendingFrom<VocabularyReport>().Select(t => (VocabularyReport)Activator.CreateInstance(t)!))
        {
            Assert.Contains(report.Group, VocabularyReport.GroupOrder);
            VocabReportResult result = report.Run(view, new Dictionary<string, string>());
            VocabReportResult copy = MessagePackSerializer.Deserialize<VocabReportResult>(MessagePackSerializer.Serialize(result));
            Assert.Equal(report.Key, copy.Key);
            Assert.True(copy.Stats.Length + copy.Series.Length + copy.Tables.Length > 0 || copy.Note.Length > 0, report.Key);
            Assert.All(copy.Tables, t => Assert.All(t.Rows, r => Assert.Equal(t.Columns.Length, r.Cells.Length)));
        }
    }

    [Fact]
    public void WordDetail_ShowsFormsLinesAndTimeline()
    {
        VocabWordDetail detail = WordDetail.Build(View(), "美味しい")!;

        Assert.Equal(("delicious", 1, 3), (detail.Meaning, detail.Learner, detail.Partner));
        Assert.Equal(4, detail.Lines.Length);
        // Newest call first
        Assert.Equal(Second, detail.Lines[0].Date);
        Assert.Contains("美味しい", detail.Lines[0].Marks);
        Assert.Equal([0.0, 1.0], detail.Timeline.Points.Select(p => p.Values[0]!.Value));
        Assert.Equal(First, detail.FirstHeard);
        Assert.Null(WordDetail.Build(View(), "存在しない"));
    }

    [Fact]
    public void JmdictBands_ComeFromPriorities()
    {
        Assert.Equal(9_000, JmdictWordDictionary.Rank(["Ichimango1", "Newspaper1", "Frequency18"]));
        Assert.Equal(WordInfo.Common, JmdictWordDictionary.Rank(["Ichimango1"]));
        Assert.Equal(int.MaxValue, JmdictWordDictionary.Rank([]));
        Assert.Equal("top 10k", new WordInfo("", 9_000).Band);
        Assert.Equal("common", new WordInfo("", WordInfo.Common).Band);
        Assert.Equal("uncommon", new WordInfo("", int.MaxValue).Band);
    }

    [Fact]
    public async Task Handler_ListsReportsAndRunsThem_OnlyForTheUsersSeparatedCalls()
    {
        InMemoryCallJobDatabase jobs = new();
        CallJob mine = Call("Rii", First, ("Rii", 0, "美味しい？"), ("Me", 2, "まあ、美味しいよ。"));
        CallJob mixed = Call("Ken", Second, ("", 0, "こんにちは"));
        mixed.LearnerChannel = LearnerChannel.Mixed;
        mixed.LearnerSpeaker = string.Empty;
        CallJob foreign = Call("Rii", Second, ("Me", 0, "秘密"));
        foreign.UserId = ObjectId.GenerateNewId().ToString();
        foreach (CallJob job in new[] { mine, mixed, foreign })
        {
            await jobs.SaveDocumentAsync(job);
        }
        VocabularyReport[] reports = [.. TypeUtils.GetNonAbstractTypesExtendingFrom<VocabularyReport>().Select(t => (VocabularyReport)Activator.CreateInstance(t)!)];
        VocabularyRequestHandler handler = new(new VocabularyIndex(jobs), new StubWordDictionary(), reports, NullLogger<VocabularyRequestHandler>.Instance);

        VocabCatalog catalog = (await handler.ProcessAsync(UserId, new VocabularyRequestTransmission { Action = VocabularyAction.Catalog })).Catalog!;
        Assert.Equal(["summary", "growth"], catalog.Reports.Take(2).Select(r => r.Key));
        Assert.Equal(mine.Id, Assert.Single(catalog.Calls).Id);
        Assert.Equal(["Rii"], catalog.Partners);
        Assert.Equal(1, catalog.MixedCalls);

        VocabularyResponseTransmission report = await handler.ProcessAsync(UserId, new VocabularyRequestTransmission { Action = VocabularyAction.Report, ReportKey = "frequency" });
        Assert.Contains(report.Report!.Tables[0].Rows, r => r.Cells[0].Text == "美味しい");
        Assert.DoesNotContain(report.Report.Tables[0].Rows, r => r.Cells[0].Text == "秘密");

        Assert.NotNull((await handler.ProcessAsync(UserId, new VocabularyRequestTransmission { Action = VocabularyAction.Report, ReportKey = "nope" })).Error);
        Assert.NotNull((await handler.ProcessAsync(UserId, new VocabularyRequestTransmission { Action = VocabularyAction.Report, ReportKey = "summary", Filter = new VocabFilter { Partner = "Nobody" } })).Error);
        Assert.Equal(2, (await handler.ProcessAsync(UserId, new VocabularyRequestTransmission { Action = VocabularyAction.Word, Lemma = "美味しい" })).Word!.Lines.Length);
    }

    [Fact]
    public async Task Index_RebuildsACallOnlyWhenItChanged()
    {
        InMemoryCallJobDatabase jobs = new();
        CallJob job = Call("Rii", First, ("Me", 0, "まあ、いいね。"));
        await jobs.SaveDocumentAsync(job);
        VocabularyIndex index = new(jobs);

        CallTokens before = Assert.Single((await index.LoadAsync(UserId)).Calls);
        Assert.Same(before, Assert.Single((await index.LoadAsync(UserId)).Calls));

        job.Segments.Add(new TranscriptSegment { Speaker = "Me", Start = 3, End = 4, Text = "美味しい。" });
        job.UpdatedAt = job.UpdatedAt.AddMinutes(1);
        CallTokens after = Assert.Single((await index.LoadAsync(UserId)).Calls);
        Assert.NotSame(before, after);
        Assert.Contains(after.Tokens, t => t.Token.Lemma == "美味しい");
    }
}
