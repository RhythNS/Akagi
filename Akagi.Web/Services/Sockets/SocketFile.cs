using Akagi.Bridge.Chat.Transmissions.Responses;

namespace Akagi.Web.Services.Sockets;

public class SocketFile
{
    public required string Path { get; set; }

    public required string Type { get; set; }

    public required byte[] Data { get; set; } = [];

    public static SocketFile FromBridgeTransmission(FileResponseTransmission transmission)
    {
        return new SocketFile
        {
            Path = transmission.FileUrl,
            Type = transmission.Type,
            Data = transmission.Data
        };
    }
}
