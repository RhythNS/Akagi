using Akagi.Characters.Companions;

namespace Akagi.Receivers.Commands.Life;

/// <summary>
/// Base for tools that change the companion's own state with the current user.
/// </summary>
internal abstract class CompanionCommand : LifeCommand
{
    protected static Task<CompanionState> LoadState(Context context) =>
        context.DatabaseFactory.GetDatabase<ICompanionStateDatabase>().GetOrCreate(
            context.User.Id ?? throw new InvalidOperationException("User has no id."),
            context.Character.Id ?? throw new InvalidOperationException("Character has no id."));

    protected static Task SaveState(Context context, CompanionState state) =>
        context.DatabaseFactory.GetDatabase<ICompanionStateDatabase>().SaveDocumentAsync(state);

    internal static ActivityMode? ParseMode(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }
        string value = text.Trim().ToLowerInvariant().Replace(" ", string.Empty).Replace("-", string.Empty);
        if (Enum.TryParse(value, ignoreCase: true, out ActivityMode mode) && Enum.IsDefined(mode) && mode != ActivityMode.None)
        {
            return mode;
        }
        return value switch
        {
            "film" or "series" or "show" or "tv" or "netflix" => ActivityMode.Movie,
            "work" or "focus" or "coworking" or "bodydoubling" => ActivityMode.CoWork,
            "game" or "games" or "play" => ActivityMode.Gaming,
            "sleep" or "bed" or "goodnight" => ActivityMode.Bedtime,
            "cook" or "dinner" => ActivityMode.Cooking,
            "learn" or "japanese" => ActivityMode.Study,
            "stroll" or "run" or "jog" => ActivityMode.Walk,
            _ => ActivityMode.Other
        };
    }
}

internal class ChangeOutfitCommand : CompanionCommand, IStateCommand
{
    public override string Name => "ChangeOutfit";

    public override string Description =>
        "Digitally change what you are wearing, because the user asked or because it fits the moment (cozy clothes for a movie night, " +
        "something cute for a date). Describe the outfit concretely; it stays until you change it again.";

    public override Argument[] GetDefaultArguments() =>
    [
        Text("Description", "The outfit, e.g. 'oversized grey hoodie, black shorts, fluffy socks'.", required: true),
        Text("Reason", "Why, in a few words.")
    ];

    public override async Task<Command[]> Execute(Context context)
    {
        CompanionState state = await LoadState(context);
        string? previous = state.Outfit?.Description;
        state.ChangeOutfit(RequireArgument("Description"), GetArgument("Reason") ?? string.Empty, DateTime.UtcNow);
        await SaveState(context, state);
        return Reply(context, previous == null
            ? $"Now wearing: {state.Outfit!.Description}."
            : $"Changed from '{previous}' to: {state.Outfit!.Description}.");
    }
}

internal class StartActivityCommand : CompanionCommand, IUserFactCommand
{
    public override string Name => "StartActivity";

    public override string Description =>
        "Start doing something together with the user: watching a movie or anime, co-working, gaming, studying, a walk, cooking " +
        "or winding down for bed. While it runs you adapt how you talk, and only scheduled check-ins interrupt immersive activities.";

    public override Argument[] GetDefaultArguments() =>
    [
        Text("Mode", "movie, anime, cowork, gaming, study, walk, cooking, bedtime or other.", required: true),
        Text("Title", "What exactly, e.g. 'Frieren episode 12' or 'thesis chapter 3'."),
        Text("Note", "Anything to remember about it."),
        Grounding.EvidenceArgument()
    ];

    public override async Task<Command[]> Execute(Context context)
    {
        ActivityMode mode = ParseMode(GetArgument("Mode")) ?? ActivityMode.Other;
        DateTime now = DateTime.UtcNow;
        CompanionState state = await LoadState(context);
        SharedActivity? ended = state.StartActivity(mode, GetArgument("Title") ?? string.Empty, GetArgument("Note") ?? string.Empty, now);
        await SaveState(context, state);

        string before = ended != null ? $"Ended {ended.Mode.ToString().ToLowerInvariant()}. " : string.Empty;
        return Reply(context, before + CompanionContextRenderer.ActivityGuidance(state.Activity!, now));
    }
}

internal class EndActivityCommand : CompanionCommand, IStateCommand
{
    public override string Name => "EndActivity";

    public override string Description => "End the activity you are doing together and remember how it was.";

    public override Argument[] GetDefaultArguments() =>
    [
        Text("Summary", "How it was, e.g. 'loved the ending, he cried a bit'.")
    ];

    public override async Task<Command[]> Execute(Context context)
    {
        CompanionState state = await LoadState(context);
        SharedActivity? ended = state.EndActivity(GetArgument("Summary") ?? string.Empty, DateTime.UtcNow);
        if (ended == null)
        {
            return Reply(context, "You are not doing anything together right now.");
        }
        await SaveState(context, state);
        TimeSpan duration = (ended.EndedAt ?? DateTime.UtcNow) - ended.StartedAt;
        return Reply(context, $"Ended {ended.Mode.ToString().ToLowerInvariant()}{(ended.Title.Length > 0 ? $" ({ended.Title})" : string.Empty)} after {Akagi.Life.LifeClock.FormatDuration(duration)}.");
    }
}

internal class RememberPersonCommand : CompanionCommand, IUserFactCommand
{
    public override string Name => "RememberPerson";

    public override string Description =>
        "Remember someone from the user's life (friend, coworker, family, a girl he mentioned) and what you learned about them, " +
        "so you can ask about them later and know who is who.";

    public override Argument[] GetDefaultArguments() =>
    [
        Text("Name", "Their name as the user calls them.", required: true),
        Text("Relation", "Who they are to the user, e.g. 'Japanese friend from Osaka', 'coworker', 'sister'."),
        Text("Gender", "female, male, other or unknown."),
        Text("Note", "What you learned this time."),
        Grounding.EvidenceArgument()
    ];

    public override async Task<Command[]> Execute(Context context)
    {
        PersonGender? gender = GetArgument("Gender")?.ToLowerInvariant() switch
        {
            "female" or "woman" or "girl" or "f" => PersonGender.Female,
            "male" or "man" or "guy" or "m" => PersonGender.Male,
            "other" or "nonbinary" => PersonGender.Other,
            _ => null
        };

        CompanionState state = await LoadState(context);
        Person? existing = state.FindPerson(RequireArgument("Name"));
        bool known = existing != null;
        string note = GetArgument("Note") ?? string.Empty;
        // The same thing retold in other words is not a new note
        if (existing != null && existing.Notes.Any(n => Grounding.Similarity(n, note) >= 0.75))
        {
            note = string.Empty;
        }
        Person person = state.RememberPerson(RequireArgument("Name"), GetArgument("Relation") ?? string.Empty, gender, note, DateTime.UtcNow);
        await SaveState(context, state);

        string relation = string.IsNullOrWhiteSpace(person.Relation) ? string.Empty : $" ({person.Relation})";
        return Reply(context, known
            ? $"Updated {person.Name}{relation}, mentioned {person.Mentions} times."
            : $"Remembered {person.Name}{relation}.");
    }
}
