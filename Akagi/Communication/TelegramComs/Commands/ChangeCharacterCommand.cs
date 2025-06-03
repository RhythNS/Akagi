using Akagi.Characters;

namespace Akagi.Communication.TelegramComs.Commands;

internal class ChangeCharacterCommand : TelegramTextCommand
{
    public override string Name => "/changeCharacter";

    public override string Description => "Changes the current character.";

    private readonly ICharacterDatabase _characterDatabase;

    public ChangeCharacterCommand(ICharacterDatabase characterDatabase)
    {
        _characterDatabase = characterDatabase;
    }

    public override async Task ExecuteAsync(Context context, string[] args)
    {
        if (args.Length < 2)
        {
            await Communicator.SendMessage(context.User, "Please provide a character id");
            return;
        }
        string id = args[1];
        Character? character = await _characterDatabase.GetCharacter(id);
        if (character == null)
        {
            await Communicator.SendMessage(context.User, "Character not found");
            return;
        }
        context.User.TelegramUser!.CurrentCharacterId = character.Id;
        await Communicator.SendMessage(context.User, $"Current character changed to {character.Card.Name}");
    }
}
