using Akagi.Scheduling;
using Akagi.Scheduling.Tasks;
using Akagi.Utils;
using Microsoft.Extensions.DependencyInjection;

namespace Akagi.Receivers.Commands;

internal class RemindCommand : Command
{
    public override string Name => "Remind";

    public override string Description => "Remind the user of a thought or task.";

    public override Argument[] Arguments => _arguments;
    private readonly Argument[] _arguments =
    [
        new Argument
        {
            Name = "Thought",
            Description = "The thought or task to remind the user about.",
            IsRequired = true,
            ArgumentType = Argument.Type.String
        },
        new Argument
        {
            Name = "Time",
            Description = "The time to remind the user (in minutes).",
            IsRequired = true,
            ArgumentType = Argument.Type.Int
        }
    ];

    public override bool ContinueAfterExecution => true;

    public override Task Execute(Context context)
    {
        if (Arguments.Length < 2
            || string.IsNullOrWhiteSpace(Arguments[0].Value)
            || !int.TryParse(Arguments[1].Value, out int timeInMinutes)
            || timeInMinutes <= 0)
        {
            throw new ArgumentException("Both Thought and Time arguments are required and must be valid.");
        }
        string thought = Arguments[0].Value;

        SendSystemMessageTask sendSystemMessageTask = new()
        {
            UserId = context.User.Id!,
            CharacterId = context.Character.Id!,
            Message = $"Reminder has elapsed: {thought}",
            Time = DateTime.UtcNow.AddMinutes(timeInMinutes)
        };

        ITaskDatabase taskDatabase = Globals.Instance.ServiceProvider.GetRequiredService<ITaskDatabase>();
        taskDatabase.SaveAsync(sendSystemMessageTask);

        return Task.CompletedTask;
    }
}
