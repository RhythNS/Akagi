using Akagi.Bridge.Attributes;
using Akagi.Characters.Routines;
using Akagi.Life;
using System.Globalization;
using System.Reflection;
using System.Text;

namespace Akagi.Receivers.Commands.Life;

/// <summary>
/// Base for the tools a character uses to manage its own routines: the ones from its graph and the ones it scheduled itself.
/// </summary>
internal abstract class RoutineCommand : LifeCommand
{
    public const int MaxSelfMadeRoutines = 25;

    protected static IRoutineDatabase Routines(Context context) => context.DatabaseFactory.GetDatabase<IRoutineDatabase>();

    protected static Task<List<Routine>> LoadRoutines(Context context) => RoutineRunner.LoadRoutines(Routines(context), context.Character);

    /// <summary>
    /// Finds a routine by short id, full id or name. Returns an error text when nothing or several routines match.
    /// </summary>
    internal static (Routine? Routine, string? Error) Find(IReadOnlyList<Routine> routines, string key)
    {
        string trimmed = key.Trim().TrimStart('[', '#').TrimEnd(']');
        Routine? byId = routines.FirstOrDefault(r => string.Equals(r.Id, trimmed, StringComparison.OrdinalIgnoreCase)
            || string.Equals(r.ShortId, trimmed, StringComparison.OrdinalIgnoreCase));
        if (byId != null)
        {
            return (byId, null);
        }

        List<Routine> byName = [.. routines.Where(r => string.Equals(r.Name, trimmed, StringComparison.OrdinalIgnoreCase))];
        if (byName.Count == 0)
        {
            byName = [.. routines.Where(r => r.Name.Contains(trimmed, StringComparison.OrdinalIgnoreCase))];
        }
        return byName.Count switch
        {
            1 => (byName[0], null),
            0 => (null, $"No routine matches '{key}'. Use ListRoutines to see them."),
            _ => (null, $"'{key}' matches several routines: {string.Join(", ", byName.Select(r => $"[{r.ShortId}] {r.Name}"))}. Use the id.")
        };
    }

    internal static string Describe(Routine routine, LifeProfile profile)
    {
        StringBuilder line = new();
        line.Append($"[{routine.ShortId}] {(string.IsNullOrWhiteSpace(routine.Name) ? routine.GetType().Name : routine.Name)}: ");
        line.Append(routine.DescribeSchedule(profile));
        line.Append(routine.Enabled ? string.Empty : ", disabled");
        line.Append(routine.CreatedBySelf ? ", made by you" : ", built in");
        if (routine.LastFiredUtc.HasValue)
        {
            line.Append($", last fired {profile.ToLocal(routine.LastFiredUtc.Value).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)}");
        }
        if (!string.IsNullOrWhiteSpace(routine.Instruction))
        {
            string instruction = routine.Instruction.Length > 120 ? routine.Instruction[..117] + "..." : routine.Instruction;
            line.Append($". Instruction: {instruction}");
        }
        return line.ToString();
    }
}

internal class ScheduleCheckInCommand : RoutineCommand, IUserFactCommand
{
    public override string Name => "ScheduleCheckIn";

    public override string Description =>
        "Schedule yourself to reach out to the user later: ask how an appointment went, wish luck before an exam, " +
        "remind them of a deadline, or set up a recurring ritual. Use it on your own whenever the user mentions something with a time attached.";

    public override Argument[] GetDefaultArguments() =>
    [
        Text("About", "What to do when the time comes, written as a note to yourself, e.g. 'ask how the dentist appointment went'.", required: true),
        Text("At", "Local time 'yyyy-MM-dd HH:mm', 'yyyy-MM-dd' (09:00) or 'HH:mm' (today, or tomorrow if already past)."),
        Number("InMinutes", "Alternative to At: reach out after this many minutes."),
        Number("InHours", "Alternative to At: reach out after this many hours."),
        Text("Repeat", "'once' (default), 'daily', 'weekdays', 'weekends' or days like 'mon,wed,fri' for a recurring routine at the same local time."),
        Text("Name", "Short label, e.g. 'Dentist follow-up'."),
        Grounding.EvidenceArgument()
    ];

