using Akagi.Characters;

namespace Akagi.Communication.Commands;

internal class DeleteCharacterCommand : TextCommand
{
    public override string Name => "/deleteCharacter";

    public override string Description => "Deletes a character by its ID. Usage: /deleteCharacter <characterId>";

    private readonly ICharacterDatabase _characterDatabase;

    public DeleteCharacterCommand(ICharacterDatabase characterDatabase)
    {
        _characterDatabase = characterDatabase;
    }

    public override async Task ExecuteAsync(Context context, string[] args)
    {
        if (args.Length != 1)
        {
            await Communicator.SendMessage(context.User, "Please provide a character ID.");
            return;
        }
        string characterId = args[0];
        if (string.IsNullOrWhiteSpace(characterId))
        {
            await Communicator.SendMessage(context.User, "Character ID cannot be empty.");
            return;
        }

        List<Character> existingCharacters = await _characterDatabase.GetCharactersForUser(context.User);
        if (existingCharacters.Any(c => c.Id!.Equals(characterId, StringComparison.OrdinalIgnoreCase)) == false)
        {
            await Communicator.SendMessage(context.User, "Character not found.");
            return;
        }

        await _characterDatabase.DeleteDocumentByIdAsync(characterId);

        await Communicator.SendMessage(context.User, $"Character with ID {characterId} has been deleted successfully.");
    }
}
