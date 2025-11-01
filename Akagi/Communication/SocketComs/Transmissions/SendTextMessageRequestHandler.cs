using Akagi.Bridge.Chat.Transmissions;
using Akagi.Bridge.Chat.Transmissions.Requests;
using Akagi.Bridge.Chat.Transmissions.Responses;
using Akagi.Characters;
using Akagi.Communication.Commands;
using Akagi.Data;
using Akagi.Users;
using Microsoft.Extensions.Logging;

namespace Akagi.Communication.SocketComs.Transmissions;

internal class SendTextMessageRequestHandler : SendMessageRequestHandler<SendTextMessageRequestTransmission>
{
    public override string HandlesType => nameof(SendTextMessageRequestTransmission);

    public SendTextMessageRequestHandler(IDatabaseFactory databaseFactory, ICharacterDatabase characterDatabase, IUserDatabase userDatabase, ILogger<SendTextMessageRequestHandler> logger) : base(databaseFactory, characterDatabase, userDatabase, logger)
    {
    }

    protected override SendTextMessageRequestTransmission GetMessage(TransmissionWrapper transmission)
    {
        return GetTransmission<SendTextMessageRequestTransmission>(transmission);
    }

    protected override Task HandleDocumentCommand(Context context, Character? character, User user, SendTextMessageRequestTransmission message, DocumentCommand command, string[] args)
    {
        SendResponse(context, message, "No document attached.");
        return Task.CompletedTask;
    }

    protected override void SendResponse(Context context, SendTextMessageRequestTransmission message, string? error = null)
    {
        SendTextMessageResponseTransmission response = new()
        {
            CharacterId = message.CharacterId,
            Text = message.Text,
            Error = error,
        };
        context.Session.SendTransmission(response);
    }

    protected override Task TryHandleMessage(Context context, Character character, User user, SendTextMessageRequestTransmission message)
    {
        return context.Service.RecieveText(user, character, message.Text);
    }
}
