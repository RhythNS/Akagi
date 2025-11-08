using Akagi.Characters;
using Akagi.Characters.Conversations;
using Akagi.Users;

namespace Akagi.Receivers.MessageCompilers;

internal class InjectionCompiler : MessageCompiler
{
    private TextMessage? _injectionMessage;

    public TextMessage InjectionMessage
    {
        get => _injectionMessage ?? throw new InvalidOperationException("InjectionMessage has not been set.");
        set => SetProperty(ref _injectionMessage, value);
    }

    public override void FilterCompile(User user, Character character, ref List<Conversation> filteredConversations)
    {
        if (_injectionMessage == null)
        {
            throw new InvalidOperationException("InjectionMessage must be set before compiling messages.");
        }
        Conversation injectionConversation = new()
        {
            Id = 0,
            Time = DateTime.UtcNow,
            Messages = [_injectionMessage]
        };
        filteredConversations.Insert(0, injectionConversation);
    }
}
