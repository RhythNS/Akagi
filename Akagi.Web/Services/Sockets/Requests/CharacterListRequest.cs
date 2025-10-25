using Akagi.Bridge.Chat.Transmissions.Requests;
using Akagi.Web.Models.Chat;

namespace Akagi.Web.Services.Sockets.Requests;

public class CharacterListRequest : Request<Character[], CharacterRequestTransmission>
{
    public string[] Ids { get; set; } = [];

    protected override CharacterRequestTransmission GetTransmission()
    {
        return new()
        {
            Ids = Ids
        };
    }
}
