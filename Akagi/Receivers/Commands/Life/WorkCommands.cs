using Akagi.Life;
using Akagi.Life.Work;

namespace Akagi.Receivers.Commands.Life;

internal class StartWorkCommand : LifeCommand, IUserFactCommand
{
    public override string Name => "StartWork";

    public override string Description =>
        "Start tracking a work session when the user says what they are about to work on. Stops any running session first.";

    public override Argument[] GetDefaultArguments() =>
    [
        Text("Project", "Project name, e.g. 'Akagi' or 'N2 study'.", required: true),
        Text("Task", "What exactly they are doing."),
        Grounding.EvidenceArgument()
    ];

    public override async Task<Command[]> Execute(Context context)
    {
        DateTime now = DateTime.UtcNow;
        LifeProfile profile = await LoadProfile(context);

        string previous = string.Empty;
        WorkSession? stopped = profile.Work.Stop(now, string.Empty);
        if (stopped != null)
        {
            previous = $"Stopped {stopped.Project} after {LifeClock.FormatDuration(stopped.Duration(now))}. ";
            await WorkSync.TrySync(context, profile, stopped);
        }

        WorkSession session = profile.Work.Start(RequireArgument("Project"), GetArgument("Task") ?? string.Empty, now);
        profile.MarkChanged();
        await SaveProfile(context, profile);

        return Reply(context, $"{previous}Started tracking {session.Project}{(session.Task.Length > 0 ? $" ({session.Task})" : string.Empty)} at {profile.ToLocal(now):HH:mm}.");
    }
}

internal class StopWorkCommand : LifeCommand, IUserFactCommand
{
    public override string Name => "StopWork";

    public override string Description => "Stop the running work session when the user is done or switches to something else.";

    public override Argument[] GetDefaultArguments() =>
    [
        Text("Notes", "What got done, in a few words."),
        Grounding.EvidenceArgument()
    ];

    public override async Task<Command[]> Execute(Context context)
    {
        DateTime now = DateTime.UtcNow;
        LifeProfile profile = await LoadProfile(context);
        WorkSession? stopped = profile.Work.Stop(now, GetArgument("Notes") ?? string.Empty);
        if (stopped == null)
        {
            return Reply(context, "No work session is running.");
        }

        string sync = await WorkSync.TrySync(context, profile, stopped);
        profile.MarkChanged();
        await SaveProfile(context, profile);

        WorkSummary summary = WorkSummary.Create(profile, now);
        return Reply(context, $"Stopped {stopped.Project} after {LifeClock.FormatDuration(stopped.Duration(now))}. {sync}".TrimEnd()
            + Environment.NewLine + summary.Describe());
    }
}

internal class TakeBreakCommand : LifeCommand, IUserFactCommand
{
    public override string Name => "TakeBreak";

    public override string Description => "Note that the user took a break, which resets the break reminder for the running session.";

    public override Argument[] GetDefaultArguments() => [Grounding.EvidenceArgument()];

    public override async Task<Command[]> Execute(Context context)
    {
        LifeProfile profile = await LoadProfile(context);
        if (profile.Work.Active == null)
        {
            return Reply(context, "No work session is running.");
        }
        profile.Work.Active.LastBreakUtc = DateTime.UtcNow;
        profile.MarkChanged();
        await SaveProfile(context, profile);
        return Reply(context, "Break noted.");
    }
}

internal class WorkStatusCommand : LifeCommand
{
    public override string Name => "WorkStatus";

    public override string Description => "Look up what the user is working on and how long they worked today and this week.";

    public override Argument[] GetDefaultArguments() => [];

    public override async Task<Command[]> Execute(Context context)
    {
        LifeProfile profile = await LoadProfile(context);
        WorkSummary summary = WorkSummary.Create(profile, DateTime.UtcNow);
        return Reply(context, summary.HasData ? summary.Describe() : "No work tracked in the last 7 days.");
    }
}
