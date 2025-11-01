using Akagi.Bridge.Chat.Transmissions;
using Akagi.Bridge.Chat.Transmissions.Requests;
using Akagi.Bridge.Chat.Transmissions.Responses;
using Akagi.Characters;
using Akagi.Users;

namespace Akagi.Communication.SocketComs.Transmissions;

internal class CharacterRequestHandler : SocketTransmissionHandler
{
    public override string HandlesType => nameof(CharacterRequestTransmission);

    private readonly ICharacterDatabase _characterDatabase;

    public CharacterRequestHandler(ICharacterDatabase characterDatabase)
    {
        _characterDatabase = characterDatabase;
    }

    public override async Task ExecuteAsync(Context context, TransmissionWrapper transmissionWrapper)
    {
        User user = context.User ?? throw new ArgumentNullException(nameof(context), "User cannot be null in CharacterRequestHandler");

        CharacterRequestTransmission characterRequestTransmission = GetTransmission<CharacterRequestTransmission>(transmissionWrapper);

        Character[] characters;
        if (characterRequestTransmission.Ids == null || characterRequestTransmission.Ids.Length == 0)
        {
            characters = [.. await _characterDatabase.GetCharactersForUser(user)];
        }
        else
        {
            IEnumerable<Task<Character?>> getCharacterTasks = characterRequestTransmission.Ids.Select(id => _characterDatabase.GetCharacter(id));
            Character?[] fetchedCharacters = await Task.WhenAll(getCharacterTasks);
            characters = [.. fetchedCharacters.Where(c => c is not null && c.UserId == user.Id)!];
        }

        List<Bridge.Chat.Models.Character> castCharacters = [];
        foreach (Character character in characters)
        {
            DateTime lastMessageTime = character.Conversations
                .SelectMany(c => c.Messages)
                .Select(m => (DateTime?)m.Time)
                .Max() ?? DateTime.MinValue;

            castCharacters.Add(new Bridge.Chat.Models.Character
            {
                Name = character.Name,
                CardId = character.CardId,
                Id = character.Id!,
                LastMessageTime = lastMessageTime,
            });
        }

        CharacterResponseTransmission characterResponse = new()
        {
            Characters = [.. castCharacters],
            RequestedIds = characterRequestTransmission.Ids ?? [],
        };
        context.Session.SendTransmission(characterResponse);
    }
}
