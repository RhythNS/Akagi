using Akagi.Bridge.Chat.Models;
using Akagi.Bridge.Chat.Transmissions.Requests;
using Akagi.Bridge.Chat.Transmissions.Responses;
using Akagi.Bridge.Japanese;
using Akagi.Communication.SocketComs.Transmissions;
using Akagi.Life.Japanese;
using Akagi.Life.Japanese.Conjugations;
using Akagi.Life.Japanese.Vocabulary;
using Akagi.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Bson;
using Wacton.Desu.Japanese;

namespace Akagi.Tests.Life;

internal class InMemoryConjugationItemDatabase : InMemoryDatabase<ConjugationItem>, IConjugationItemDatabase
{
    public InMemoryConjugationItemDatabase() : base("conjugation_items")
    {
    }

    public Task<List<ConjugationItem>> GetForUser(string userId) => Task.FromResult(Items.Where(i => i.UserId == userId).ToList());

    public Task<ConjugationItem?> FindAsync(string userId, string verb, ConjugationForm form, bool reverse) =>
        Task.FromResult(Items.FirstOrDefault(i => i.UserId == userId && i.Verb == verb && i.Form == form && i.Reverse == reverse));

    public async Task DeleteForUserAsync(string userId)
    {
        foreach (ConjugationItem item in Items.Where(i => i.UserId == userId).ToList())
        {
            await DeleteDocumentByIdAsync(item.Id!);
        }
    }
}

internal class InMemoryConjugationAttemptDatabase : InMemoryDatabase<ConjugationAttempt>, IConjugationAttemptDatabase
{
    public InMemoryConjugationAttemptDatabase() : base("conjugation_attempts")
    {
    }

    public Task<List<ConjugationAttempt>> GetSinceAsync(string userId, DateTime since) =>
        Task.FromResult(Items.Where(a => a.UserId == userId && a.At >= since).ToList());

    public Task<List<ConjugationAttempt>> GetMistakesAsync(string userId, int count) =>
        Task.FromResult(Items.Where(a => a.UserId == userId && !a.Correct).OrderByDescending(a => a.At).Take(count).ToList());

    public async Task DeleteForUserAsync(string userId)
    {
        foreach (ConjugationAttempt attempt in Items.Where(a => a.UserId == userId).ToList())
        {
            await DeleteDocumentByIdAsync(attempt.Id!);
        }
    }
}

internal class InMemoryConjugationPreferencesDatabase : InMemoryDatabase<ConjugationPreferences>, IConjugationPreferencesDatabase
{
    public InMemoryConjugationPreferencesDatabase() : base("conjugation_preferences")
    {
    }

    public Task<ConjugationPreferences?> GetForUser(string userId) => Task.FromResult(Items.FirstOrDefault(p => p.UserId == userId));
}

internal class FakeVerbCatalog(params DictionaryVerb[] verbs) : IVerbCatalog
{
    public bool Ready => true;
    public IReadOnlyList<DictionaryVerb> Common => verbs;
    public DictionaryVerb? Find(string lemma) => verbs.Where(v => v.Written == lemma || v.Reading == lemma).OrderBy(v => v.Rank).FirstOrDefault();
}

internal class FakeVocabulary(params CallTokens[] calls) : IVocabularyIndex
{
    public Task<(List<CallTokens> Calls, int Mixed)> LoadAsync(string userId) => Task.FromResult((calls.ToList(), 0));
}

/// <summary>
/// The conjugation engine, typed answers, the JMdict verb list and the progress the practice page keeps.
/// </summary>
public class ConjugationTests
{
    private static readonly string UserId = ObjectId.GenerateNewId().ToString();

    private static string Reading(string written, string reading, VerbClass verbClass, ConjugationForm form) =>
        Conjugator.Conjugate(new Verb(written, reading, verbClass), form)?.Main.Reading ?? "(none)";

    private static string Written(string written, string reading, VerbClass verbClass, ConjugationForm form) =>
        Conjugator.Conjugate(new Verb(written, reading, verbClass), form)?.Main.Written ?? "(none)";

