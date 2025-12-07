using Akagi.Characters.Conversations;
using Akagi.Receivers;
using Telegram.Bot.Types.Enums;

namespace Akagi.Characters.CharacterBehaviors.MessageCompilers.Injections;

internal abstract class InjectionCompiler : MessageCompiler
{
    public enum InjectionType
    {
        Conversation,
        Message
    }

    public enum InjectionPosition
    {
        Beginning,
        End
    }

    private InjectionType? _type;
    private InjectionPosition? _position;
    private Message.Type? _messageType;

    public InjectionType Type
    {
        get => _type ?? throw new InvalidOperationException("Type has not been set.");
        set => SetProperty(ref _type, value);
    }
    public InjectionPosition Position
    {
        get => _position ?? throw new InvalidOperationException("Position has not been set.");
        set => SetProperty(ref _position, value);
    }
    public Message.Type MessageType
    {
        get => _messageType ?? throw new InvalidOperationException("Message type has not been set.");
        set => SetProperty(ref _messageType, value);
    }

    public override void FilterCompile(Context context, ref List<Conversation> filteredConversations)
    {
        Message[] messages = GetInjectionMessages(context);
        if (messages.Length == 0)
        {
            throw new InvalidOperationException("InjectionMessage must be set before compiling messages.");
        }

        switch (Type)
        {
            case InjectionType.Conversation:
                Conversation injectionConversation = new()
                {
                    Id = 0,
                    Time = DateTime.UtcNow,
                    Messages = messages
                };
                switch (Position)
                {
                    case InjectionPosition.Beginning:
                        filteredConversations.Insert(0, injectionConversation);
                        break;
                    case InjectionPosition.End:
                        filteredConversations.Add(injectionConversation);
                        break;
                    default:
                        throw new InvalidOperationException("Invalid InjectionPosition for Conversation type.");
                }
                break;

            case InjectionType.Message:
                if (filteredConversations.Count == 0)
                {
                    Conversation conversation = new()
                    {
                        Id = 0,
                        Time = DateTime.UtcNow,
                        Messages = []
                    };
                    filteredConversations.Add(conversation);
                }

                switch (Position)
                {
                    case InjectionPosition.Beginning:
                        for (int i = messages.Length - 1; i >= 0; i--)
                        {
                            filteredConversations[0].InsertMessage(0, messages[i]);
                        }
                        break;
                    case InjectionPosition.End:
                        for (int i = messages.Length - 1; i >= 0; i--)
                        {
                            filteredConversations[^1].AddMessage(messages[i]);
                        }
                        break;
                    default:
                        throw new InvalidOperationException("Invalid InjectionPosition for Conversation type.");
                }
                break;
        }
    }

    protected abstract Message[] GetInjectionMessages(Context context);
}
