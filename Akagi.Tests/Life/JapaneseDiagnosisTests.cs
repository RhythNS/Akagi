using Akagi.Characters.CharacterBehaviors.MessageCompilers.Injections;
using Akagi.Life.Japanese;
using Akagi.Receivers.Commands.Life;
using Akagi.Tests.Support;

namespace Akagi.Tests.Life;

public class JapaneseAnalyzerTests
{
    [Fact]
    public void Fillers_CountsFillerShapesButNotNormalWords()
    {
        FillerReport report = FillerAnalyzer.Analyze("えーっと、なんか、あの、明日はなんか忙しいかも。なんか疲れた。あの人は優しい。");

        Assert.Equal(1, Count(report, "えーと"));
        Assert.Equal(0, Count(report, "えー"));
        Assert.Equal(3, Count(report, "なんか"));
        Assert.Equal(1, Count(report, "あの"));
        Assert.Equal(1, report.Hesitations - Count(report, "えーと"));
        FillerCount habit = Assert.Single(report.Habits);
        Assert.Equal("なんか", habit.Form);
        Assert.Contains("なんか", habit.Example);
    }

    [Fact]
    public void Fillers_RateIsPerHundredJapaneseCharacters()
    {
        FillerReport report = FillerAnalyzer.Analyze("まあ" + new string('あ', 98) + " hello world");

        Assert.Equal(100, report.JapaneseCharacters);
        Assert.Equal(1.0, report.Per100, 3);
    }

    private static int Count(FillerReport report, string form) => report.Counts.FirstOrDefault(c => c.Form == form)?.Count ?? 0;

    [Theory]
    [InlineData("そうですね。", true)]
    [InlineData("昨日行きましたけど", true)]
    [InlineData("ありがとうございます！", true)]
    [InlineData("行くよ", false)]
    [InlineData("それな〜w", false)]
    [InlineData("楽しかったです笑", true)]
    public void Register_ClassifiesEndings(string sentence, bool polite)
    {
        Assert.Equal(polite, RegisterAnalyzer.IsPolite(sentence));
    }

    [Fact]
    public void Register_FlagsStiffSpeechWithFriends()
    {
        RegisterReport report = RegisterAnalyzer.Analyze("昨日映画を見ました。面白かったです。ユキさんも見ましたか。また行きたいです。今度一緒に行きましょう。うん");

        Assert.Equal(6, report.Sentences);
        Assert.Equal(5, report.Polite);
        Assert.Contains("Too stiff for friends", report.Mismatch(ExpectedRegister.Casual));
        Assert.Null(report.Mismatch(ExpectedRegister.Polite));
        Assert.Contains("No honorific", report.Mismatch(ExpectedRegister.Formal));
    }

    [Fact]
    public void Register_NeedsEnoughSentences()
    {
        Assert.Null(RegisterAnalyzer.Analyze("そうですね。行きます。").Mismatch(ExpectedRegister.Casual));
    }

    [Fact]
    public void Register_CountsKeigo()
    {
        RegisterReport report = RegisterAnalyzer.Analyze("部長がいらっしゃいました。資料を拝見いたしました。");

        Assert.True(report.Keigo >= 3);
    }

    [Fact]
    public void Patterns_DistinguishTheConfusableN2Forms()
    {
        string text = "明日は会議だから休むわけにはいかない。でも熱があるから休まざるを得ないかも。" +
                      "そんなの一日でできっこないよ。このままだと遅刻しかねない。";

        List<string> found = [.. GrammarPatterns.Find(text).Select(m => m.Pattern.Key)];

        Assert.Equal(["～わけにはいかない", "～ざるを得ない", "～っこない", "～かねない"], found);
        Assert.Equal("～わけにはいかない", GrammarPatterns.Lookup("わけにはいかない")!.Key);
        Assert.Contains(GrammarPatterns.DescribeGroups(), g => g.StartsWith("obligation:") && g.Contains("～ざるを得ない"));
    }

    [Fact]
    public void Patterns_KaneruIsNotKanenai()
    {
        Assert.Equal(["～かねる"], GrammarPatterns.Find("その件はお答えしかねます。").Select(m => m.Pattern.Key));
    }

    [Fact]
    public void Structures_FindLeaningOpenersAndEndings()
    {
        string text = string.Join("。",
        [
            "でも、今日は忙しいと思います", "でも、明日は暇", "でも、ユキは来ると思います", "でも、雨かもしれない",
            "週末は映画を見る", "先週は大阪に行った", "ラーメンが好き", "日本語は難しいと思います", "猫がかわいい", "でも、また行きたい"
        ]);

        List<RepeatedStructure> structures = StructureAnalyzer.Analyze(text);

        Assert.Contains(structures, s => s.AtStart && s.Text == "でも" && s.Count == 5);
        Assert.Contains(structures, s => !s.AtStart && s.Text == "と思います" && s.Count == 3);
        Assert.DoesNotContain(structures, s => !s.AtStart && s.Text == "います");
    }

