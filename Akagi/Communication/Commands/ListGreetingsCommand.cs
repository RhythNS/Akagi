﻿namespace Akagi.Communication.Commands;

internal class ListGreetingsCommand : ListCommand
{
    public override string Name => "/listGreetings";

    public override string Description => "Lists all greetings for the current character. Usage: /listGreetings";

    public override Task ExecuteAsync(Context context, string[] args)
    {
        if (context.Character == null)
        {
            return Communicator.SendMessage(context.User, "You need to have an active character to use this command.");
        }
        string[] greetings = context.Character.Card.GetGreetings();
        if (greetings.Length == 0)
        {
            return Communicator.SendMessage(context.User, "No greetings found for this character.");
        }
        string[] ids = [.. greetings.Select((_, index) => index.ToString())];
        string[] names = [.. greetings.Select((g, index) => $"Greeting {index + 1}: {g}\n\n------------------------------\n")];
        string choices = GetIdList(ids, names);
        return Communicator.SendMessage(context.User, $"Available greetings for {context.Character.Card.Name}:\n{choices}");
    }
}
