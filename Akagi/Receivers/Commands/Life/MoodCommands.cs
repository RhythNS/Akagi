using Akagi.Life;
using Akagi.Life.Mood;

namespace Akagi.Receivers.Commands.Life;

internal class RecordMoodCommand : LifeCommand
{
    public override string Name => "RecordMood";

    public override string Description =>
        "Record how the user feels, a win they had, or something they are worried about or looking forward to. " +
        "Use it whenever the user shares feelings, stress, tiredness, good news or upcoming events, so you remember later. " +
        "Worries and events can get a follow-up time so you check back on them.";

    public override Argument[] GetDefaultArguments() =>
    [
        Text("Kind", "checkin (current feelings), victory (a win or good news), worry (something weighing on them) or event (something upcoming)."),
        Integer("Valence", "Overall mood from -5 (awful) to 5 (great)."),
        Integer("Energy", "Energy from 0 (exhausted) to 10 (full of energy)."),
        Integer("Stress", "Stress from 0 (relaxed) to 10 (overwhelmed)."),
        Text("Note", "Short description in your own words, e.g. 'nervous about the N2 mock exam'.", required: true),
        Text("FollowUpAt", "For worries and events: local time to check back, 'yyyy-MM-dd HH:mm' or 'yyyy-MM-dd'."),
        Number("FollowUpInHours", "For worries and events: check back after this many hours (alternative to FollowUpAt).")
    ];

    public override async Task<Command[]> Execute(Context context)
    {
        DateTime now = DateTime.UtcNow;
        string note = RequireArgument("Note");
        MoodKind kind = ParseKind(GetArgument("Kind"));

        LifeProfile profile = await LoadProfile(context);
        MoodEntry entry = new()
        {
            Timestamp = now,
            Kind = kind,
            Valence = MoodEntry.ClampValence(GetIntArgument("Valence")),
            Energy = MoodEntry.ClampLevel(GetIntArgument("Energy")),
            Stress = MoodEntry.ClampLevel(GetIntArgument("Stress")),
            Note = note,
            CharacterId = context.Character.Id
        };
        profile.AddMood(entry);

        string output = $"Recorded {kind.ToString().ToLowerInvariant()}: {note}";

        if (kind is MoodKind.Worry or MoodKind.Event)
        {
            DateTime? followUp = ParseLocalTime(profile, GetArgument("FollowUpAt"), now);
            double? hours = GetNumberArgument("FollowUpInHours");
            if (followUp == null && hours is > 0)
            {
                followUp = now.AddHours(hours.Value);
            }

            Concern concern = profile.AddConcern(new Concern
            {
                Kind = kind,
                Text = note,
                CreatedAt = now,
                FollowUpAt = followUp
            });
            output += followUp.HasValue
                ? $" (concern {concern.Id}, follow up {profile.ToLocal(followUp.Value):yyyy-MM-dd HH:mm})"
                : $" (concern {concern.Id})";
        }

        await SaveProfile(context, profile);
        return Reply(context, output);
    }

    internal static MoodKind ParseKind(string? kind)
    {
        return kind?.Trim().ToLowerInvariant() switch
        {
            "victory" or "win" or "success" => MoodKind.Victory,
            "worry" or "concern" or "stress" => MoodKind.Worry,
            "event" or "upcoming" or "plan" => MoodKind.Event,
            _ => MoodKind.CheckIn
        };
    }
}

internal class ResolveConcernCommand : LifeCommand
{
    public override string Name => "ResolveConcern";

    public override string Description =>
        "Close a worry or upcoming event once you know how it turned out, or mark that you followed up on it.";

    public override Argument[] GetDefaultArguments() =>
    [
        Text("Concern", "The concern id shown in brackets, or words from its text.", required: true),
        Text("Outcome", "How it turned out, in a few words."),
        Flag("StillOpen", "True if you only followed up and it is not settled yet.")
    ];

    public override async Task<Command[]> Execute(Context context)
    {
        DateTime now = DateTime.UtcNow;
        LifeProfile profile = await LoadProfile(context);
        Concern? concern = profile.FindConcern(RequireArgument("Concern"));
        if (concern == null)
        {
            return Reply(context, $"No open concern matches '{GetArgument("Concern")}'.");
        }

        string outcome = GetArgument("Outcome") ?? string.Empty;
        concern.FollowedUpAt = now;
        if (GetBoolArgument("StillOpen") != true)
        {
            concern.Resolved = true;
            concern.Outcome = outcome;
        }
        profile.MarkChanged();

        if (!string.IsNullOrEmpty(outcome))
        {
            profile.AddMood(new MoodEntry
            {
                Timestamp = now,
                Kind = MoodKind.CheckIn,
                Note = $"{concern.Text}: {outcome}",
                CharacterId = context.Character.Id
            });
        }

        await SaveProfile(context, profile);
        return Reply(context, concern.Resolved
            ? $"Closed concern {concern.Id} ({concern.Text}){(outcome.Length > 0 ? ": " + outcome : string.Empty)}"
            : $"Followed up on concern {concern.Id}; it stays open.");
    }
}

internal class SetTimeZoneCommand : LifeCommand
{
    public override string Name => "SetTimeZone";

    public override string Description => "Set the user's time zone so greetings, days and quiet hours use their local time.";

    public override Argument[] GetDefaultArguments() =>
    [
        Text("TimeZone", "IANA time zone id such as 'Europe/Berlin' or 'Asia/Tokyo'.", required: true)
    ];

    public override async Task<Command[]> Execute(Context context)
    {
        string zone = RequireArgument("TimeZone");
        if (!LifeClock.IsValidTimeZone(zone))
        {
            return Reply(context, $"Unknown time zone '{zone}'. Use an IANA id like 'Europe/Berlin'.");
        }

        LifeProfile profile = await LoadProfile(context);
        profile.TimeZoneId = zone;
        await SaveProfile(context, profile);
        return Reply(context, $"Time zone set to {zone}. Local time is now {profile.ToLocal(DateTime.UtcNow):yyyy-MM-dd HH:mm}.");
    }
}