    public override async Task<Command[]> Execute(Context context)
    {
        DateTime now = DateTime.UtcNow;
        string about = RequireArgument("About");
        LifeProfile profile = await LoadProfile(context);

        DateTime? due = ParseDue(profile, now);
        if (due == null)
        {
            return Reply(context, "Give a time with At, InMinutes or InHours.");
        }
        if (due.Value < now.AddMinutes(-1))
        {
            return Reply(context, $"{profile.ToLocal(due.Value):yyyy-MM-dd HH:mm} is in the past. Pick a future time.");
        }
        if (due.Value > now.AddDays(366))
        {
            return Reply(context, "That is more than a year away. Pick a closer time.");
        }

        List<Routine> existing = await LoadRoutines(context);
        Routine? same = FindSimilar(existing, about, GetArgument("Name"), due.Value);
        if (same != null)
        {
            return Reply(context, $"You already have [{same.ShortId}] {same.Name}: {same.DescribeSchedule(profile)}. " +
                                  "Use AdjustRoutine to change it instead of scheduling it again.");
        }
        if (existing.Count(r => r.CreatedBySelf && r.Enabled) >= MaxSelfMadeRoutines)
        {
            return Reply(context, $"You already have {MaxSelfMadeRoutines} active self-made routines. Cancel some with CancelRoutine first.");
        }

        string repeat = GetArgument("Repeat") ?? "once";
        Routine routine;
        if (repeat.Equals("once", StringComparison.OrdinalIgnoreCase) || repeat.Equals("no", StringComparison.OrdinalIgnoreCase))
        {
            routine = new OneTimeRoutine { DueUtc = due.Value };
        }
        else
        {
            Weekdays? days = DailyRoutine.ParseDays(repeat);
            if (days == null)
            {
                return Reply(context, $"Unknown Repeat '{repeat}'. Use once, daily, weekdays, weekends or days like 'mon,wed,fri'.");
            }
            routine = new DailyRoutine
            {
                TimeOfDay = profile.ToLocal(due.Value).ToString("HH:mm", CultureInfo.InvariantCulture),
                Days = days.Value,
                WindowMinutes = 60
            };
        }

        routine.Name = GetArgument("Name") ?? Shorten(about, 40);
        routine.Instruction = about;
        routine.UserId = context.User.Id ?? string.Empty;
        routine.OwnerCharacterId = context.Character.Id ?? throw new InvalidOperationException("Character has no id.");
        routine.CreatedBySelf = true;
        routine.CreatedAtUtc = now;
        await Routines(context).SaveDocumentAsync(routine);

        return Reply(context, $"Scheduled [{routine.ShortId}] {routine.Name}: {routine.DescribeSchedule(profile)}.");
    }

    /// <summary>
    /// An enabled self-made check-in that is the same one again: due within half an hour, or due the same day with
    /// a similar note.
    /// </summary>
    internal static Routine? FindSimilar(IEnumerable<Routine> routines, string about, string? name, DateTime dueUtc)
    {
        foreach (OneTimeRoutine routine in routines.OfType<OneTimeRoutine>().Where(r => r.CreatedBySelf && r.Enabled))
        {
            if (routine.DueUtc == null)
            {
                continue;
            }
            TimeSpan apart = (routine.DueUtc.Value - dueUtc).Duration();
            if (apart <= TimeSpan.FromMinutes(30))
            {
                return routine;
            }
            bool similar = Grounding.Similarity(routine.Instruction, about) >= 0.6
                || (!string.IsNullOrWhiteSpace(name) && Grounding.Similarity(routine.Name, name) >= 0.8);
            if (apart <= TimeSpan.FromHours(18) && similar)
            {
                return routine;
            }
        }
        return null;
    }

    private DateTime? ParseDue(LifeProfile profile, DateTime now)
    {
        double? minutes = GetNumberArgument("InMinutes");
        double? hours = GetNumberArgument("InHours");
        if (minutes is > 0 || hours is > 0)
        {
            return now.AddMinutes((minutes ?? 0) + (hours ?? 0) * 60);
        }

        string? at = GetArgument("At");
        DateTime? due = ParseLocalTime(profile, at, now);
        if (due == null)
        {
            return null;
        }
        // A bare date means the morning of that day; a bare time already past means tomorrow
        if (at!.Trim().Length == 10)
        {
            due = due.Value.AddHours(9);
        }
        else if (LifeClock.TryParseTimeOfDay(at, out _) && due.Value < now)
        {
            due = LifeClock.ToUtc(profile.ToLocal(due.Value).AddDays(1), profile.TimeZone);
        }
        return due;
    }

    private static string Shorten(string text, int length) => text.Length <= length ? text : text[..(length - 3)].TrimEnd() + "...";
}

internal class ListRoutinesCommand : RoutineCommand
{
    public override string Name => "ListRoutines";

    public override string Description => "List your routines: built-in rituals and check-ins you scheduled yourself, with ids for AdjustRoutine and CancelRoutine.";

    public override Argument[] GetDefaultArguments() =>
    [
        Flag("IncludeDone", "Also list finished one-time check-ins.")
    ];

