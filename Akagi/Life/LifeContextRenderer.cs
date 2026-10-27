using Akagi.Life.Health;
using Akagi.Life.Mood;
using Akagi.Life.Study;
using Akagi.Life.Work;
using System.Globalization;
using System.Text;

namespace Akagi.Life;

[Flags]
internal enum LifeSections
{
    None = 0,
    Time = 1,
    Mood = 2,
    Concerns = 4,
    Health = 8,
    Work = 16,
    Study = 32,
    Tone = 64,
    All = Time | Mood | Concerns | Health | Work | Study | Tone
}

/// <summary>
/// Renders the companion's awareness of the user's life as a compact text block for the prompt.
/// </summary>
internal static class LifeContextRenderer
{
    public static string Render(LifeProfile profile, DateTime nowUtc, LifeSections sections)
    {
        StringBuilder builder = new();

        MoodSnapshot mood = MoodAnalyzer.Analyze(profile, nowUtc);
        HealthSummary health = HealthSummary.Create(profile, nowUtc);
        WorkSummary work = WorkSummary.Create(profile, nowUtc);
        StudySummary study = StudySummary.Create(profile, nowUtc);

        if (sections.HasFlag(LifeSections.Time))
        {
            DateTime local = profile.ToLocal(nowUtc);
            string zone = string.IsNullOrWhiteSpace(profile.TimeZoneId) ? "UTC" : profile.TimeZoneId;
            builder.AppendLine($"Local time: {local.ToString("dddd yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)} ({zone}, {PartOfDay(local)})");
        }

        if (sections.HasFlag(LifeSections.Mood) && (mood.HasData || mood.RecentVictories.Count > 0))
        {
            if (mood.HasData)
            {
                List<string> dims = [];
                if (mood.Valence.HasValue)
                {
                    dims.Add($"mood {mood.Valence:+0.#;-0.#;0}/5");
                }
                if (mood.Energy.HasValue)
                {
                    dims.Add($"energy {mood.Energy:0.#}/10");
                }
                if (mood.Stress.HasValue)
                {
                    dims.Add($"stress {mood.Stress:0.#}/10");
                }
                string age = mood.LastEntryAt.HasValue ? $"last check-in {LifeClock.FormatAge(nowUtc - mood.LastEntryAt.Value)}" : string.Empty;
                string trend = mood.Trend is MoodTrend.Falling or MoodTrend.Rising ? $", trending {mood.Trend.ToString().ToLowerInvariant()}" : string.Empty;
                string note = string.IsNullOrWhiteSpace(mood.LastNote) ? string.Empty : $" - \"{mood.LastNote}\"";
                builder.AppendLine($"How the user feels: {string.Join(", ", dims)} ({age}{trend}){note}");
            }
            foreach (MoodEntry victory in mood.RecentVictories)
            {
                builder.AppendLine($"Recent win ({LifeClock.FormatAge(nowUtc - victory.Timestamp)}): {victory.Note}");
            }
        }

        if (sections.HasFlag(LifeSections.Concerns) && mood.OpenConcerns.Count > 0)
        {
            builder.AppendLine("On their mind:");
            foreach (Concern concern in mood.OpenConcerns)
            {
                string kind = concern.Kind == MoodKind.Event ? "upcoming" : "worry";
                string followUp = concern.FollowUpAt.HasValue
                    ? concern.IsDueForFollowUp(nowUtc)
                        ? ", follow up now"
                        : $", follow up {profile.ToLocal(concern.FollowUpAt.Value).ToString("ddd yyyy-MM-dd", CultureInfo.InvariantCulture)}"
                    : string.Empty;
                builder.AppendLine($"- [{concern.Id}] {kind}: {concern.Text}{followUp}");
            }
        }

        if (sections.HasFlag(LifeSections.Health) && health.HasData)
        {
            builder.AppendLine(health.Describe());
        }

        if (sections.HasFlag(LifeSections.Work) && work.HasData)
        {
            builder.AppendLine(work.Describe());
        }

        if (sections.HasFlag(LifeSections.Study) && study.HasData)
        {
            builder.AppendLine(study.Describe());
        }

        if (sections.HasFlag(LifeSections.Tone))
        {
            ToneAdvice advice = ToneAdvisor.Advise(mood, health, work, nowUtc);
            if (advice.Guidance.Count > 0)
            {
                builder.AppendLine($"Tone right now: {advice.Tone.ToString().ToLowerInvariant()}");
                foreach (string guidance in advice.Guidance)
                {
                    builder.AppendLine("- " + guidance);
                }
            }
        }

        return builder.ToString().TrimEnd();
    }

    public static string PartOfDay(DateTime local) => local.Hour switch
    {
        < 5 => "late night",
        < 11 => "morning",
        < 14 => "midday",
        < 18 => "afternoon",
        < 22 => "evening",
        _ => "night"
    };
}
