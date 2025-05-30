using Akagi.Characters;
using Akagi.Characters.Conversations;

namespace Akagi.Communication.Commands;

internal class DeleteLastMessagesCommand : TextCommand
{
    public override string Name => "/deleteLastMessages";

    public override string Description => "Deletes the last messages from the current conversation. Usage: /deleteLastMessages <count>";

    public override async Task ExecuteAsync(Context context, string[] args)
    {
        int count = 0;
        if (args.Length > 0 && int.TryParse(args[0], out int parsedCount))
        {
            count = parsedCount;
        }
        if (count < 0)
        {
            await Communicator.SendMessage(context.User, "Please provide a positive number of messages to delete.");
            return;
        }
        Conversation conversation = context.Character.GetCurrentConversation();
        for (int i = conversation.Messages.Count - 1; i >= 0; i--)
        {
            Message.Type from = conversation.Messages[i].From;

            if (from == Message.Type.User && --count < 0)
            {
                break;
            }

            conversation.Messages.RemoveAt(i);
        }
        if (conversation.Messages.Count == 0)
        {
            context.Character.ClearCurrentConversation();
        }

        string toReturn;
        if (conversation.Messages.Count == 0)
        {
            toReturn = "All messages have been deleted from the current conversation.";
        }
        else
        {
            toReturn = $"Deleted the messages from the current conversation. The last message is now: {conversation.Messages.Last()}";
        }

        await Communicator.SendMessage(context.User, context.Character, toReturn);
    }
}