    [Fact]
    public void Structures_IgnoreShortTexts()
    {
        Assert.Empty(StructureAnalyzer.Analyze("でも、行く。でも、来る。でも、寝る。"));
    }
}

public class JapaneseDiagnosisModelTests
{
    private static readonly DateTime Now = new(2026, 10, 27, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void RecordIssue_MergesSamePatternRegardlessOfTildeAndSpacing()
    {
        JapaneseDiagnosis diagnosis = new();

        (DiagnosisIssue first, bool firstNew) = diagnosis.RecordIssue(IssueCategory.GrammarPattern, "～わけにはいかない", "a", new IssueExample { Said = "x" }, 2, Now);
        (DiagnosisIssue second, bool secondNew) = diagnosis.RecordIssue(IssueCategory.GrammarPattern, "わけには いかない", "b", null, 3, Now);

        Assert.True(firstNew);
        Assert.False(secondNew);
        Assert.Same(first, second);
        Assert.Equal(2, first.Count);
        Assert.Equal("b", first.Explanation);
        Assert.Equal(3, first.Severity);
        Assert.Same(first, diagnosis.FindIssue(first.Id));
        Assert.Same(first, diagnosis.FindIssue("わけにはいかない"));
    }

    [Fact]
    public void CorrectUses_MoveIssueToImprovingAndMistakesBack()
    {
        DiagnosisIssue issue = new() { LastSeen = Now };

        issue.RecordCorrect(Now);
        issue.RecordCorrect(Now);
        Assert.Equal(IssueStatus.Active, issue.Status);
        issue.RecordCorrect(Now);
        Assert.Equal(IssueStatus.Improving, issue.Status);

        issue.RecordMistake(null, Now);
        Assert.Equal(IssueStatus.Active, issue.Status);
        Assert.Equal(0, issue.CorrectStreak);
        Assert.Equal(3, issue.TotalCorrect);
    }

    [Fact]
    public void CorrectUses_ResolveOnlyAfterAQuietPeriod()
    {
        DiagnosisIssue issue = new() { LastSeen = Now };
        for (int i = 0; i < DiagnosisIssue.CorrectUsesToResolve; i++)
        {
            issue.RecordCorrect(Now.AddDays(1));
        }
        Assert.Equal(IssueStatus.Improving, issue.Status);

        issue.RecordCorrect(Now.AddDays(30));
        Assert.Equal(IssueStatus.Resolved, issue.Status);
    }

    [Fact]
    public void Focus_PrefersSevereRecentFrequentIssues()
    {
        JapaneseDiagnosis diagnosis = new();
        diagnosis.RecordIssue(IssueCategory.Particle, "old particle slip", "", null, 3, Now.AddDays(-90));
        diagnosis.RecordIssue(IssueCategory.CrutchWord, "なんか", "", null, 1, Now);
        for (int i = 0; i < 4; i++)
        {
            diagnosis.RecordIssue(IssueCategory.Register, "too polite with friends", "", null, 2, Now);
        }

        List<DiagnosisIssue> focus = diagnosis.Focus(2, Now);

        Assert.Equal(["too polite with friends", "なんか"], focus.Select(i => i.Pattern));
    }

    [Fact]
    public void Apply_FoldsLocalFindingsIntoTheDiagnosis()
    {
        JapaneseDiagnosis diagnosis = new();
        string call = string.Concat(Enumerable.Repeat("なんか、今日は仕事でした。でも、楽しかったです。休むわけにはいかないです。", 8));
        SpeechReport report = SpeechAnalysis.Analyze(call, ExpectedRegister.Casual);

        int recorded = SpeechAnalysis.Apply(diagnosis, report, "call with Yuki", Now);

        Assert.True(recorded >= 2);
        Assert.Contains(diagnosis.Issues, i => i.Category == IssueCategory.CrutchWord && i.Pattern == "なんか");
        Assert.Contains(diagnosis.Issues, i => i.Category == IssueCategory.Register && i.Pattern == "too polite with friends");
        Assert.Equal(8, diagnosis.PatternUsage.Single(u => u.Pattern == "～わけにはいかない").Count);
        SpeechSnapshot snapshot = Assert.Single(diagnosis.Snapshots);
        Assert.Equal("call with Yuki", snapshot.Source);
        Assert.Contains("fillers per 100", diagnosis.Describe(Now, 5, withExamples: true));
    }

    [Fact]
    public void Injection_RendersDiagnosisAndGrammarGroups()
    {
        JapaneseDiagnosis diagnosis = new();
        diagnosis.RecordIssue(IssueCategory.Particle, "に vs で", "location of an action takes で", new IssueExample { Said = "公園に遊ぶ", Better = "公園で遊ぶ" }, 2, Now);

        string text = JapaneseDiagnosisInjectionCompiler.Render(diagnosis, Now, 5, includeExamples: true, includeGrammarGroups: true);

        Assert.Contains("'公園に遊ぶ' -> '公園で遊ぶ'", text);
        Assert.Contains("N2 nuance groups", text);
        Assert.Equal(string.Empty, JapaneseDiagnosisInjectionCompiler.Render(null, Now, 5, true, false));
    }
}

public class JapaneseCommandTests
{
    [Fact]
    public async Task RecordIssue_NormalizesKnownGrammarAndMerges()
    {
        LifeTestContext test = new();

        string first = await test.Run<RecordJapaneseIssueCommand>(("Category", "grammar"), ("Pattern", "ざるを得ない"),
            ("Said", "行くざるを得ない"), ("Better", "行かざるを得ない"), ("Explanation", "attaches to the ない stem"));
        string again = await test.Run<RecordJapaneseIssueCommand>(("Category", "GrammarPattern"), ("Pattern", "～ざるを得ない"), ("Severity", 3));

        DiagnosisIssue issue = Assert.Single(test.Diagnoses.Items.Single().Issues);
        Assert.Equal("～ざるを得ない", issue.Pattern);
        Assert.Equal(2, issue.Count);
        Assert.Contains("New issue", first);
        Assert.Contains("seen again", again);
    }

