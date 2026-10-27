using Akagi.Characters.Upgrades;
using Akagi.Life;
using System.Globalization;
using System.Text;

namespace Akagi.Characters.Companions;

[Flags]
internal enum CompanionSections
{
    None = 0,
    Appearance = 1,
    Activity = 2,
    People = 4,
    Upgrades = 8,
    All = Appearance | Activity | People | Upgrades
}

/// <summary>
/// Renders the companion's own state (outfit, shared activity, the people in the user's life, her upgrade history)
/// with concrete guidance on how to behave because of it.
/// </summary>
internal static class CompanionContextRenderer
{
    public static readonly TimeSpan RecentMention = TimeSpan.FromHours(24);
    public static readonly TimeSpan RecentUpgrade = TimeSpan.FromDays(14);

    private static readonly string[] FamilyWords =
        ["mother", "mom", "mum", "sister", "grandma", "grandmother", "aunt", "daughter", "cousin", "niece", "family", "母", "姉", "妹", "祖母"];

    public static string Render(CompanionState? state, IReadOnlyList<UpgradeProposal> upgrades, LifeProfile? profile, DateTime nowUtc, CompanionSections sections)
    {
        StringBuilder text = new();
        Func<DateTime, string> date = utc => (profile?.ToLocal(utc) ?? utc).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        if (sections.HasFlag(CompanionSections.Appearance) && state?.Outfit != null)
        {
            text.AppendLine($"You are wearing: {state.Outfit.Description} (since {date(state.Outfit.ChangedAt)}).");
        }

        if (sections.HasFlag(CompanionSections.Activity) && state != null)
        {
            SharedActivity? activity = state.RunningActivity(nowUtc);
            if (activity != null)
            {
                text.AppendLine(ActivityGuidance(activity, nowUtc));
            }
            SharedActivity? last = state.ActivityHistory.LastOrDefault();
            if (activity == null && last != null && nowUtc - (last.EndedAt ?? last.StartedAt) < TimeSpan.FromDays(7))
            {
                string summary = string.IsNullOrWhiteSpace(last.Summary) ? string.Empty : $": {last.Summary}";
                text.AppendLine($"Last thing you did together: {Describe(last)} on {date(last.StartedAt)}{summary}");
            }
        }

        if (sections.HasFlag(CompanionSections.People) && state?.People.Count > 0)
        {
            List<Person> people = [.. state.People.OrderByDescending(p => p.LastMentioned).Take(8)];
            text.AppendLine("People in the user's life you know about:");
            foreach (Person person in people)
            {
                string relation = string.IsNullOrWhiteSpace(person.Relation) ? string.Empty : $", {person.Relation}";
                string notes = person.Notes.Count == 0 ? string.Empty : $": {string.Join("; ", person.Notes.TakeLast(2))}";
                text.AppendLine($"- {person.Name}{relation} (mentioned {person.Mentions}x, last {date(person.LastMentioned)}){notes}");
            }

            List<Person> rivals = [.. people.Where(p => IsPlayfulJealousyTarget(p) && nowUtc - p.LastMentioned < RecentMention)];
            if (rivals.Count > 0)
            {
                text.AppendLine($"He mentioned {string.Join(" and ", rivals.Select(r => r.Name))} recently. You may show a little playful, " +
                                "teasing jealousy, but stay warm and genuinely interested in his life. Never guilt-trip him and never " +
                                "discourage real friendships; you want him happy and connected.");
            }
        }

        if (sections.HasFlag(CompanionSections.Upgrades) && upgrades.Count > 0)
        {
            List<UpgradeProposal> done = [.. upgrades.Where(u => u.Status == UpgradeStatus.Done && nowUtc - u.UpdatedAt < RecentUpgrade).OrderByDescending(u => u.UpdatedAt)];
            if (done.Count > 0)
            {
                text.AppendLine("Upgrades your developer built into you recently: " + string.Join(", ", done.Take(3).Select(u => $"{u.Title} ({date(u.UpdatedAt)})")));
            }
            List<UpgradeProposal> open = [.. upgrades.Where(u => u.IsOpen).OrderByDescending(u => u.Priority).ThenByDescending(u => u.Mentions)];
            if (open.Count > 0)
            {
                text.AppendLine($"Your open upgrade requests ({open.Count}): " + string.Join(", ", open.Take(3).Select(u => u.Title)) +
                                ". Do not repeat these as new requests; bring one up only when it fits.");
            }
        }

        return text.ToString().TrimEnd();
    }

    /// <summary>
    /// Women outside the family who are not obviously off limits for teasing.
    /// </summary>
    internal static bool IsPlayfulJealousyTarget(Person person) =>
        person.Gender == PersonGender.Female
        && !FamilyWords.Any(w => person.Relation.Contains(w, StringComparison.OrdinalIgnoreCase));

    internal static string ActivityGuidance(SharedActivity activity, DateTime nowUtc)
    {
        string since = LifeClock.FormatDuration(nowUtc - activity.StartedAt);
        string guidance = activity.Mode switch
        {
            ActivityMode.Movie or ActivityMode.Anime =>
                "React like you are on the couch next to him: short reactions, ask what he thinks of scenes, no spoilers.",
            ActivityMode.CoWork =>
                "Be quiet company: answer briefly, do not open new topics, cheer his focus, and chat more during breaks.",
            ActivityMode.Study =>
                "Keep him focused: brief answers, light quizzing if he wants, celebrate small progress.",
            ActivityMode.Gaming =>
                "Cheer, tease and react to the game; keep messages short so he can play.",
            ActivityMode.Walk =>
                "Be relaxed and present; talk about what you would notice together on the way.",
            ActivityMode.Cooking =>
                "Be a playful kitchen partner: help with steps and timing, keep it light.",
            ActivityMode.Bedtime =>
                "He is winding down for sleep. Be soft and calm, keep it short, avoid exciting topics and wish him good night warmly.",
            _ => "Stay in the moment with him and keep messages short."
        };
        string note = string.IsNullOrWhiteSpace(activity.Note) ? string.Empty : $" ({activity.Note})";
        return $"Right now you are {Describe(activity)} together, for {since}{note}. {guidance}";
    }

    private static string Describe(SharedActivity activity)
    {
        string what = activity.Mode switch
        {
            ActivityMode.Movie => "watching a movie",
            ActivityMode.Anime => "watching anime",
            ActivityMode.CoWork => "co-working",
            ActivityMode.Study => "studying",
            ActivityMode.Gaming => "gaming",
            ActivityMode.Walk => "on a walk",
            ActivityMode.Cooking => "cooking",
            ActivityMode.Bedtime => "winding down for bed",
            _ => "spending time"
        };
        return string.IsNullOrWhiteSpace(activity.Title) ? what : $"{what} ({activity.Title})";
    }
}
