using Akagi.Characters;
using Akagi.Users;

namespace Akagi.Communication.Commands;

internal class ListCharactersCommand : ListCommand
{
    public override string Name => "/listCharacters";

    private readonly ICharacterDatabase _characterDatabase;

    public ListCharactersCommand(ICharacterDatabase characterDatabase)
    {
        _characterDatabase = characterDatabase;
    }

    public override async Task ExecuteAsync(User user, string[] _)
    {
        List<Character> characters = await _characterDatabase.GetCharactersForUser(user);
        if (characters.Count == 0)
        {
            await Communicator.SendMessage(user, "No characters found");
            return;
        }
        string[] ids = characters.Select(x => x.Id!).ToArray();
        string[] names = characters.Select(x => x.Card.Name).ToArray();
        string choices = GetList(ids, names);
        await Communicator.SendMessage(user, $"Available characters:\n{choices}");
    }
}
