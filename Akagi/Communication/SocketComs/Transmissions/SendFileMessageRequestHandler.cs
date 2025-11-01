using Akagi.Bridge.Chat.Transmissions;
using Akagi.Bridge.Chat.Transmissions.Requests;
using Akagi.Bridge.Chat.Transmissions.Responses;
using Akagi.Characters;
using Akagi.Communication.Commands;
using Akagi.Data;
using Akagi.Users;
using Microsoft.Extensions.Logging;

namespace Akagi.Communication.SocketComs.Transmissions;

internal class SendFileMessageRequestHandler : SendMessageRequestHandler<SendFileMessageRequestTransmission>
{
    public override string HandlesType => nameof(SendFileMessageRequestTransmission);

    public SendFileMessageRequestHandler(IDatabaseFactory databaseFactory, ICharacterDatabase characterDatabase, IUserDatabase userDatabase, ILogger<SendTextMessageRequestHandler> logger) : base(databaseFactory, characterDatabase, userDatabase, logger)
    {
    }

    protected override SendFileMessageRequestTransmission GetMessage(TransmissionWrapper transmission)
    {
        return GetTransmission<SendFileMessageRequestTransmission>(transmission);
    }

    protected override async Task HandleDocumentCommand(Context context, Character? character, User user, SendFileMessageRequestTransmission message, DocumentCommand command, string[] args)
    {
        SocketDocument socketDocument = new()
        {
            FileName = message.FileName,
            ContentType = message.FileType,
            Data = message.FileData,
        };

        await using Command.Context commandContext = new()
        {
            Character = character,
            User = user,
            DatabaseFactory = DatabaseFactory
        };

        await command.ExecuteAsync(commandContext, [socketDocument], args);
    }

    protected override void SendResponse(Context context, SendFileMessageRequestTransmission message, string? error = null)
    {
        SendFileMessageResponseTransmission response = new()
        {
            CharacterId = message.CharacterId,
            Text = message.Text,
            Error = error,
        };
        context.Session.SendTransmission(response);
    }

    protected override Task TryHandleMessage(Context context, Character character, User user, SendFileMessageRequestTransmission message)
    {
        // TODO: Implement file receiving logic
        // return context.Service.RecieveFile(user, character, message.FileName, message.FileType, message.FileData);
        SendResponse(context, message, "File receiving not implemented yet.");
        return Task.CompletedTask;
    }
}
