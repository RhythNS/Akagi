using Akagi.Bridge.Chat.Transmissions;
using Akagi.Bridge.Chat.Transmissions.Requests;
using Akagi.Bridge.Chat.Transmissions.Responses;
using Akagi.Characters;
using Akagi.Characters.Conversations;
using Akagi.Characters.Groups;
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

        ICharacterGroupDatabase groupDatabase = context.DatabaseFactory.GetDatabase<ICharacterGroupDatabase>();
        Character[] characters;
        CharacterGroup[] groups;
        if (characterRequestTransmission.Ids == null || characterRequestTransmission.Ids.Length == 0)
        {
            characters = [.. await _characterDatabase.GetCharactersForUser(user)];
            groups = [.. await groupDatabase.GetGroupsForUser(user)];
        }
        else
        {
            IEnumerable<Task<Character?>> getCharacterTasks = characterRequestTransmission.Ids.Select(id => _characterDatabase.GetCharacter(id));
            Character?[] fetchedCharacters = await Task.WhenAll(getCharacterTasks);
            characters = [.. fetchedCharacters.Where(c => c is not null && c.UserId == user.Id)!];

            // Ids that are no character may be groups
            IEnumerable<Task<CharacterGroup?>> getGroupTasks = characterRequestTransmission.Ids
                .Where(id => !characters.Any(c => c.Id == id))
                .Select(groupDatabase.GetDocumentByIdAsync);
            CharacterGroup?[] fetchedGroups = await Task.WhenAll(getGroupTasks);
            groups = [.. fetchedGroups.Where(g => g is not null && g.UserId == user.Id)!];
        }

        List<Bridge.Chat.Models.Character> castCharacters = [];
        foreach (Character character in characters)
        {
            castCharacters.Add(new Bridge.Chat.Models.Character
            {
                Name = character.Name,
                CardId = character.CardId,
                HasImage = !string.IsNullOrEmpty(character.Card.ImageId),
                Id = character.Id!,
                LastMessageTime = LastMessageTime(character.Conversations),
            });
        }

        foreach (CharacterGroup group in groups)
        {
            string? speakerId = await GroupSpeaker.GetIdAsync(group, context.DatabaseFactory);
            Character? speaker = speakerId == null
                ? null
                : characters.FirstOrDefault(c => c.Id == speakerId) ?? await _characterDatabase.GetCharacter(speakerId);

            castCharacters.Add(new Bridge.Chat.Models.Character
            {
                Name = group.Name,
                CardId = speaker?.CardId ?? string.Empty,
                HasImage = !string.IsNullOrEmpty(speaker?.Card.ImageId),
                Id = group.Id!,
                LastMessageTime = LastMessageTime(group.Conversations),
                IsGroup = true,
                MemberIds = group.MemberCharacterIds,
            });
        }

        CharacterResponseTransmission characterResponse = new()
        {
            Characters = [.. castCharacters],
            RequestedIds = characterRequestTransmission.Ids ?? [],
        };
        context.Session.SendTransmission(characterResponse);
    }

    private static DateTime LastMessageTime(ConversationContainer conversations)
    {
        return conversations.List
            .SelectMany(c => c.Messages)
            .Select(m => (DateTime?)m.Time)
            .Max() ?? DateTime.MinValue;
    }
}
