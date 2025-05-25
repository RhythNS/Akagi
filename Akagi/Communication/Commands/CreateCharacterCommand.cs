using Akagi.Characters;
using Akagi.Characters.Cards;
using Akagi.Receivers.Puppeteers;
using Akagi.Users;

namespace Akagi.Communication.Commands;

internal class CreateCharacterCommand : TextCommand
{
    public override string Name => "/createCharacter";

    private readonly ICardDatabase _cardDatabase;
    private readonly IPuppeteerDatabase _puppeteerDatabase;
    private readonly ICharacterDatabase _characterDatabase;

    public CreateCharacterCommand(ICardDatabase cardDatabase,
                                  IPuppeteerDatabase systemProcessorDatabase,
                                  ICharacterDatabase characterDatabase)
    {
        _cardDatabase = cardDatabase;
        _puppeteerDatabase = systemProcessorDatabase;
        _characterDatabase = characterDatabase;
    }

    public override async Task ExecuteAsync(User user, string[] args)
    {
        if (args.Length < 2)
        {
            await Communicator.SendMessage(user, "Please provide a card id and processor id");
            return;
        }

        string cardId = args[0];
        bool cardExists = await _cardDatabase.DocumentExistsAsync(cardId);
        if (cardExists == false)
        {
            await Communicator.SendMessage(user, "Card not found");
            return;
        }

        string puppeteerId = args[1];
        bool puppeteerExists = await _puppeteerDatabase.DocumentExistsAsync(puppeteerId);
        if (puppeteerExists == false)
        {
            await Communicator.SendMessage(user, "System processor not found");
            return;
        }

        Character character = new()
        {
            MessagePuppeteerId = puppeteerId,
            CardId = cardId,
            UserId = user.Id!,
        };
        await _characterDatabase.SaveDocumentAsync(character);

        await Communicator.SendMessage(user, $"Character created with card {cardId} and puppeteer {puppeteerId}");
    }
}
