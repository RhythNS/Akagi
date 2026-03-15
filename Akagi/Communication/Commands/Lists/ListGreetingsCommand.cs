namespace Akagi.Communication.Commands.Lists;

internal class ListGreetingsCommand : ListCommand
{
    public override string Name => "/listGreetings";

    public override string Description => "Lists all greetings for the current character. Usage: /listGreetings";

    public override async Task<CommandResult> ExecuteAsync(Context context, string[] args)
    {
        if (context.Character == null)
        {
            await Communicator.SendMessage(context.User, "You need to have an active character to use this command.");
            return CommandResult.Fail("No active character.");
        }
        string[] greetings = context.Character.Card.GetGreetings();
        if (greetings.Length == 0)
        {
            await Communicator.SendMessage(context.User, "No greetings found for this character.");
            return CommandResult.Ok;
        }
        string[] ids = [.. greetings.Select((_, index) => index.ToString())];
        string[] names = [.. greetings.Select((g, index) => $"Greeting {index + 1}: {g}\n\n------------------------------\n")];
        string choices = GetIdList(ids, names);
        await Communicator.SendMessage(context.User, $"Available greetings for {context.Character.Card.Name}:\n{choices}");
        return CommandResult.Ok;
    }
}
