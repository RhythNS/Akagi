using Akagi.Characters.CharacterBehaviors.MessageCompilers;
using Akagi.Data;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Akagi.Characters.Presets.Hardcoded.MessageCompilers;

internal class LatestUnCompletedConversationCompilerPreset : Preset
{
    private string _messageCompilerId = string.Empty;

    [BsonRepresentation(BsonType.ObjectId)]
    public string MessageCompilerId
    {
        get => _messageCompilerId;
        set => SetProperty(ref _messageCompilerId, value);
    }

    public override async Task CreateAsync(IDatabaseFactory databaseFactory)
    {
        LatestUnCompletedConversationCompiler compiler = new()
        {
            Name = "Latest UnCompleted Conversation Compiler",
            Description = "Filters to only the latest not completed conversation.",
        };

        await Save(databaseFactory, compiler, MessageCompilerId);

        MessageCompilerId = compiler.Id!;
    }
}
