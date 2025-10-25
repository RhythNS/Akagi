using Akagi.Bridge.Chat.Transmissions.Requests;

namespace Akagi.Web.Services.Sockets.Requests;

public class FileRequest : Request<SocketFile, FileRequestTransmission>
{
    public string FileUrl { get; init; }

    public FileRequest(string imageUrl)
    {
        FileUrl = imageUrl;
    }

    protected override FileRequestTransmission GetTransmission()
    {
        return new()
        {
            FileUrl = FileUrl
        };
    }
}
