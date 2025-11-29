using Akagi.Characters.Conversations;
using Akagi.Receivers;

namespace Akagi.Characters.CharacterBehaviors.MessageCompilers;

internal class InjectionCompiler : MessageCompiler
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

    private TextMessage? _injectionMessage;
    private InjectionType? _type;
    private InjectionPosition? _position;

    public TextMessage InjectionMessage
    {
        get => _injectionMessage ?? throw new InvalidOperationException("InjectionMessage has not been set.");
        set => SetProperty(ref _injectionMessage, value);
    }
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

    public override void FilterCompile(Context context, ref List<Conversation> filteredConversations)
    {
        if (_injectionMessage == null)
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
                    Messages = [_injectionMessage]
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
                        filteredConversations[0].InsertMessage(0, _injectionMessage);
                        break;
                    case InjectionPosition.End:
                        filteredConversations[^1].AddMessage(_injectionMessage);
                        break;
                    default:
                        throw new InvalidOperationException("Invalid InjectionPosition for Conversation type.");
                }
                break;
        }

    }
}
