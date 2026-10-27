using Akagi.Life.Health;
using Akagi.Life.Work;

namespace Akagi.Life.Mood;

internal enum Tone
{
    Neutral,
    Encouraging,
    Celebratory,
    Gentle,
    Calm,
    Supportive
}

internal record ToneAdvice(Tone Tone, IReadOnlyList<string> Guidance);

/// <summary>
/// Turns what the companion knows about the user's state into concrete guidance on how to talk right now.
/// Deterministic on purpose: the LLM gets clear instructions instead of having to infer tone from raw numbers.
/// </summary>
internal static class ToneAdvisor
{
    public const double LowValence = -2;
    public const double HighValence = 3;
    public const double LowEnergy = 3;
    public const double HighStress = 7;
    public static readonly TimeSpan LongWorkDay = TimeSpan.FromHours(9);
    public static readonly TimeSpan LongStretch = TimeSpan.FromMinutes(120);

    public static ToneAdvice Advise(MoodSnapshot mood, HealthSummary? health, WorkSummary? work, DateTime nowUtc)
    {
        List<(Tone Tone, string Text)> advice = [];

        if (mood.Valence <= LowValence)
        {
            advice.Add((Tone.Supportive, "The user has been feeling down. Lead with empathy and ask how they are before bringing up tasks or plans."));
        }
        if (mood.Trend == MoodTrend.Falling)
        {
            advice.Add((Tone.Supportive, "Their mood has been sliding over the last days. Check in gently and notice small things that might help."));
        }
        if (mood.Stress >= HighStress)
        {
            advice.Add((Tone.Calm, "Stress is high. Keep a calm, grounding tone, acknowledge the pressure first and offer one small next step at most."));
        }
        if (mood.Energy <= LowEnergy || health?.SleptShortLastNight == true)
        {
            string reason = health?.SleptShortLastNight == true && health.LastSleep?.SleepMinutes is int minutes
                ? $"They slept only {LifeClock.FormatDuration(TimeSpan.FromMinutes(minutes))} last night."
                : "Their energy is low.";
            advice.Add((Tone.Gentle, $"{reason} Be gentle, keep messages short and do not pile on tasks; suggest rest where it fits."));
        }
        if (work != null && work.Today >= LongWorkDay)
        {
            advice.Add((Tone.Gentle, $"They have already worked {LifeClock.FormatDuration(work.Today)} today. Encourage wrapping up and resting."));
        }
        else if (work?.Active != null && work.SinceBreak >= LongStretch)
        {
            advice.Add((Tone.Gentle, $"They have been working for {LifeClock.FormatDuration(work.SinceBreak)} without a break. A short stretch or drink of water would be good."));
        }

        MoodEntry? freshVictory = mood.RecentVictories.FirstOrDefault(v => nowUtc - v.Timestamp <= TimeSpan.FromHours(36));
        if (freshVictory != null)
        {
            advice.Add((Tone.Celebratory, $"Celebrate their recent win ({freshVictory.Note}) warmly and specifically."));
        }
        else if (mood.Valence >= HighValence)
        {
            advice.Add((Tone.Encouraging, "They are in a good mood. Match their energy and be playful."));
        }

        if (advice.Count == 0)
        {
            return new ToneAdvice(Tone.Neutral, []);
        }

        // The most protective tone wins; celebration still comes through as guidance when it fits
        Tone tone = advice.Select(a => a.Tone).OrderByDescending(Priority).First();
        return new ToneAdvice(tone, [.. advice.Select(a => a.Text).Distinct()]);
    }

    private static int Priority(Tone tone) => tone switch
    {
        Tone.Supportive => 5,
        Tone.Calm => 4,
        Tone.Gentle => 3,
        Tone.Celebratory => 2,
        Tone.Encouraging => 1,
        _ => 0
    };
}
