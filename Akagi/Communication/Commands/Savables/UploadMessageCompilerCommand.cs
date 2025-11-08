using Akagi.Data;
using Akagi.Receivers.MessageCompilers;

namespace Akagi.Communication.Commands.Savables;

internal class UploadMessageCompilerCommand : UploadDocumentCommand<MessageCompiler>
{
    public override string Name => "/uploadMessageCompiler";

    protected override IDatabase<MessageCompiler> Database => _messageCompilerDatabase;
    private readonly IMessageCompilerDatabase _messageCompilerDatabase;

    protected override SaveType SaveMethod => SaveType.BSON;

    public override string Description => "Uploads a message compiler document. Usage: /uploadMessageCompiler <file>";

    public UploadMessageCompilerCommand(IMessageCompilerDatabase messageCompilerDatabase)
    {
        _messageCompilerDatabase = messageCompilerDatabase;
    }
}