    public override async Task<Command[]> Execute(Context context)
    {
        LifeProfile profile = await LoadProfile(context);
        bool includeDone = GetBoolArgument("IncludeDone") == true;
        List<Routine> routines = [.. (await LoadRoutines(context))
            .Where(r => includeDone || r is not OneTimeRoutine { IsDone: true })
            .OrderBy(r => r.CreatedBySelf)
            .ThenBy(r => r is OneTimeRoutine once ? once.DueUtc : null)];

        if (routines.Count == 0)
        {
            return Reply(context, "You have no routines yet. Use ScheduleCheckIn to create one.");
        }
        return Reply(context, string.Join(Environment.NewLine, routines.Select(r => Describe(r, profile))));
    }
}

internal class AdjustRoutineCommand : RoutineCommand, IUserFactCommand
{
    public override string Name => "AdjustRoutine";

    public override string Description =>
        "Change one of your routines: turn it on or off, move it, change what you do when it fires, or tune a setting " +
        "(e.g. the user wants the morning greeting later, or fewer break reminders).";

    public override Argument[] GetDefaultArguments() =>
    [
        Text("Routine", "Routine id from ListRoutines, or its name.", required: true),
        Flag("Enabled", "Turn the routine on or off."),
        Text("Name", "New label."),
        Text("Instruction", "New note to yourself about what to do when it fires."),
        Text("TimeOfDay", "New local time 'HH:mm' for daily routines."),
        Text("Days", "'daily', 'weekdays', 'weekends' or days like 'mon,wed,fri' for daily routines."),
        Text("At", "New local time 'yyyy-MM-dd HH:mm' for a one-time check-in."),
        Text("QuietHours", "Local quiet hours like '23:00-08:00', or 'off'."),
        Text("Setting", "Any other setting by name, e.g. 'AfterHours', 'WorkMinutes', 'MinSleepHours'."),
        Text("Value", "Value for Setting."),
        Grounding.EvidenceArgument()
    ];

    public override async Task<Command[]> Execute(Context context)
    {
        LifeProfile profile = await LoadProfile(context);
        (Routine? routine, string? error) = Find(await LoadRoutines(context), RequireArgument("Routine"));
        if (routine == null)
        {
            return Reply(context, error!);
        }

        List<string> changes = [];
        List<string> problems = [];

        if (GetBoolArgument("Enabled") is bool enabled)
        {
            routine.Enabled = enabled;
            changes.Add(enabled ? "enabled" : "disabled");
        }
        if (GetArgument("Name") is string name)
        {
            routine.Name = name;
            changes.Add($"renamed to '{name}'");
        }
        if (GetArgument("Instruction") is string instruction)
        {
            routine.Instruction = instruction;
            changes.Add("new instruction");
        }
        if (GetArgument("TimeOfDay") is string timeOfDay)
        {
            Apply(routine, "TimeOfDay", timeOfDay, changes, problems);
        }
        if (GetArgument("Days") is string daysText)
        {
            if (routine is not DailyRoutine daily)
            {
                problems.Add("Days only applies to daily routines");
            }
            else if (DailyRoutine.ParseDays(daysText) is Weekdays days)
            {
                daily.Days = days;
                changes.Add($"days {DailyRoutine.DescribeDays(days)}");
            }
            else
            {
                problems.Add($"unknown Days '{daysText}'");
            }
        }
        if (GetArgument("At") is string at)
        {
            DateTime? due = ParseLocalTime(profile, at, DateTime.UtcNow);
            if (routine is not OneTimeRoutine once)
            {
                problems.Add("At only applies to one-time check-ins");
            }
            else if (due == null)
            {
                problems.Add($"could not read At '{at}'");
            }
            else
            {
                once.DueUtc = due;
                once.LastFiredUtc = null;
                once.Enabled = true;
                changes.Add($"moved to {profile.ToLocal(due.Value).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)}");
            }
        }
        if (GetArgument("QuietHours") is string quiet)
        {
            ApplyQuietHours(routine, quiet, changes, problems);
        }
        if (GetArgument("Setting") is string setting)
        {
            Apply(routine, setting, GetArgument("Value") ?? string.Empty, changes, problems);
        }

        if (changes.Count == 0 && problems.Count == 0)
        {
            return Reply(context, "Nothing to change. Pass at least one setting.");
        }
        if (changes.Count > 0)
        {
            await Routines(context).SaveDocumentAsync(routine);
        }

        StringBuilder output = new();
        output.Append(changes.Count > 0 ? $"Updated {Describe(routine, profile)} ({string.Join(", ", changes)})." : "Nothing changed.");
        if (problems.Count > 0)
        {
            output.Append($" Problems: {string.Join("; ", problems)}.");
        }
        return Reply(context, output.ToString());
    }

