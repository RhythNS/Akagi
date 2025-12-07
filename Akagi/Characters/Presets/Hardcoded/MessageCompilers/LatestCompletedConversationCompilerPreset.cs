using Akagi.Characters.CharacterBehaviors.MessageCompilers;
using Akagi.Data;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Akagi.Characters.Presets.Hardcoded.MessageCompilers;

internal class LatestCompletedConversationCompilerPreset : Preset
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
        LatestCompletedConversationCompiler compiler = new()
        {
            Name = "Latest Completed Conversation Compiler",
            Description = "Filters to only the latest completed conversation.",
        };

        await Save(databaseFactory, compiler, MessageCompilerId);

        MessageCompilerId = compiler.Id!;
    }
}
