using Akagi.Bridge.Attributes;
using Akagi.Characters.Routines;
using Akagi.Flow;
using Akagi.Scheduling;
using Akagi.Scheduling.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace Akagi.Characters.TriggerPoints.Actions;

/// <summary>
/// Makes the character write to the user on its own after a delay, e.g. a check-in a few hours after a stressful
/// conversation ended. The message is scheduled instead of sent inline: sending inline would process a new message
/// from inside the trigger that the previous message fired, which can loop.
/// </summary>
[NodeDocu("Schedules the character to reach out to the user after a delay, in its own voice.")]
internal class TriggerSendMessage : TriggerAction
{
    private string _instruction = "Check in on the user and ask how they are doing.";
    private int _delayMinutes = 0;
    private string? _pendingTaskId;

    [NodeDocu("What the character should do when the time comes.")]
    public string Instruction
    {
        get => _instruction;
        set => SetProperty(ref _instruction, value);
    }
    public int DelayMinutes
    {
        get => _delayMinutes;
        set => SetProperty(ref _delayMinutes, value);
    }
    [NodeIgnore]
    public string? PendingTaskId
    {
        get => _pendingTaskId;
        set => SetProperty(ref _pendingTaskId, value);
    }

    public SendSystemMessageTask CreateTask(DateTime nowUtc)
    {
        return new SendSystemMessageTask
        {
            Name = string.IsNullOrWhiteSpace(Name) ? nameof(TriggerSendMessage) : Name,
            UserId = Context.User.Id!,
            CharacterId = Context.Character.Id!,
            Message = $"{RoutineEngine.ProactivePrefix}{Instruction} Write to the user first, naturally and in your own voice; do not mention that this was scheduled.",
            // Never immediately: the receiver still holds the character lock for the message that fired the trigger
            Time = nowUtc.AddMinutes(Math.Max(1, DelayMinutes))
        };
    }

    public override async Task ExecuteAsync()
    {
        ITaskDatabase taskDatabase = Globals.Instance.ServiceProvider.GetRequiredService<ITaskDatabase>();

        // Re-triggering replaces a pending outreach instead of stacking several
        if (!string.IsNullOrEmpty(PendingTaskId))
        {
            await taskDatabase.DeleteDocumentByIdAsync(PendingTaskId);
        }

        SendSystemMessageTask task = CreateTask(DateTime.UtcNow);
        await taskDatabase.SaveDocumentAsync(task);
        PendingTaskId = task.Id;
    }
}
