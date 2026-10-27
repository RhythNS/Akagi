using Akagi.Life;
using Akagi.Life.Health;
using Akagi.Life.Mood;
using Akagi.Life.Work;

namespace Akagi.Tests.Life;

public class MoodTests
{
    private static readonly DateTime Now = new(2026, 10, 27, 9, 0, 0, DateTimeKind.Utc);

    private static LifeProfile Profile(params MoodEntry[] entries)
    {
        LifeProfile profile = new() { UserId = "u" };
        foreach (MoodEntry entry in entries)
        {
            profile.AddMood(entry);
        }
        return profile;
    }

    [Fact]
    public void Analyze_WeighsRecentEntriesMore()
    {
        LifeProfile profile = Profile(
            new MoodEntry { Timestamp = Now.AddHours(-48), Valence = 5 },
            new MoodEntry { Timestamp = Now.AddHours(-1), Valence = -3 });

        MoodSnapshot snapshot = MoodAnalyzer.Analyze(profile, Now);

        Assert.NotNull(snapshot.Valence);
        Assert.True(snapshot.Valence < -2, $"Valence was {snapshot.Valence}");
        Assert.Equal(Now.AddHours(-1), snapshot.LastEntryAt);
    }

    [Fact]
    public void Analyze_IgnoresEntriesOlderThanThreeDays()
    {
        LifeProfile profile = Profile(new MoodEntry { Timestamp = Now.AddDays(-4), Valence = 4, Energy = 9 });

        MoodSnapshot snapshot = MoodAnalyzer.Analyze(profile, Now);

        Assert.False(snapshot.HasData);
    }

    [Fact]
    public void Analyze_DetectsFallingTrend()
    {
        LifeProfile profile = Profile(
            new MoodEntry { Timestamp = Now.AddHours(-60), Valence = 3 },
            new MoodEntry { Timestamp = Now.AddHours(-40), Valence = 2 },
            new MoodEntry { Timestamp = Now.AddHours(-3), Valence = -1 });

        Assert.Equal(MoodTrend.Falling, MoodAnalyzer.Analyze(profile, Now).Trend);
    }

    [Fact]
    public void AddMood_KeepsOnlyTheNewestEntries()
    {
        LifeProfile profile = new();
        for (int i = 0; i < LifeProfile.MaxMoodEntries + 20; i++)
        {
            profile.AddMood(new MoodEntry { Timestamp = Now.AddMinutes(i), Note = i.ToString() });
        }

        Assert.Equal(LifeProfile.MaxMoodEntries, profile.Mood.Count);
        Assert.Equal("20", profile.Mood[0].Note);
    }

    [Fact]
    public void ToneAdvisor_PrefersSupportOverCelebration()
    {
        LifeProfile profile = Profile(
            new MoodEntry { Timestamp = Now.AddHours(-2), Valence = -3, Stress = 8 },
            new MoodEntry { Timestamp = Now.AddHours(-5), Kind = MoodKind.Victory, Note = "passed the mock exam" });

        ToneAdvice advice = ToneAdvisor.Advise(MoodAnalyzer.Analyze(profile, Now), null, null, Now);

        Assert.Equal(Tone.Supportive, advice.Tone);
        Assert.Contains(advice.Guidance, g => g.Contains("passed the mock exam"));
        Assert.Contains(advice.Guidance, g => g.Contains("Stress is high"));
    }

    [Fact]
    public void ToneAdvisor_IsGentleAfterShortSleep()
    {
        LifeProfile profile = Profile();
        HealthImport.LogSleep(profile, 4.5, Now.AddHours(-2), "test");

        ToneAdvice advice = ToneAdvisor.Advise(MoodAnalyzer.Analyze(profile, Now), HealthSummary.Create(profile, Now), null, Now);

        Assert.Equal(Tone.Gentle, advice.Tone);
        Assert.Contains(advice.Guidance, g => g.Contains("4h30m"));
    }

    [Fact]
    public void ToneAdvisor_SuggestsBreakAfterLongStretch()
    {
        LifeProfile profile = Profile();
        profile.Work.Start("Akagi", "routines", Now.AddHours(-3));

        ToneAdvice advice = ToneAdvisor.Advise(MoodAnalyzer.Analyze(profile, Now), null, WorkSummary.Create(profile, Now), Now);

        Assert.Equal(Tone.Gentle, advice.Tone);
        Assert.Contains(advice.Guidance, g => g.Contains("without a break"));
    }

    [Fact]
    public void ToneAdvisor_IsNeutralWithoutData()
    {
        ToneAdvice advice = ToneAdvisor.Advise(MoodAnalyzer.Analyze(new LifeProfile(), Now), null, null, Now);

        Assert.Equal(Tone.Neutral, advice.Tone);
        Assert.Empty(advice.Guidance);
    }

    [Fact]
    public void Concern_IsDueOnlyOnceAndWhileOpen()
    {
        Concern concern = new() { FollowUpAt = Now.AddHours(-1) };
        Assert.True(concern.IsDueForFollowUp(Now));

        concern.FollowedUpAt = Now;
        Assert.False(concern.IsDueForFollowUp(Now));

        Assert.False(new Concern { FollowUpAt = Now.AddHours(-1), Resolved = true }.IsDueForFollowUp(Now));
        Assert.False(new Concern { FollowUpAt = Now.AddHours(1) }.IsDueForFollowUp(Now));
    }
}
