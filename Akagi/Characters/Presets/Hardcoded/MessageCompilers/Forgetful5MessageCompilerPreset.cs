using Akagi.Characters.CharacterBehaviors.MessageCompilers;
using Akagi.Data;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Akagi.Characters.Presets.Hardcoded.MessageCompilers;

internal class Forgetful5MessageCompilerPreset : Preset
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
        ForgetfulMessageCompiler compiler = new()
        {
            Name = "Forgetful 5",
            Description = "A message compiler that forgets context after 5 messages.",
            MaxMessages = 5
        };

        await Save(databaseFactory, compiler, MessageCompilerId);

        MessageCompilerId = compiler.Id!;
    }
}
