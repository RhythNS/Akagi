using Akagi.Characters;
using Akagi.Characters.Conversations;

namespace Akagi.Communication.Commands.ActiveCharacters;

internal class AddGreetingCommand : TextCommand
{
    public override string Name => "/addGreeting";

    public override string Description => "Adds a greeting to a new conversation. This completes the current conversation. Usage: /addGreeting <index>";

    private readonly ICharacterDatabase _characterDatabase;

    public AddGreetingCommand(ICharacterDatabase characterDatabase)
    {
        _characterDatabase = characterDatabase;
    }

    public override async Task<CommandResult> ExecuteAsync(Context context, string[] args)
    {
        if (args.Length == 0)
        {
            await Communicator.SendMessage(context.User, "Please provide the index of the greeting.");
            return CommandResult.Fail("No greeting index provided.");
        }
        if (context.Character == null)
        {
            await Communicator.SendMessage(context.User, "You need to have an active character to use this command.");
            return CommandResult.Fail("No active character.");
        }
        if (int.TryParse(args[0], out int index) == false || context.Character.Card.TryGetGreeting(out string greeting, index) == false)
        {
            await Communicator.SendMessage(context.User, "Please provide a valid index.");
            return CommandResult.Fail("Invalid greeting index.");
        }

        Conversation conversation = context.Character.StartNewConversation();
        TextMessage message = new()
        {
            From = Message.Type.Character,
            Text = greeting,
            Time = DateTime.UtcNow,
        };
        conversation.AddMessage(message);

        await _characterDatabase.SaveDocumentAsync(context.Character);
        await Communicator.SendMessage(context.User, context.Character, message);
        return CommandResult.Ok;
    }
}
