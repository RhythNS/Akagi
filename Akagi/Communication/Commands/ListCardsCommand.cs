using Akagi.Characters.Cards;
using Akagi.Users;

namespace Akagi.Communication.Commands;

internal class ListCardsCommand : ListCommand
{
    public override string Name => "/listCards";

    private readonly ICardDatabase _cardDatabase;

    public ListCardsCommand(ICardDatabase cardDatabase)
    {
        _cardDatabase = cardDatabase;
    }

    public override async Task ExecuteAsync(User user, string[] _)
    {
        List<Card> cards = await _cardDatabase.GetDocumentsAsync();
        if (cards.Count == 0)
        {
            await Communicator.SendMessage(user, "No cards found");
            return;
        }
        string[] ids = cards.Select(x => x.Id!).ToArray();
        string[] names = cards.Select(x => x.Name).ToArray();
        string choices = GetCommandListChoice("createCharacter", ids, names);
        await Communicator.SendMessage(user, $"Available cards:\n{choices}");
    }
}

