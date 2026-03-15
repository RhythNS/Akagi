using Akagi.Characters.Cards;

namespace Akagi.Communication.Commands.ActiveCharacters;

internal class SwitchCardCommand : TextCommand
{
    public override string Name => "/switchCard";

    public override string Description => "Switch the active character card. Usage: /switchCard <CardName>";

    private readonly ICardDatabase _cardDatabase;

    public SwitchCardCommand(ICardDatabase cardDatabase)
    {
        _cardDatabase = cardDatabase;
    }

    public override async Task<CommandResult> ExecuteAsync(Context context, string[] args)
    {
        if (context.Character == null)
        {
            await Communicator.SendMessage(context.User, "You need to have an active character to use this command.");
            return CommandResult.Fail("No active character.");
        }

        if (args.Length != 1)
        {
            await Communicator.SendMessage(context.User, "Usage: !SwitchCard <CardName>");
            return CommandResult.Fail("Invalid arguments.");
        }
        string cardId = args[0];

        Card? card = await _cardDatabase.GetDocumentByIdAsync(cardId);
        if (card == null)
        {
            await Communicator.SendMessage(context.User, $"Card with ID '{cardId}' not found.");
            return CommandResult.Fail($"Card '{cardId}' not found.");
        }
        context.Character.CardId = card.Id!;
        await Communicator.SendMessage(context.User, "Switched cards!");
        return CommandResult.Ok;
    }
}
