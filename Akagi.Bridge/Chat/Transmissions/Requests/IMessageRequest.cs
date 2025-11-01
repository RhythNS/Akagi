namespace Akagi.Bridge.Chat.Transmissions.Requests;

public interface IMessageRequest
{
    public string CharacterId { get; set; }
    public string Text { get; set; }
}
