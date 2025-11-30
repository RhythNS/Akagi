using Akagi.Data;
using Microsoft.Extensions.Options;

namespace Akagi.Characters.CharacterBehaviors.MessageCompilers;

internal interface IMessageCompilerDatabase : IDatabase<MessageCompiler>;

internal class MessageCompilerDatabase : Database<MessageCompiler>, IMessageCompilerDatabase
{
    public MessageCompilerDatabase(IOptionsMonitor<DatabaseOptions> options) : base(options, "message_compiler")
    {
    }

    public override bool CanSave(Savable savable) => savable is MessageCompiler;

    public override Task SaveAsync(Savable savable) => SaveDocumentAsync((MessageCompiler)savable);
}
