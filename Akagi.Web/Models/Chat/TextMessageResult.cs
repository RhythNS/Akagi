namespace Akagi.Web.Models.Chat;

public class TextMessageResult
{
    public required string CharacterId { get; set; }
    public required string Text { get; set; }
    public string? Error { get; set; } = null;
}
