using Akagi.Characters;
using Akagi.Characters.Conversations;

namespace Akagi.Communication.Commands.ActiveCharacters;

internal class LastMessagesCommand : TextCommand
{
    public override string Name => "/lastMessage";

    public override string Description => "Retrieves the last messages from the current conversation. Usage: /lastMessage <count>";

    public override Task ExecuteAsync(Context context, string[] args)
    {
        if (context.Character == null)
        {
            return Communicator.SendMessage(context.User, "You must select a character to use this command.");
        }
        Conversation? conversation = context.Character.GetCurrentConversation();
        if (conversation == null || conversation.Messages.Count == 0)
        {
            return Communicator.SendMessage(context.User, "No messages found in the current conversation.");
        }
        int lastMessagesCount = args.Length > 0 && int.TryParse(args[0], out int count) ? (int)MathF.Min(count, 7) : 5;
        Message[] lastMessages = [.. conversation.Messages.TakeLast(lastMessagesCount)];
        lastMessagesCount = (int)MathF.Min(lastMessagesCount, lastMessages.Length);
        string response = $"Last {lastMessagesCount} messages in the conversation:\n" +
                          string.Join("\n", lastMessages.Select(m => m));
        return Communicator.SendMessage(context.User, response);
    }
}
