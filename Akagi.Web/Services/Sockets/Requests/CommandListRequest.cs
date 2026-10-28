using Akagi.Bridge.Chat.Models;
using Akagi.Bridge.Chat.Transmissions.Requests;

namespace Akagi.Web.Services.Sockets.Requests;

/// <summary>
/// Asks the server for the commands the user may run, with their parameter forms.
/// </summary>
public class CommandListRequest : Request<Command[], CommandRequestTransmission>
{
    protected override CommandRequestTransmission GetTransmission()
    {
        return new();
    }
}
