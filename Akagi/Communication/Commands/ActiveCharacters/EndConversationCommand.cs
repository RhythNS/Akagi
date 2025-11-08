namespace Akagi.Communication.Commands.ActiveCharacters;

internal class EndConversationCommand : TextCommand
{
    public override string Name => "/endConversation";

    public override string Description => "Ends the current conversation with the active character. Usage: /endConversation";

    public override Task ExecuteAsync(Context context, string[] args)
    {
        if (context.Character == null)
        {
            throw new InvalidOperationException("You need to have an active character to use this command.");
        }

        context.Character.StartNewConversation();
        return Task.CompletedTask;
    }
}
