using Akagi.Characters;
using Akagi.Characters.Conversations;

namespace Akagi.Communication.Commands.ActiveCharacters;

internal class LastMessagesCommand : TextCommand
{
    public override string Name => "/lastMessage";

    public override string Description => "Retrieves the last messages from the current conversation. Usage: /lastMessage <count>";

    public override async Task<CommandResult> ExecuteAsync(Context context, string[] args)
    {
        if (context.Character == null)
        {
            await Communicator.SendMessage(context.User, "You must select a character to use this command.");
            return CommandResult.Fail("No active character.");
        }
        Conversation? conversation = context.Character.GetCurrentConversation();
        if (conversation == null || conversation.Messages.Count == 0)
        {
            await Communicator.SendMessage(context.User, "No messages found in the current conversation.");
            return CommandResult.Fail("No messages found.");
        }
        int lastMessagesCount = args.Length > 0 && int.TryParse(args[0], out int count) ? (int)MathF.Min(count, 7) : 5;
        Message[] lastMessages = [.. conversation.Messages.TakeLast(lastMessagesCount)];
        lastMessagesCount = (int)MathF.Min(lastMessagesCount, lastMessages.Length);
        string response = $"Last {lastMessagesCount} messages in the conversation:\n" +
                          string.Join("\n", lastMessages.Select(m => m));
        await Communicator.SendMessage(context.User, response);
        return CommandResult.Ok;
    }
}
