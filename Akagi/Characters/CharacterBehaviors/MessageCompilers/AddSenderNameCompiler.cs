using Akagi.Characters.Conversations;
using Akagi.Receivers;

namespace Akagi.Characters.CharacterBehaviors.MessageCompilers;

internal class AddSenderNameCompiler : MessageCompiler
{
    private string _suffix = string.Empty;

    public string Suffix
    {
        get => _suffix;
        set => SetProperty(ref _suffix, value);
    }

    public override void FilterCompile(Context context, ref List<Conversation> filteredConversations)
    {
        ICharacterDatabase characterDatabase = context.DatabaseFactory.GetDatabase<ICharacterDatabase>();
        Dictionary<string, Character> idToCharacter =
            new Dictionary<string, Character> { { context.Character.Id!, context.Character } };

        foreach (TextMessage textMessage in filteredConversations.SelectMany(x => x.Messages).OfType<TextMessage>())
        {
            string senderName;
            if (textMessage.From == Message.Type.User)
            {
                senderName = context.User.Name;
            }
            else
            {
                if (textMessage.CharacterId == null)
                {
                    continue;
                }

                if (idToCharacter.TryGetValue(textMessage.CharacterId, out Character? character) == false)
                {
                    // TODO: Fix this
                    character = characterDatabase.GetCharacter(textMessage.CharacterId!).Result
                        ?? throw new Exception($"Character with ID {textMessage.CharacterId} not found in database.");
                    idToCharacter[textMessage.CharacterId!] = character;
                }

                senderName = character.Name;
            }

            textMessage.Text = $"{senderName}{Suffix}{textMessage.Text}";
        }
    }
}
