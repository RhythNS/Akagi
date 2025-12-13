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

    protected override Message[] GetInjectionMessages(Context context)
    {
        TextMessage copied = (TextMessage)InjectionMessage.Copy();
        copied.From = MessageType;
        return [copied];
    }
}
