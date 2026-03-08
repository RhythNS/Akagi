using Akagi.Data;
using Akagi.Flow;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Akagi.LLMs;

internal interface ILLMDefinitionDatabase : IDatabase<LLMDefinition>
{
    public Task<string?> GetDefaultIdAsync();
}

internal class LLMDefinitionDatabase : Database<LLMDefinition>, ILLMDefinitionDatabase, ISystemInitializer
{
    private ILogger<LLMDefinitionDatabase> _logger;

    public LLMDefinitionDatabase(IOptionsMonitor<DatabaseOptions> options, ILogger<LLMDefinitionDatabase> logger) : base(options)
    {
        _logger = logger;
    }

    public async Task InitializeAsync()
    {
        LLMDefinition[] definitions =
        [
            new LLMDefinition() { Type = ILLM.LLMType.Gemini, Model = "gemini-2.5-flash-lite" },
            new LLMDefinition() { Type = ILLM.LLMType.Gemini, Model = "gemini-2.5-flash" },
            new LLMDefinition() { Type = ILLM.LLMType.Gemini, Model = "gemini-2.5-pro" },
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
        // TODO: Find a better solution
        List<LLMDefinition> definitions = await GetDocumentsAsync();
        return definitions.Count > 0 ? definitions[0].Id : null;
    }
}