    [Theory]
    [InlineData("書く", "かく", ConjugationForm.Te, "かいて")]
    [InlineData("泳ぐ", "およぐ", ConjugationForm.Past, "およいだ")]
    [InlineData("話す", "はなす", ConjugationForm.Te, "はなして")]
    [InlineData("待つ", "まつ", ConjugationForm.Past, "まった")]
    [InlineData("死ぬ", "しぬ", ConjugationForm.Te, "しんで")]
    [InlineData("遊ぶ", "あそぶ", ConjugationForm.Conditional, "あそんだら")]
    [InlineData("読む", "よむ", ConjugationForm.Past, "よんだ")]
    [InlineData("分かる", "わかる", ConjugationForm.Potential, "わかれる")]
    [InlineData("買う", "かう", ConjugationForm.Negative, "かわない")]
    [InlineData("買う", "かう", ConjugationForm.Passive, "かわれる")]
    [InlineData("書く", "かく", ConjugationForm.Polite, "かきます")]
    [InlineData("書く", "かく", ConjugationForm.PolitePastNegative, "かきませんでした")]
    [InlineData("書く", "かく", ConjugationForm.Volitional, "かこう")]
    [InlineData("書く", "かく", ConjugationForm.Imperative, "かけ")]
    [InlineData("書く", "かく", ConjugationForm.Provisional, "かけば")]
    [InlineData("書く", "かく", ConjugationForm.Causative, "かかせる")]
    [InlineData("書く", "かく", ConjugationForm.CausativePassive, "かかせられる")]
    [InlineData("書く", "かく", ConjugationForm.NegativeTe, "かかなくて")]
    [InlineData("書く", "かく", ConjugationForm.Desire, "かきたい")]
    [InlineData("帰る", "かえる", ConjugationForm.NegativePast, "かえらなかった")]
    public void Godan(string written, string reading, ConjugationForm form, string expected)
    {
        Assert.Equal(expected, Reading(written, reading, VerbClass.Godan, form));
    }

    [Theory]
    [InlineData(ConjugationForm.Negative, "たべない")]
    [InlineData(ConjugationForm.Te, "たべて")]
    [InlineData(ConjugationForm.Potential, "たべられる")]
    [InlineData(ConjugationForm.Volitional, "たべよう")]
    [InlineData(ConjugationForm.Imperative, "たべろ")]
    [InlineData(ConjugationForm.Provisional, "たべれば")]
    [InlineData(ConjugationForm.Causative, "たべさせる")]
    [InlineData(ConjugationForm.PoliteVolitional, "たべましょう")]
    public void Ichidan(ConjugationForm form, string expected)
    {
        Assert.Equal(expected, Reading("食べる", "たべる", VerbClass.Ichidan, form));
    }

    [Fact]
    public void Irregular_verbs_and_exceptions()
    {
        Assert.Equal("しない", Reading("する", "する", VerbClass.Suru, ConjugationForm.Negative));
        Assert.Equal("できる", Reading("する", "する", VerbClass.Suru, ConjugationForm.Potential));
        Assert.Equal("べんきょうして", Reading("勉強する", "べんきょうする", VerbClass.Suru, ConjugationForm.Te));
        Assert.Equal("勉強して", Written("勉強する", "べんきょうする", VerbClass.Suru, ConjugationForm.Te));

        Assert.Equal("こない", Reading("来る", "くる", VerbClass.Kuru, ConjugationForm.Negative));
        Assert.Equal("来ない", Written("来る", "くる", VerbClass.Kuru, ConjugationForm.Negative));
        Assert.Equal("きて", Reading("来る", "くる", VerbClass.Kuru, ConjugationForm.Te));
        Assert.Equal("くれば", Reading("来る", "くる", VerbClass.Kuru, ConjugationForm.Provisional));
        Assert.Equal("こい", Reading("来る", "くる", VerbClass.Kuru, ConjugationForm.Imperative));
        Assert.Equal("もってこられる", Reading("持って来る", "もってくる", VerbClass.Kuru, ConjugationForm.Passive));
        Assert.Equal("持って来られる", Written("持って来る", "もってくる", VerbClass.Kuru, ConjugationForm.Passive));

        Assert.Equal("いって", Reading("行く", "いく", VerbClass.GodanIku, ConjugationForm.Te));
        Assert.Equal("行った", Written("行く", "いく", VerbClass.GodanIku, ConjugationForm.Past));
        Assert.Equal("ない", Reading("ある", "ある", VerbClass.GodanAru, ConjugationForm.Negative));
        Assert.Equal("なかった", Reading("ある", "ある", VerbClass.GodanAru, ConjugationForm.NegativePast));
        Assert.Equal("あって", Reading("ある", "ある", VerbClass.GodanAru, ConjugationForm.Te));
        Assert.Null(Conjugator.Conjugate(new Verb("ある", "ある", VerbClass.GodanAru), ConjugationForm.Potential));
        Assert.Equal("いらっしゃいます", Reading("いらっしゃる", "いらっしゃる", VerbClass.GodanHonorific, ConjugationForm.Polite));
        Assert.Equal("ください", Reading("くださる", "くださる", VerbClass.GodanHonorific, ConjugationForm.Imperative));
        Assert.Equal("くれ", Reading("くれる", "くれる", VerbClass.IchidanKureru, ConjugationForm.Imperative));
        Assert.Equal("くれない", Reading("くれる", "くれる", VerbClass.IchidanKureru, ConjugationForm.Negative));
    }

