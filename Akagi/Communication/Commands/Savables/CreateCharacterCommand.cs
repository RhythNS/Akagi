using Akagi.Characters;
using Akagi.Characters.Cards;
using Akagi.Receivers.Puppeteers;

namespace Akagi.Communication.Commands.Savables;

internal class CreateCharacterCommand : TextCommand
{
    public override string Name => "/createCharacter";

    public override string Description => "Creates a new character with the specified card, system processor and optinal name. Usage: /createCharacter <cardId> <puppeteerId> [opt]<name>";

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
        if (args.Length != 2 && args.Length != 3)
        {
            await Communicator.SendMessage(context.User, "Please provide a card id and processor id");
            return;
        }

        string cardId = args[0];
        Card? card = await _cardDatabase.GetDocumentByIdAsync(cardId);
        if (card == null)
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

        string name = args.Length == 3 ? args[2] : string.Empty;
        if (string.IsNullOrWhiteSpace(name) == false && name.Length > 50)
        {
            await Communicator.SendMessage(context.User, "Name is too long. Maximum length is 50 characters.");
            return;
        }
        if (string.IsNullOrWhiteSpace(name) == true)
        {
            name = card.Name;
        }
        List<Character> existingCharacters = await _characterDatabase.GetCharactersForUser(context.User);
        int suffix = 1;
        string originalName = name;
        while (existingCharacters.Any(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            name = $"{originalName}{suffix++}";
        }

        Character character = new()
        {
            PuppeteerId = puppeteerId,
            CardId = cardId,
            UserId = context.User.Id!,
            Name = name,
        };
        await _characterDatabase.SaveDocumentAsync(character);

        await Communicator.SendMessage(context.User, $"Character {name} created with card {cardId} and puppeteer {puppeteerId}");
    }
}
