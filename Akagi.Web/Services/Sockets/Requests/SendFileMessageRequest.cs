using Akagi.Bridge.Chat.Transmissions.Requests;
using Akagi.Web.Models.Chat;

namespace Akagi.Web.Services.Sockets.Requests;

public class SendFileMessageRequest : Request<FileMessageResult, SendFileMessageRequestTransmission>
{
    public required string CharacterId { get; set; }
    public required string Text { get; set; }
    public required string FileType { get; set; }
    public required string FileName { get; set; }
    public required byte[] FileData { get; set; }

    protected override SendFileMessageRequestTransmission GetTransmission()
    {
        return new()
        {
            CharacterId = CharacterId,
            Text = Text,
            FileType = FileType,
            FileName = FileName,
            FileData = FileData
        };
    }
}
