using Akagi.Data;
using Akagi.Flow;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Akagi.LLMs;

internal interface ILLMDefinitionDatabase : IDatabase<LLMDefinition>
{
    public Task<string?> GetDefaultIdAsync();
    public Task<LLMDefinition?> GetDefaultDefinitionAsync();
}

internal class LLMDefinitionDatabase : Database<LLMDefinition>, ILLMDefinitionDatabase, ISystemInitializer
{
    private readonly ILogger<LLMDefinitionDatabase> _logger;

    public LLMDefinitionDatabase(IOptionsMonitor<DatabaseOptions> options, ILogger<LLMDefinitionDatabase> logger) : base(options)
    {
        _logger = logger;
    }

    public async Task InitializeAsync()
    {
        LLMDefinition[] definitions =
        [
            new LLMDefinition() { Type = ILLM.LLMType.OpenRouter, Model = "google/gemini-2.0-flash-001" }
        ];

        List<LLMDefinition> existingDefinitions = await GetDocumentsAsync();

        foreach (var definition in definitions
            .Where(x => existingDefinitions.Any(y => y.Type == x.Type && y.Model == x.Model) == false))
        {
            await SaveDocumentAsync(definition);
            _logger.LogInformation("Created LLMDefinition: {type}:{model}", definition.Type, definition.Model);
        }
    }

    public override string CollectionName => "llmDefinitions";

    public override bool CanSave(Savable savable) => savable is LLMDefinition;

    public override Task SaveAsync(Savable savable) => SaveDocumentAsync((LLMDefinition)savable);

    public async Task<string?> GetDefaultIdAsync()
    {
        LLMDefinition? defaultDef = await GetDefaultDefinitionAsync();
        return defaultDef?.Id;
    }

    public async Task<LLMDefinition?> GetDefaultDefinitionAsync()
    {
        List<LLMDefinition> definitions = await GetDocumentsAsync();
        return definitions.Count > 0 ? definitions[0] : null;
    }
}
