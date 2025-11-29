using Akagi.Characters.CharacterBehaviors.MessageCompilers;
using Akagi.Data;

namespace Akagi.Characters.Presets.Hardcoded.MessageCompilers;

internal class LatestCompletedConversationCompilerPreset : Preset
{
    private string _messageCompilerId = string.Empty;

    public string MessageCompilerId
    {
        get => _messageCompilerId;
        set => SetProperty(ref _messageCompilerId, value);
    }

    public override async Task CreateAsync(IDatabaseFactory databaseFactory)
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