    [Fact]
    public async Task RecordIssue_RejectsUnknownCategory()
    {
        LifeTestContext test = new();

        Assert.Contains("Unknown category", await test.Run<RecordJapaneseIssueCommand>(("Category", "vibes"), ("Pattern", "x")));
    }

    [Fact]
    public async Task CorrectUsage_ReportsProgress()
    {
        LifeTestContext test = new();
        await test.Run<RecordJapaneseIssueCommand>(("Category", "particle"), ("Pattern", "に vs で"));

        await test.Run<RecordCorrectUsageCommand>(("Issue", "に vs で"));
        await test.Run<RecordCorrectUsageCommand>(("Issue", "に vs で"));
        string third = await test.Run<RecordCorrectUsageCommand>(("Issue", "に vs で"), ("Example", "公園で遊んだ"));

        Assert.Contains("now improving", third);
        Assert.Contains("No issue matches", await test.Run<RecordCorrectUsageCommand>(("Issue", "keigo")));
    }

    [Fact]
    public async Task AnalyzeJapanese_RecordsOnlyWhenAsked()
    {
        LifeTestContext test = new();
        string text = string.Concat(Enumerable.Repeat("なんか、昨日は楽しかったです。なんか、また行きたいです。", 4));

        string measured = await test.Run<AnalyzeJapaneseCommand>(("Text", text));
        Assert.Contains("Habit words: なんか", measured);
        Assert.Contains("Too stiff for friends", measured);
        Assert.True(test.Diagnoses.Items.All(d => !d.HasData));

        string recorded = await test.Run<AnalyzeJapaneseCommand>(("Text", text), ("Record", true), ("Source", "voice note"));
        Assert.Contains("Recorded", recorded);
        Assert.Contains(test.Diagnoses.Items.Single().Issues, i => i.Pattern == "なんか");
        Assert.Contains("Register must be", await test.Run<AnalyzeJapaneseCommand>(("Text", text), ("Register", "pirate")));
    }

    [Fact]
    public async Task Report_ShowsFocusStrengthsAndCategoryFilter()
    {
        LifeTestContext test = new();
        Assert.Contains("No diagnosis yet", await test.Run<JapaneseDiagnosisReportCommand>());

        await test.Run<RecordJapaneseIssueCommand>(("Category", "collocation"), ("Pattern", "薬を食べる"), ("Better", "薬を飲む"));
        await test.Run<RecordJapaneseStrengthCommand>(("Strength", "natural aizuchi"));

        string report = await test.Run<JapaneseDiagnosisReportCommand>();
        Assert.Contains("Collocation: 薬を食べる", report);
        Assert.Contains("Strengths: natural aizuchi", report);
        Assert.Contains("No Particle issues", await test.Run<JapaneseDiagnosisReportCommand>(("Category", "particle")));
    }
}
