namespace Akagi.Web.Models.Chat;

public class FileMessageResult
{
    public required string CharacterId { get; set; }
    public required string Message { get; set; }
    public required string FileUrl { get; set; }
    public string? Error { get; set; } = null;
}
