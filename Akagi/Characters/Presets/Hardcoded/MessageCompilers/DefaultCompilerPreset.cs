using Akagi.Characters.CharacterBehaviors.MessageCompilers;
using Akagi.Data;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Akagi.Characters.Presets.Hardcoded.MessageCompilers;

internal class DefaultCompilerPreset : Preset
{
    private string _messageCompilerId = string.Empty;

    [BsonRepresentation(BsonType.ObjectId)]
    public string MessageCompilerId
    {
        get => _messageCompilerId;
        set => SetProperty(ref _messageCompilerId, value);
    }

    protected override async Task CreateInnerAsync(IDatabaseFactory databaseFactory)
    {
        DefaultMessageCompiler compiler = new()
        {
            Name = "Default Message Compiler",
            Description = "The default message compiler that adds all conversations without filtering.",
        };

        await Save(databaseFactory, compiler, MessageCompilerId);

        MessageCompilerId = compiler.Id!;
    }
}