    [Fact]
    public void Answers_in_kana_katakana_romaji_or_kanji_count()
    {
        Bridge.Japanese.Conjugation te = Conjugator.Conjugate(new Verb("書く", "かく", VerbClass.Godan), ConjugationForm.Te)!;
        Assert.True(Conjugator.IsCorrect(te, "かいて"));
        Assert.True(Conjugator.IsCorrect(te, " カイテ "));
        Assert.True(Conjugator.IsCorrect(te, "kaite"));
        Assert.True(Conjugator.IsCorrect(te, "書いて"));
        Assert.False(Conjugator.IsCorrect(te, "かきて"));
        Assert.False(Conjugator.IsCorrect(te, ""));

        // Everyday alternatives are right too
        Bridge.Japanese.Conjugation potential = Conjugator.Conjugate(new Verb("食べる", "たべる", VerbClass.Ichidan), ConjugationForm.Potential)!;
        Assert.True(Conjugator.IsCorrect(potential, "たべれる"));
        Bridge.Japanese.Conjugation causativePassive = Conjugator.Conjugate(new Verb("書く", "かく", VerbClass.Godan), ConjugationForm.CausativePassive)!;
        Assert.True(Conjugator.IsCorrect(causativePassive, "かかされる"));
        Bridge.Japanese.Conjugation negativeTe = Conjugator.Conjugate(new Verb("書く", "かく", VerbClass.Godan), ConjugationForm.NegativeTe)!;
        Assert.True(Conjugator.IsCorrect(negativeTe, "かかないで"));
    }

    [Fact]
    public void Romaji_follows_the_ime()
    {
        Assert.Equal("こんにちは", Kana.FromRomaji("konnichiha"));
        Assert.Equal("かった", Kana.FromRomaji("katta"));
        Assert.Equal("まっちゃ", Kana.FromRomaji("matcha"));
        Assert.Equal("しんぶん", Kana.FromRomaji("shinbun"));
        Assert.Equal("きんえん", Kana.FromRomaji("kin'en"));
        Assert.Equal("ほん", Kana.FromRomaji("honn"));
        Assert.Equal("おんな", Kana.FromRomaji("onna"));
        Assert.Equal("じゃない", Kana.FromRomaji("janai"));
        Assert.Equal("つかう", Kana.FromRomaji("tsukau"));
        Assert.Equal("かいて", Kana.Normalize("ｋａｉｔｅ"));
    }

    [Fact]
    public void Reverse_answers_any_verb_with_the_same_form()
    {
        Verb kau = new("買う", "かう", VerbClass.Godan);
        Assert.True(Conjugator.IsDictionaryFormOf(kau, ConjugationForm.Past, "かう"));
        Assert.True(Conjugator.IsDictionaryFormOf(kau, ConjugationForm.Past, "買う"));
        // かった is also the past of 勝つ
        Assert.True(Conjugator.IsDictionaryFormOf(kau, ConjugationForm.Past, "かつ"));
        Assert.False(Conjugator.IsDictionaryFormOf(kau, ConjugationForm.Past, "かく"));
        Assert.True(Conjugator.IsDictionaryFormOf(new Verb("食べる", "たべる", VerbClass.Ichidan), ConjugationForm.Negative, "taberu"));
    }

    [Fact]
    public void Furigana_lines_up_with_the_kana()
    {
        IReadOnlyList<FuriganaPart> parts = Kana.Furigana("取り消す", "とりけす");
        Assert.Equal([new FuriganaPart("取", "と"), new FuriganaPart("り", null), new FuriganaPart("消", "け"), new FuriganaPart("す", null)], parts);
        Assert.Equal([new FuriganaPart("来", "こ"), new FuriganaPart("ない", null)], Kana.Furigana("来ない", "こない"));
        Assert.Equal([new FuriganaPart("する", null)], Kana.Furigana("する", "する"));
    }

