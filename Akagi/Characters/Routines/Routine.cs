using Akagi.Bridge.Attributes;
using Akagi.Data;
using Akagi.Life;
using Akagi.Users;
using MongoDB.Bson.Serialization.Attributes;

namespace Akagi.Characters.Routines;

/// <summary>
/// Everything a routine may look at when deciding whether the character should reach out.
/// </summary>
internal class RoutineState
{
    public required DateTime NowUtc { get; init; }
    public required LifeProfile Profile { get; init; }
    public DateTime? LastUserMessageUtc { get; init; }
    public DateTime? LastCharacterMessageUtc { get; init; }

    public DateTime LocalNow => Profile.ToLocal(NowUtc);
    public string LocalToday => LifeClock.DateKey(LocalNow);
    public int LocalMinutes => LocalNow.Hour * 60 + LocalNow.Minute;
}

internal record RoutineDecision(bool Fire, string Reason, string Details = "")
{
    public static RoutineDecision No(string reason) => new(false, reason);
    public static RoutineDecision Yes(string reason, string details = "") => new(true, reason, details);
}

/// <summary>
/// A reason for the character to write first: a morning greeting, a check-in after silence, a break reminder,
/// a follow-up on a worry. Routines only decide; <see cref="RoutineService"/> delivers the outreach as a system event
/// so the character speaks in its own voice.
/// </summary>
[GraphNode]
[NodeDocu("Makes the character reach out on its own. Connect routines to Character.RoutineIds.")]
internal abstract class Routine : Savable
{
    private string _name = string.Empty;
    private string _instruction = string.Empty;
    private bool _enabled = true;
    private string _quietHoursStart = "22:30";
    private string _quietHoursEnd = "07:30";
    private int _skipWhenUserActiveMinutes = 15;
    private DateTime? _lastFiredUtc;
    private string _userId = string.Empty;
    private string _ownerCharacterId = string.Empty;
    private bool _createdBySelf;
    private DateTime? _createdAtUtc;

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }
    [NodeDocu("What the character should do when the routine fires. Empty uses the routine's default.")]
    public string Instruction
    {
        get => _instruction;
        set => SetProperty(ref _instruction, value);
    }
    public bool Enabled
    {
        get => _enabled;
        set => SetProperty(ref _enabled, value);
    }
    [NodeDocu("Local time 'HH:mm' from which the routine stays silent. Equal start and end disables quiet hours.")]
    public string QuietHoursStart
    {
        get => _quietHoursStart;
        set => SetProperty(ref _quietHoursStart, value);
    }
    [NodeDocu("Local time 'HH:mm' at which quiet hours end.")]
    public string QuietHoursEnd
    {
        get => _quietHoursEnd;
        set => SetProperty(ref _quietHoursEnd, value);
    }
    [NodeDocu("Do not interrupt when the user wrote within this many minutes. 0 disables the check.")]
    public int SkipWhenUserActiveMinutes
    {
        get => _skipWhenUserActiveMinutes;
        set => SetProperty(ref _skipWhenUserActiveMinutes, value);
    }
    [NodeIgnore]
    public DateTime? LastFiredUtc
    {
        get => _lastFiredUtc;
        set => SetProperty(ref _lastFiredUtc, value);
    }
    [NodeReference(typeof(User), Optional = true)]
    [BsonRepresentation(MongoDB.Bson.BsonType.ObjectId)]
    [UserId]
    public string UserId
    {
        get => _userId;
        set => SetProperty(ref _userId, value);
    }

    /// <summary>
    /// The character that scheduled this routine for itself. Graph routines are attached through Character.RoutineIds instead,
    /// so self-made routines survive graph updates.
    /// </summary>
    [NodeIgnore]
    public string OwnerCharacterId
    {
        get => _ownerCharacterId;
        set => SetProperty(ref _ownerCharacterId, value);
    }
    [NodeIgnore]
    public bool CreatedBySelf
    {
        get => _createdBySelf;
        set => SetProperty(ref _createdBySelf, value);
    }
    [NodeIgnore]
    public DateTime? CreatedAtUtc
    {
        get => _createdAtUtc;
        set => SetProperty(ref _createdAtUtc, value);
    }

    protected abstract string DefaultInstruction { get; }

    /// <summary>
    /// When the routine fires, in the user's local time, e.g. "daily 08:00 on workdays".
    /// </summary>
    public abstract string DescribeSchedule(LifeProfile profile);

    /// <summary>
    /// Short id used by the routine tools; the last characters of the database id.
    /// </summary>
    public string ShortId => Id is { Length: > 6 } id ? id[^6..] : Id ?? string.Empty;

    public string EffectiveInstruction => string.IsNullOrWhiteSpace(Instruction) ? DefaultInstruction : Instruction;

    public RoutineDecision Evaluate(RoutineState state)
    {
        if (!Enabled)
        {
            return RoutineDecision.No("disabled");
        }
        if (IsQuietHours(state))
        {
            return RoutineDecision.No("quiet hours");
        }
        if (SkipWhenUserActiveMinutes > 0 && state.LastUserMessageUtc.HasValue
            && state.NowUtc - state.LastUserMessageUtc.Value < TimeSpan.FromMinutes(SkipWhenUserActiveMinutes))
        {
            return RoutineDecision.No("user is active in the conversation");
        }
        return EvaluateCore(state);
    }

    protected abstract RoutineDecision EvaluateCore(RoutineState state);

    /// <summary>
    /// Called after the routine fired, before the outreach is delivered. May update the profile.
    /// </summary>
    public virtual void OnFired(RoutineState state)
    {
        LastFiredUtc = state.NowUtc;
    }

    public bool IsQuietHours(RoutineState state)
    {
        return LifeClock.TryParseTimeOfDay(QuietHoursStart, out int start)
            && LifeClock.TryParseTimeOfDay(QuietHoursEnd, out int end)
            && LifeClock.IsInRange(state.LocalMinutes, start, end);
    }

    protected bool FiredToday(RoutineState state)
    {
        return LastFiredUtc.HasValue && state.Profile.LocalDate(LastFiredUtc.Value) == state.LocalToday;
    }
}