    private static void ApplyQuietHours(Routine routine, string quiet, List<string> changes, List<string> problems)
    {
        if (quiet.Equals("off", StringComparison.OrdinalIgnoreCase) || quiet.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            routine.QuietHoursStart = "00:00";
            routine.QuietHoursEnd = "00:00";
            changes.Add("quiet hours off");
            return;
        }

        string[] parts = quiet.Split('-', StringSplitOptions.TrimEntries);
        if (parts.Length == 2 && LifeClock.TryParseTimeOfDay(parts[0], out _) && LifeClock.TryParseTimeOfDay(parts[1], out _))
        {
            routine.QuietHoursStart = parts[0];
            routine.QuietHoursEnd = parts[1];
            changes.Add($"quiet hours {parts[0]}-{parts[1]}");
        }
        else
        {
            problems.Add($"could not read QuietHours '{quiet}', use 'HH:mm-HH:mm' or 'off'");
        }
    }

    /// <summary>
    /// Sets a public routine setting by name. Identity, ownership and bookkeeping fields are not adjustable.
    /// </summary>
    internal static void Apply(Routine routine, string setting, string value, List<string> changes, List<string> problems)
    {
        PropertyInfo? property = routine.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .FirstOrDefault(p => string.Equals(p.Name, setting.Trim(), StringComparison.OrdinalIgnoreCase));
        if (property == null || !property.CanWrite || property.GetCustomAttribute<NodeIgnoreAttribute>() != null
            || property.GetCustomAttribute<NodeReferenceAttribute>() != null || property.Name is "Id" or "UserId")
        {
            problems.Add($"{routine.GetType().Name} has no adjustable setting '{setting}'");
            return;
        }

        if (!TryConvert(value, property.PropertyType, out object? converted))
        {
            problems.Add($"'{value}' is not a valid {property.Name}");
            return;
        }
        if (property.PropertyType == typeof(string) && property.Name.Contains("Time", StringComparison.Ordinal)
            && !LifeClock.TryParseTimeOfDay(value, out _))
        {
            problems.Add($"{property.Name} needs a local time 'HH:mm'");
            return;
        }

        property.SetValue(routine, converted);
        changes.Add($"{property.Name} = {value}");
    }

    /// <summary>
    /// Converts tool text to a setting type: numbers culture independently, enums by name or flags list, booleans.
    /// </summary>
    internal static bool TryConvert(string value, Type type, out object? result)
    {
        result = null;
        Type target = Nullable.GetUnderlyingType(type) ?? type;
        string text = value.Trim();
        if (target == typeof(string))
        {
            result = text;
            return true;
        }
        if (text.Length == 0)
        {
            return Nullable.GetUnderlyingType(type) != null;
        }
        if (target.IsEnum)
        {
            if (!Enum.TryParse(target, text.Replace(' ', ','), ignoreCase: true, out object? parsed))
            {
                return false;
            }
            // Any number parses; only flags enums may hold combinations that are not declared
            if (target.GetCustomAttribute<FlagsAttribute>() == null && !Enum.IsDefined(target, parsed))
            {
                return false;
            }
            result = parsed;
            return true;
        }
        if (target == typeof(bool))
        {
            bool ok = bool.TryParse(text, out bool flag);
            result = flag;
            return ok;
        }
        try
        {
            result = Convert.ChangeType(text, target, CultureInfo.InvariantCulture);
            return true;
        }
        catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException)
        {
            return false;
        }
    }
}

internal class CancelRoutineCommand : RoutineCommand, IStateCommand
{
    public override string Name => "CancelRoutine";

    public override string Description =>
        "Cancel a routine. Check-ins you scheduled yourself are deleted; built-in routines are switched off and can be re-enabled with AdjustRoutine.";

    public override Argument[] GetDefaultArguments() =>
    [
        Text("Routine", "Routine id from ListRoutines, or its name.", required: true)
    ];

    public override async Task<Command[]> Execute(Context context)
    {
        (Routine? routine, string? error) = Find(await LoadRoutines(context), RequireArgument("Routine"));
        if (routine == null)
        {
            return Reply(context, error!);
        }

        if (routine.CreatedBySelf)
        {
            await Routines(context).DeleteDocumentByIdAsync(routine.Id!);
            return Reply(context, $"Deleted [{routine.ShortId}] {routine.Name}.");
        }

        routine.Enabled = false;
        await Routines(context).SaveDocumentAsync(routine);
        return Reply(context, $"Switched off [{routine.ShortId}] {routine.Name}. Re-enable it with AdjustRoutine when wanted.");
    }
}
