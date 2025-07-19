using Akagi.Characters;

namespace Akagi.Communication.Commands;

internal class EditMessageCommand : TextCommand
{
    public override string Name => "/editMessage";

    public override string Description => "Edits a message in the current conversation. Usage: /editMessage <messageIndex> <newText>";

    public async override Task ExecuteAsync(Context context, string[] args)
    {
        if (context.Character == null)
        {
            await Communicator.SendMessage(context.User, "You need to have an active character to use this command.");
            return;
        }
        Conversation conversation = context.Character.GetCurrentConversation();
        int index = conversation.Messages.Count - 1;

        if (args.Length < 2 || int.TryParse(args[0], out int parsedCount) == false)
        {
            await Communicator.SendMessage(context.User, "Please provide a message index and the new text to edit the message.");
            return;
        }

        if (args.Length > 2)
        {
            await Communicator.SendMessage(context.User, "Too many arguments provided.");
            return;
        }

        index -= parsedCount;
        if (index < 0 || index >= context.Character.GetCurrentConversation().Messages.Count)
        {
            await Communicator.SendMessage(context.User, "Invalid message index.");
            return;
        }

        string newText = args[1];
        if (conversation.EditMessage(index, newText) == false)
        {
            await Communicator.SendMessage(context.User, "Failed to edit the message.");
            return;
        }

        await Communicator.SendMessage(context.User, context.Character, $"Edited message at index {index} to: {newText}");
    }
}
