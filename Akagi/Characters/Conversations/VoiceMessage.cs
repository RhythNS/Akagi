namespace Akagi.Characters.Conversations;

internal class VoiceMessage : Message
{
    public string Content { get; set; } = string.Empty;

    public string VoiceId { get; set; } = string.Empty;

    public override Message Copy()
    {
        return new VoiceMessage
        {
            Time = Time,
            From = From,
            Content = Content,
            VoiceId = VoiceId
        };
    }
}