    [Fact]
    public void Rules_group_what_learners_get_wrong()
    {
        Assert.Equal("Te:godan-く", Conjugator.Rule(new Verb("書く", "かく", VerbClass.Godan), ConjugationForm.Te).Key);
        Assert.Equal("く → いて", Conjugator.Rule(new Verb("書く", "かく", VerbClass.Godan), ConjugationForm.Te).Label);
        Assert.Equal(Conjugator.Rule(new Verb("買う", "かう", VerbClass.Godan), ConjugationForm.Te).Key,
            Conjugator.Rule(new Verb("待つ", "まつ", VerbClass.Godan), ConjugationForm.Te).Key);
        Assert.Equal("Negative:godan-う", Conjugator.Rule(new Verb("買う", "かう", VerbClass.Godan), ConjugationForm.Negative).Key);
        Assert.Equal("Negative:godan", Conjugator.Rule(new Verb("書く", "かく", VerbClass.Godan), ConjugationForm.Negative).Key);
        Assert.Equal("Te:iku", Conjugator.Rule(new Verb("行く", "いく", VerbClass.GodanIku), ConjugationForm.Te).Key);
    }

    [Fact]
    public void Status_reads_the_latest_answers()
    {
        Assert.Equal(ConjugationStatus.New, ConjugationProgress.Status(""));
        Assert.Equal(ConjugationStatus.Trouble, ConjugationProgress.Status("1110"));
        Assert.Equal(ConjugationStatus.Learning, ConjugationProgress.Status("01"));
        Assert.Equal(ConjugationStatus.Fine, ConjugationProgress.Status("111"));
        Assert.Equal(ConjugationStatus.Trouble, ConjugationProgress.Status("0010101"));
        Assert.Equal("1111111110", ConjugationProgress.Push("1111111111", false));
        Assert.Equal(ConjugationStatus.Trouble, ConjugationProgress.GroupStatus(["111", "0"]));
        Assert.Equal(ConjugationStatus.Fine, ConjugationProgress.GroupStatus(["111", "111"]));
    }

    [Fact]
    public async Task Answers_are_recorded_per_item_with_the_wrong_answers()
    {
        InMemoryConjugationItemDatabase items = new();
        InMemoryConjugationAttemptDatabase attempts = new();
        InMemoryConjugationPreferencesDatabase preferences = new();
        DictionaryVerb kaku = new("書く", "かく", VerbClass.Godan, "to write", 500);
        ConjugationRequestHandler handler = new(new ConjugationService(new FakeVerbCatalog(kaku), items, attempts, preferences, null),
            NullLogger<ConjugationRequestHandler>.Instance);

        async Task<ConjugationResponseTransmission> Record(string answer, bool correct) =>
            await handler.ProcessAsync(UserId, new ConjugationRequestTransmission
            {
                Action = ConjugationAction.Record, Verb = kaku.Id, Written = "書く", Reading = "かく", Class = VerbClass.Godan,
                Form = ConjugationForm.Te, Answer = answer, Correct = correct, Millis = 2400
            });

        await Record("かきて", false);
        ConjugationResponseTransmission last = await Record("かいて", true);
        Assert.Null(last.Error);
        Assert.Equal(2, last.Item!.Attempts);
        Assert.Equal(1, last.Item.Correct);
        Assert.Equal("01", last.Item.Recent);
        Assert.Equal(["かきて"], last.Item.WrongAnswers);
        Assert.Single(items.Items);
        Assert.Equal(2, attempts.Items.Count());

        ConjugationResponseTransmission loaded = await handler.ProcessAsync(UserId, new ConjugationRequestTransmission { Action = ConjugationAction.Load });
        Assert.Single(loaded.Items);
        Assert.Equal(kaku.Id, Assert.Single(loaded.Verbs).Id);
        ConjugationDayView today = Assert.Single(loaded.Days);
        Assert.Equal((2, 1), (today.Attempts, today.Correct));
        Assert.Equal("かきて", Assert.Single(loaded.Mistakes).Answer);

        await handler.ProcessAsync(UserId, new ConjugationRequestTransmission { Action = ConjugationAction.Reset });
        Assert.Empty(items.Items);
        Assert.Empty(attempts.Items);
    }

