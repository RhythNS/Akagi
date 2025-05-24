using Akagi.Characters;
using Akagi.Characters.Cards;
using Akagi.Puppeteers.SystemProcessors;
using Akagi.Users;

namespace Akagi.Communication.Commands;

internal class CreateCharacterCommand : TextCommand
{
    public override string Name => "/createCharacter";

    private readonly ICardDatabase _cardDatabase;
    private readonly ISystemProcessorDatabase _systemProcessorDatabase;
    private readonly ICharacterDatabase _characterDatabase;

    public CreateCharacterCommand(ICardDatabase cardDatabase,
                                  ISystemProcessorDatabase systemProcessorDatabase,
                                  ICharacterDatabase characterDatabase)
    {
        _cardDatabase = cardDatabase;
        _systemProcessorDatabase = systemProcessorDatabase;
        _characterDatabase = characterDatabase;
    }

    public override async Task ExecuteAsync(User user, string[] args)
    {
        if (args.Length < 2)
        {
            await Communicator.SendMessage(user, "Please provide a card id and processor id");
            return;
        }

        string id = args[0];
        Card? card = await _cardDatabase.GetDocumentByIdAsync(id);
        if (card == null)
        {
            await Communicator.SendMessage(user, "Card not found");
            return;
        }

        string processorId = args[1];
        SystemProcessor? systemProcessor = await _systemProcessorDatabase.GetDocumentByIdAsync(processorId);
        if (systemProcessor == null)
        {
            await Communicator.SendMessage(user, "System processor not found");
            return;
        }

        Character character = new()
        {
            SystemProcessorId = systemProcessor.Id,
            CardId = card.Id,
            UserId = user.Id,
        };
        await _characterDatabase.SaveDocumentAsync(character);

        await Communicator.SendMessage(user, $"Character created with card {card.Name} and system processor {systemProcessor.Name}");
    }
}
