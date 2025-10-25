using Akagi.Bridge.Chat.Transmissions;
using Akagi.Bridge.Chat.Transmissions.Requests;
using Akagi.Bridge.Chat.Transmissions.Responses;
using Akagi.Characters;
using Akagi.Users;

namespace Akagi.Communication.SocketComs.Transmissions;

internal class ConversationRequestHandler : SocketTransmissionHandler
{
    public override string HandlesType => nameof(ConversationRequestTransmission);

    private readonly ICharacterDatabase _characterDatabase;

    public ConversationRequestHandler(ICharacterDatabase characterDatabase)
    {
        _characterDatabase = characterDatabase;
    }

    public override async Task ExecuteAsync(Context context, TransmissionWrapper transmissionWrapper)
    {
        User user = context.User ?? throw new ArgumentNullException(nameof(context), "User cannot be null in CharacterRequestHandler");

        ConversationRequestTransmission conversationRequestTransmission = GetTransmission<ConversationRequestTransmission>(transmissionWrapper);

        if (conversationRequestTransmission.CharacterId == null)
        {
            throw new ArgumentException("CharacterId cannot be null in ConversationRequestHandler");
        }

        Character? character = await _characterDatabase.GetCharacter(conversationRequestTransmission.CharacterId);
        if (character == null || character.UserId != user.Id)
        {
            throw new ArgumentException("Character not found or does not belong to the user");
        }

        if (conversationRequestTransmission.ConversationId != null)
        {
            Conversation? conversation = character.Conversations.FirstOrDefault(c => c.Id == conversationRequestTransmission.ConversationId);
            if (conversation == null)
            {
                throw new ArgumentException("Conversation not found for the character");
            }

            ConversationResponseTransmission response = new()
            {
                CharacterId = character.Id!,
                ConversationId = conversation.Id,
                Conversations = [conversation.ToBridgeModel()],
            };
            context.Session.SendTransmission(response);
            return;
        }

        ConversationResponseTransmission responseTransmission = new()
        {
            CharacterId = character.Id!,
            ConversationId = null,
            Conversations = [.. character.Conversations.Select(c => c.ToBridgeModel())],
        };
        context.Session.SendTransmission(responseTransmission);
    }
}
