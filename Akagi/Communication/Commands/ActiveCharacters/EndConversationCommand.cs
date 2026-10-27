using Akagi.Flow;
using Akagi.Receivers;
using Microsoft.Extensions.DependencyInjection;

namespace Akagi.Communication.Commands.ActiveCharacters;

internal class EndConversationCommand : TextCommand
{
    public override string Name => "/endConversation";

    public override string Description => "Ends the current conversation with the active character. Usage: /endConversation";

    public override async Task<CommandResult> ExecuteAsync(Context context, string[] args)
    {
        if (context.Character == null)
        {
            throw new InvalidOperationException("You need to have an active character to use this command.");
        }

        bool hadMessages = context.Character.Conversations.Current is { Messages.Count: > 0 };
        context.Character.Conversations.StartNewConversation();
        if (hadMessages)
        {
            await context.DatabaseFactory.SaveIfDirty(context.Character);
            await Globals.Instance.ServiceProvider.GetRequiredService<IReceiver>().OnConversationEnded(context.Character, context.User);
        }
        return CommandResult.Ok;
    }
}
