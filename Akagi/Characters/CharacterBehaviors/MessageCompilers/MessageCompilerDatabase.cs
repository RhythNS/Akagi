using Akagi.Data;
using Microsoft.Extensions.Options;

namespace Akagi.Characters.CharacterBehaviors.MessageCompilers;

internal interface IMessageCompilerDatabase : IDatabase<MessageCompiler>;

internal class MessageCompilerDatabase : Database<MessageCompiler>, IMessageCompilerDatabase
{
    public override string CollectionName => "message_compilers";

    public MessageCompilerDatabase(IOptionsMonitor<DatabaseOptions> options) : base(options)
    {
    }

    public override bool CanSave(Savable savable) => savable is MessageCompiler;

    public override Task SaveAsync(Savable savable) => SaveDocumentAsync((MessageCompiler)savable);
}
