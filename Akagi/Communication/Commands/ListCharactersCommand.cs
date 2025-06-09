using Akagi.Characters;

namespace Akagi.Communication.Commands;

internal class ListCharactersCommand : ListCommand
{
    public override string Name => "/listCharacters";

    public override string Description => "Lists all characters for the current user. Usage: /listCharacters";

    private readonly ICharacterDatabase _characterDatabase;

    public ListCharactersCommand(ICharacterDatabase characterDatabase)
    {
        _characterDatabase = characterDatabase;
    }

    public override async Task ExecuteAsync(Context context, string[] _)
    {
        List<Character> characters = await _characterDatabase.GetCharactersForUser(context.User);
        if (characters.Count == 0)
        {
            await Communicator.SendMessage(context.User, "No characters found");
            return;
        }
        string[] ids = [.. characters.Select(x => x.Id!)];
        string[] names = [.. characters.Select(x => x.Name)];
        string choices = GetIdList(ids, names);
        await Communicator.SendMessage(context.User, $"Available characters:\n{choices}");
    }
}
