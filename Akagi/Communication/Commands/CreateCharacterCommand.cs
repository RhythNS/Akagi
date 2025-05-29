using Akagi.Characters;
using Akagi.Characters.Cards;
using Akagi.Receivers.Puppeteers;

namespace Akagi.Communication.Commands;

internal class CreateCharacterCommand : TextCommand
{
    public override string Name => "/createCharacter";

    public override string Description => "Creates a new character with the specified card and system processor. Usage: /createCharacter <cardId> <puppeteerId>";

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

    public override async Task ExecuteAsync(Context context, string[] args)
    {
        if (args.Length < 2)
        {
            await Communicator.SendMessage(context.User, "Please provide a card id and processor id");
            return;
        }

        string cardId = args[0];
        bool cardExists = await _cardDatabase.DocumentExistsAsync(cardId);
        if (cardExists == false)
        {
            await Communicator.SendMessage(context.User, "Card not found");
            return;
        }

        string puppeteerId = args[1];
        bool puppeteerExists = await _puppeteerDatabase.DocumentExistsAsync(puppeteerId);
        if (puppeteerExists == false)
        {
            await Communicator.SendMessage(context.User, "System processor not found");
            return;
        }

        Character character = new()
        {
            PuppeteerId = puppeteerId,
            CardId = cardId,
            UserId = context.User.Id!,
        };
        await _characterDatabase.SaveDocumentAsync(character);

        await Communicator.SendMessage(context.User, $"Character created with card {cardId} and puppeteer {puppeteerId}");
    }
}
