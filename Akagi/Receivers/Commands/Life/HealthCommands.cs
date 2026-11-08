using Akagi.Life;
using Akagi.Life.Health;

namespace Akagi.Receivers.Commands.Life;

internal class LogSleepCommand : LifeCommand, IUserFactCommand
{
    public override string Name => "LogSleep";

    public override string Description => "Record how long the user slept when they tell you (e.g. 'only slept 5 hours').";

    public override Argument[] GetDefaultArguments() =>
    [
        Number("Hours", "Hours of sleep, e.g. 6.5.", required: true),
        Text("WokeUpAt", "Local wake-up time 'HH:mm' (today) or 'yyyy-MM-dd HH:mm'. Defaults to now."),
        Grounding.EvidenceArgument()
    ];

    public override async Task<Command[]> Execute(Context context)
    {
        double hours = GetNumberArgument("Hours") ?? throw new ArgumentException("Hours must be a number.");
        if (hours <= 0 || hours > 24)
        {
            throw new ArgumentException("Hours must be between 0 and 24.");
        }

        DateTime now = DateTime.UtcNow;
        LifeProfile profile = await LoadProfile(context);
        DateTime wokeUp = ParseLocalTime(profile, GetArgument("WokeUpAt"), now) ?? now;
        if (wokeUp > now)
        {
            wokeUp = wokeUp.AddDays(-1);
        }

        HealthDay day = HealthImport.LogSleep(profile, hours, wokeUp, "conversation");
        await SaveProfile(context, profile);

        HealthSummary summary = HealthSummary.Create(profile, now);
        return Reply(context, $"Logged {LifeClock.FormatDuration(TimeSpan.FromHours(hours))} of sleep for {day.Date}." +
            (summary.HasData ? Environment.NewLine + summary.Describe() : string.Empty));
    }
}

internal class LogStepsCommand : LifeCommand, IUserFactCommand
{
    public override string Name => "LogSteps";

    public override string Description => "Record the user's step count for a day.";

    public override Argument[] GetDefaultArguments() =>
    [
        Integer("Steps", "Number of steps.", required: true),
        Text("Date", "Local date 'yyyy-MM-dd'. Defaults to today."),
        Grounding.EvidenceArgument()
    ];

    public override async Task<Command[]> Execute(Context context)
    {
        int steps = GetIntArgument("Steps") ?? throw new ArgumentException("Steps must be a number.");
        LifeProfile profile = await LoadProfile(context);
        string date = GetArgument("Date") ?? profile.LocalDate(DateTime.UtcNow);

        HealthDay day = profile.GetOrAddHealthDay(date);
        day.Steps = Math.Max(0, steps);
        day.Source = "conversation";
        day.UpdatedAt = DateTime.UtcNow;
        await SaveProfile(context, profile);

        return Reply(context, $"Logged {steps:N0} steps for {date}.");
    }
}

internal class HealthStatusCommand : LifeCommand
{
    public override string Name => "HealthStatus";

    public override string Description =>
        "Look up what is already known about the user's health: recent sleep, steps, resting heart rate and the last 7 days " +
        "of workouts (watch sessions with heart rate and zones, and interval timer workouts).";

    public override Argument[] GetDefaultArguments() => [];

    public override async Task<Command[]> Execute(Context context)
    {
        LifeProfile profile = await LoadProfile(context);
        HealthSummary summary = HealthSummary.Create(profile, DateTime.UtcNow);
        string health = summary.HasData ? summary.Describe(withExercise: false) : "No sleep, step or heart rate data recorded yet.";
        return Reply(context, health + Environment.NewLine + await ActivityLookup.DescribeAsync(context, profile));
    }
}
