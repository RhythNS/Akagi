using Akagi.Bridge.Chat.Transmissions.Requests;
using Akagi.Web.Models.Chat;

namespace Akagi.Web.Services.Sockets.Requests;

public class SendTextMessageRequest : Request<TextMessageResult, SendTextMessageRequestTransmission>
{
    public required string CharacterId { get; set; }
    public required string Text { get; set; }

    protected override SendTextMessageRequestTransmission GetTransmission()
    {
        return new()
        {
            CharacterId = CharacterId,
            Text = Text
        };
    }
}
