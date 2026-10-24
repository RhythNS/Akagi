using Akagi.Characters.Conversations;
using Akagi.Receivers;

namespace Akagi.Characters.CharacterBehaviors.MessageCompilers.Injections;

internal class TextMessageInjectionCompiler : InjectionCompiler
{
    private TextMessage? _injectionMessage;

    public TextMessage InjectionMessage
    {
        get => _injectionMessage ?? throw new InvalidOperationException("InjectionMessage has not been set.");
        set => SetProperty(ref _injectionMessage, value);
    }

    protected override Task<Message[]> GetInjectionMessagesAsync(Context context)
    {
        TextMessage copied = (TextMessage)InjectionMessage.Copy();
        copied.From = MessageType;
        return Task.FromResult<Message[]>([copied]);
    }
}
