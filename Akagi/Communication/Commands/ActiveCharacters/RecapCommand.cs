using Akagi.Characters;
using Akagi.Characters.Conversations;
using System.Text;

namespace Akagi.Communication.Commands.ActiveCharacters;

internal class RecapCommand : TextCommand
{
    public override string Name => "/recap";

    public override string Description => "Gives context about the current active conversation";

    public override async Task<CommandResult> ExecuteAsync(Context context, string[] args)
    {
        StringBuilder sb = new();
        if (context.Character == null)
        {
            sb.AppendLine("No active character!");
        }
        else
        {
            sb.AppendLine($"Character: {context.Character.Name}");
            sb.AppendLine($"Character ID: {context.Character.Id}");

            Conversation conversation = context.Character.GetCurrentConversation();
            Message[] lastMessages = [.. conversation.Messages.TakeLast(3)];
            if (lastMessages.Length == 0)
            {
                sb.AppendLine("No messages in the current conversation.");
            }
            else
            {
                sb.AppendLine("Last Messages:");
                foreach (Message message in lastMessages)
                {
                    sb.AppendLine(message.ToString());
                }
            }
        }

        await Communicator.SendMessage(context.User, sb.ToString());
        return CommandResult.Ok;
    }
}
