using Akagi.Bridge.Attributes;
using Akagi.Characters.Conversations;
using Akagi.Receivers.Commands;

namespace Akagi.Characters.CharacterBehaviors.Reflectors;

[GraphNode]
internal abstract class Reflector : CharacterBehavior
{
    private bool? _onlyAfterUserMessages;

    [NodeDocu("Skip this reflector unless the latest turn was started by a user message, so routines, reminders and call reports never make it record things nobody said. On by default, off by default for memory consolidation.")]
    public bool OnlyAfterUserMessages
    {
        get => _onlyAfterUserMessages ?? DefaultOnlyAfterUserMessages;
        set => SetProperty(ref _onlyAfterUserMessages, (bool?)value);
    }

    protected virtual bool DefaultOnlyAfterUserMessages => true;

    /// <summary>
    /// True when the newest message that started a turn came from the user. Routine, reminder and call report
    /// messages start turns too; consultation requests and tool results do not.
    /// </summary>
    public static bool LatestTurnStartedByUser(IEnumerable<Message> messages)
    {
        foreach (Message message in messages.Reverse())
        {
            switch (message)
            {
                case TextMessage or VoiceMessage when message.From == Message.Type.User:
                    return true;
                case TextMessage text when message.From == Message.Type.System && !ConsultCommand.IsConsultation(text.Text, out _):
                    return false;
            }
        }
        return false;
    }
}
