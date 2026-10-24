
using Akagi.Characters.Conversations;
using Akagi.Flow;
using Microsoft.Extensions.DependencyInjection;

namespace Akagi.Receivers.Commands;

internal class AddSystemEvent : Command
{
    public override string Name => "AddSystemEvent";

    public override string Description => "Adds a system event to the system.";

    public override bool ContinueAfterExecution => true;
    
    public override Argument[] GetDefaultArguments() =>
    [
        new Argument
        {
            Name = "Event",
            Description = "The event to add to the system.",
            IsRequired = true,
            ArgumentType = Argument.Type.String
        },
    ];

    public override async Task<Command[]> Execute(Context context)
    {
        if (Arguments.Length < 1 || string.IsNullOrWhiteSpace(Arguments[0].Value))
        {
            throw new ArgumentException("Event argument is required and cannot be empty.");
        }

        string eventArg = Arguments[0].Value;
        TextMessage message = new()
        {
            CharacterId = context.Character.Id,
            From = Message.Type.Character,
            Time = DateTime.UtcNow,
            Text = eventArg
        };

        string output = $"Added system event: {eventArg}";
        context.Conversation.AddMessage(CreateCommandMessage(output));

        await Globals.Instance.ServiceProvider.GetRequiredService<IReceiver>()
            .OnSystemEvent(context.Character, context.User, message);

        return Array.Empty<Command>();
    }

}