    [Fact]
    public async Task Settings_are_kept_and_verbs_from_calls_are_counted()
    {
        DictionaryVerb kaku = new("書く", "かく", VerbClass.Godan, "to write", 500);
        DictionaryVerb sabotte = new("サボる", "さぼる", VerbClass.Godan, "to skip", int.MaxValue);
        CallTokens call = new()
        {
            CallId = "c", Name = "Call", Partner = "Rii", Date = DateTime.UtcNow, CreatedAt = DateTime.UtcNow, HasPartner = true
        };
        call.Tokens.Add(new VocabToken(0, 0, 0, true, new DetailedToken("サボっ", "サボる", "サボッ", WordClass.Verb, "動詞", "自立", "*", "五段・ラ行", "連用タ接続")));
        call.Tokens.Add(new VocabToken(0, 0, 0, false, new DetailedToken("サボっ", "サボる", "サボッ", WordClass.Verb, "動詞", "自立", "*", "五段・ラ行", "連用タ接続")));
        call.Tokens.Add(new VocabToken(0, 0, 0, false, new DetailedToken("サボる", "サボる", "サボル", WordClass.Verb, "動詞", "自立", "*", "五段・ラ行", "基本形")));
        ConjugationService service = new(new FakeVerbCatalog(kaku, sabotte), new InMemoryConjugationItemDatabase(), new InMemoryConjugationAttemptDatabase(),
            new InMemoryConjugationPreferencesDatabase(), new FakeVocabulary(call));

        await service.SaveSettingsAsync(UserId, new ConjugationSettingsView
        {
            Forms = [ConjugationForm.Potential, ConjugationForm.Potential], Pool = ConjugationPool.Both, ShowFurigana = true, CommonLimit = 1
        });
        ConjugationState state = await service.LoadAsync(UserId, 60);
        Assert.Equal([ConjugationForm.Potential], state.Settings.Forms);
        Assert.Equal(ConjugationPool.Both, state.Settings.Pool);
        Assert.True(state.Settings.ShowFurigana);
        Assert.Equal(500, state.Settings.CommonLimit);

        ConjugationVerbView fromCalls = Assert.Single(state.Verbs, v => v.Written == "サボる");
        Assert.Equal((1, 2), (fromCalls.LearnerUses, fromCalls.PartnerUses));
        Assert.Contains(state.Verbs, v => v.Written == "書く");
    }

    private static readonly Lazy<IReadOnlyList<IJapaneseEntry>> Dictionary = new(() => [.. JapaneseDictionary.ParseEntries()]);

    [Fact]
    public void The_verb_list_comes_from_jmdict()
    {
        VerbCatalog catalog = new(() => Dictionary.Value);

        Assert.Equal(VerbClass.Godan, catalog.Find("書く")!.Class);
        Assert.Equal(VerbClass.Ichidan, catalog.Find("食べる")!.Class);
        Assert.Equal(VerbClass.GodanIku, catalog.Find("行く")!.Class);
        Assert.Equal(VerbClass.GodanAru, catalog.Find("ある")!.Class);
        Assert.Equal(VerbClass.Kuru, catalog.Find("来る")!.Class);
        Assert.Equal(VerbClass.Suru, catalog.Find("する")!.Class);
        Assert.Equal(VerbClass.IchidanKureru, catalog.Find("くれる")!.Class);
        Assert.Equal(VerbClass.GodanHonorific, catalog.Find("いらっしゃる")!.Class);
        Assert.Equal("する", catalog.Find("する")!.Written);
        Assert.Contains("write", catalog.Find("書く")!.Meaning);

        IReadOnlyList<DictionaryVerb> common = catalog.Common;
        int top5k = common.Count(v => v.Rank <= 5_000);
        int common12k = common.Count(v => v.Rank <= ConjugationService.MaxCommonRank);
        Console.WriteLine($"JMdict verbs: {common.Count} marked common, {top5k} in the top 5k, {common12k} within {ConjugationService.MaxCommonRank}");
        Assert.InRange(top5k, 100, 3_000);
        Assert.InRange(common12k, top5k, 6_000);
        // Every verb sent to the page can be asked in every form it has
        foreach (DictionaryVerb verb in common.Where(v => v.Rank <= ConjugationService.MaxCommonRank))
        {
            Assert.NotNull(Conjugator.Conjugate(new Verb(verb.Written, verb.Reading, verb.Class), ConjugationForm.Te));
        }
    }
}
