using Akagi.Life;
using Akagi.Life.Health;
using Akagi.Life.Study;
using Akagi.Life.Work;

namespace Akagi.Tests.Life;

public class HealthTests
{
    private static readonly DateTime Now = new(2026, 10, 27, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Import_AssignsSleepToWakeUpDayInLocalTime()
    {
        LifeProfile profile = new() { TimeZoneId = "Asia/Tokyo" };
        string json = """
        {
          "sleep": [ { "start": "2026-10-26T14:00:00Z", "end": "2026-10-26T20:30:00Z" } ],
          "steps": [ { "date": "2026-10-26", "count": 8000 } ],
          "restingHeartRate": [ { "date": "2026-10-27", "bpm": 57 } ]
        }
        """;

        HealthImport.ImportResult result = HealthImport.Import(profile, json);

        Assert.Equal(1, result.SleepDays);
        // 20:30 UTC is 05:30 on the 27th in Tokyo
        HealthDay day = Assert.Single(profile.HealthDays, d => d.Date == "2026-10-27");
        Assert.Equal(390, day.SleepMinutes);
        Assert.Equal(57, day.RestingHeartRate);
        Assert.Equal(8000, profile.HealthDays.Single(d => d.Date == "2026-10-26").Steps);
    }

    [Fact]
    public void Import_AcceptsHealthConnectRecordNamesAndSumsSessions()
    {
        LifeProfile profile = new();
        string json = """
        {
          "SleepSessionRecord": [
            { "startTime": "2026-10-26T23:00:00Z", "endTime": "2026-10-27T03:00:00Z" },
            { "startTime": "2026-10-27T03:30:00Z", "endTime": "2026-10-27T06:00:00Z" }
          ],
          "StepsRecord": [
            { "startTime": "2026-10-27T07:00:00Z", "endTime": "2026-10-27T08:00:00Z", "count": 1200 },
            { "startTime": "2026-10-27T12:00:00Z", "endTime": "2026-10-27T13:00:00Z", "count": "300" },
            { "count": 5 }
          ]
        }
        """;

        HealthImport.ImportResult result = HealthImport.Import(profile, json);

        HealthDay day = profile.HealthDays.Single(d => d.Date == "2026-10-27");
        Assert.Equal(390, day.SleepMinutes);
        Assert.Equal(1500, day.Steps);
        Assert.Single(result.Warnings);
    }

    [Fact]
    public void Import_IsIdempotent()
    {
        LifeProfile profile = new();
        string json = """{ "steps": [ { "date": "2026-10-27", "count": 1000 } ] }""";

        HealthImport.Import(profile, json);
        HealthImport.Import(profile, json);

        Assert.Equal(1000, profile.HealthDays.Single().Steps);
    }

    [Fact]
    public void Summary_ComputesAveragesDebtAndShortNight()
    {
        LifeProfile profile = new();
        for (int i = 1; i <= 3; i++)
        {
            HealthImport.LogSleep(profile, 8, Now.AddDays(-i), "test");
        }
        HealthImport.LogSleep(profile, 5, Now.AddHours(-1), "test");

        HealthSummary summary = HealthSummary.Create(profile, Now);

        Assert.True(summary.SleptShortLastNight);
        Assert.Equal(7.25 * 60, summary.AverageSleepMinutes);
        Assert.Equal(150, summary.SleepDebtMinutes);
        Assert.Contains("short night", summary.Describe());
    }
}

public class WorkTests
{
    private static readonly DateTime Now = new(2026, 10, 27, 15, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Summary_SplitsSessionsAtLocalMidnight()
    {
        LifeProfile profile = new();
        profile.Work.Start("Akagi", "late night", Now.Date.AddHours(-1));
        profile.Work.Stop(Now.Date.AddHours(2), "done");
        profile.Work.Start("N2", "reading", Now.AddMinutes(-30));

        WorkSummary summary = WorkSummary.Create(profile, Now);

        Assert.Equal(TimeSpan.FromHours(2.5), summary.Today);
        Assert.Equal(TimeSpan.FromHours(3.5), summary.Week);
        Assert.Equal("Akagi", summary.TodayByProject[0].Project);
        Assert.Equal(TimeSpan.FromMinutes(30), summary.ActiveDuration);
        Assert.Contains("Currently working on N2", summary.Describe());
    }

    [Fact]
    public void Stop_WithoutActiveSession_ReturnsNull()
    {
        Assert.Null(new WorkLog().Stop(Now, "x"));
    }
}

public class StudyTests
{
    private static readonly DateTime Now = new(2026, 10, 27, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Leitner_MovesUpOnSuccessAndResetsOnMistake()
    {
        StudyProfile study = new();
        study.AddVocab("影響", "えいきょう", "influence", Now, out VocabItem item);
        Assert.False(item.IsDue(Now));
        Assert.True(item.IsDue(Now.AddDays(1)));

        item.Review(true, Now);
        item.Review(true, Now);
        item.Review(true, Now);
        Assert.Equal(4, item.Box);
        Assert.True(item.IsKnown);
        Assert.Equal(Now.AddDays(14), item.NextReviewAt);

        item.Review(false, Now);
        Assert.Equal(1, item.Box);
        Assert.Equal(3, item.Correct);
        Assert.Equal(4, item.Seen);
    }

    [Fact]
    public void AddVocab_UpdatesExistingByTermOrReading()
    {
        StudyProfile study = new();
        Assert.True(study.AddVocab("影響", "えいきょう", "", Now, out _));
        Assert.False(study.AddVocab("えいきょう", "", "influence", Now, out VocabItem item));

        Assert.Single(study.Vocabulary);
        Assert.Equal("influence", item.Meaning);
    }

    [Fact]
    public void Streak_SurvivesUntilEndOfToday()
    {
        LifeProfile profile = new();
        foreach (int daysAgo in new[] { 1, 2, 3, 5 })
        {
            profile.Study.GetDay(LifeClock.DateKey(Now.AddDays(-daysAgo))).Minutes = 15;
        }

        StudySummary before = StudySummary.Create(profile, Now);
        Assert.Equal(3, before.Streak);
        Assert.False(before.StudiedToday);

        profile.Study.GetDay(LifeClock.DateKey(Now)).Reviews = 4;
        StudySummary after = StudySummary.Create(profile, Now);
        Assert.Equal(4, after.Streak);
        Assert.True(after.StudiedToday);
    }

    [Fact]
    public void RecordMistake_CountsRecurringPatterns()
    {
        StudyProfile study = new();
        study.RecordMistake("〜わけではない", "used with noun without な", "学生わけではない", "学生なわけではない", Now);
        GrammarMistake mistake = study.RecordMistake("〜わけではない", "", "", "", Now.AddDays(1));

        Assert.Equal(2, mistake.Count);
        Assert.Equal("学生わけではない", mistake.LastExample);
    }
}

public class ReadingCheckerTests
{
    [Fact]
    public void IdenticalReadings_ArePerfect_KatakanaIsNormalized()
    {
        ReadingChecker.Result result = ReadingChecker.Check("コーヒー", "こーひー");

        Assert.True(result.Perfect);
        Assert.Equal(1.0, result.Score);
    }

    [Theory]
    [InlineData("がっこう", "がこう", "Geminate")]
    [InlineData("おばあさん", "おばさん", "VowelLength")]
    [InlineData("びょういん", "びよういん", "Contraction")]
    [InlineData("かぎ", "かき", "Voicing")]
    [InlineData("きって", "きて", "Geminate")]
    [InlineData("えいご", "えご", "VowelLength")]
    [InlineData("さかな", "さなか", "Different")]
    public void Slips_AreClassified(string expected, string heard, string kind)
    {
        ReadingChecker.Result result = ReadingChecker.Check(expected, heard);

        Assert.Contains(result.Issues, i => i.Kind.ToString() == kind);
        Assert.True(result.Score < 1);
    }

    [Fact]
    public void SplitMorae_KeepsContractedSoundsTogether()
    {
        Assert.Equal(["きょ", "う", "と"], ReadingChecker.SplitMorae("きょうと"));
    }

    [Fact]
    public void Punctuation_AndSpaces_AreIgnored()
    {
        Assert.True(ReadingChecker.Check("おはよう、ございます。", "おはよう ございます").Perfect);
    }
}
