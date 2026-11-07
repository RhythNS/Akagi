using Akagi.Characters.Conversations;
using Akagi.Receivers;
using Akagi.Receivers.Commands;

namespace Akagi.Characters.Groups;

/// <summary>
/// How messages written by other group members appear to one member's model.
/// </summary>
internal enum MemberSpeech
{
    /// <summary>Unchanged: other members' messages look like the character's own.</summary>
    Keep,
    /// <summary>Dropped. The persona gets its advisors' answers as Consult tool results instead.</summary>
    Hide,
    /// <summary>Shown as labeled notes ("[Akagi] ..."), so an advisor can tell who said what.</summary>
    Label
}

/// <summary>
/// Builds the message list one group member's model sees from the shared group conversation. Without it every
/// member reads the others' replies as its own words and replays their tool calls as its own.
/// </summary>
internal static class GroupView
{
    public static Message[] For(IEnumerable<Message> messages, Context context)
    {
        if (context.OtherMembers == MemberSpeech.Keep)
        {
            return [.. messages];
        }

        string? viewerId = context.Character.Id;
        string viewerName = context.Character.Name;
        List<Message> view = [];
        foreach (Message message in messages)
        {
            bool fromOther = !string.IsNullOrEmpty(message.CharacterId) && message.CharacterId != viewerId;
            switch (message)
            {
                case CommandMessage when fromOther:
                    continue;

                case TextMessage text when fromOther && message.From == Message.Type.Character:
                    if (context.OtherMembers == MemberSpeech.Label)
                    {
                        string name = context.MemberNames.GetValueOrDefault(message.CharacterId!) ?? "Another member";
                        view.Add(new TextMessage
                        {
                            From = Message.Type.System,
                            Time = text.Time,
                            CharacterId = text.CharacterId,
                            Text = $"[{name}] {text.Text}"
                        });
                    }
                    continue;

                case TextMessage text when message.From == Message.Type.System && ConsultCommand.IsConsultation(text.Text, out string advisor):
                    // The persona's own Consult calls carry its requests; an advisor only needs the ones addressed to it
                    if (context.OtherMembers == MemberSpeech.Label && string.Equals(advisor, viewerName, StringComparison.OrdinalIgnoreCase))
                    {
                        view.Add(message);
                    }
                    continue;

                default:
                    view.Add(message);
                    break;
            }
        }
        return [.. view];
    }
}
