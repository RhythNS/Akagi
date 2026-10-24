using Akagi.LLMs.OpenRouter;
using Akagi.Users;
using Microsoft.Extensions.DependencyInjection;

namespace Akagi.LLMs;

internal interface ILLMFactory
{
    public Task<ILLM> Create(User user, LLMDefinition? overrideModel, ILLM.LLMUsage usageType);
}

internal class LLMFactory : ILLMFactory
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILLMDefinitionDatabase _definitionDatabase;

    public LLMFactory(IServiceProvider serviceProvider, ILLMDefinitionDatabase definitionDatabase)
    {
        _serviceProvider = serviceProvider;
        _definitionDatabase = definitionDatabase;
    }

    public async Task<ILLM> Create(User user, LLMDefinition? overrideModel, ILLM.LLMUsage usageType)
    {
        LLMDefinition? effectiveDefinition = null;

        if (overrideModel != null)
        {
            effectiveDefinition = overrideModel;
        }
        else if (user.LLMPreferences.TryGetValue(usageType.ToString(), out string? modelId))
        {
            effectiveDefinition = await _definitionDatabase.GetDocumentByIdAsync(modelId);
        }

        effectiveDefinition ??= await _definitionDatabase.GetDefaultDefinitionAsync();

        if (effectiveDefinition == null)
        {
            throw new InvalidOperationException($"No LLMDefinition could be deduced for user {user.Id} ({usageType}) and no default LLMDefinition exists in the database.");
        }

        ILLM? lLM = effectiveDefinition.Type switch
        {
            ILLM.LLMType.OpenRouter => (ILLM?)_serviceProvider.GetRequiredService(typeof(IOpenRouterClient)),
            _ => throw new Exception($"Unsupported LLM type: {effectiveDefinition.Type}"),
        } ?? throw new InvalidOperationException($"LLM of type {effectiveDefinition.Type} could not be created.");

        lLM.SetModel(effectiveDefinition.Model);

        return lLM;
    }
}
