using Akagi.LLMs.Gemini;
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
        LLMDefinition effectiveDefinition;

        if (overrideModel != null)
        {
            effectiveDefinition = overrideModel;
        }
        else if (user.LLMPreferences.TryGetValue(usageType.ToString(), out string? modelId))
        {
            effectiveDefinition = await _definitionDatabase.GetDocumentByIdAsync(modelId)
                ?? throw new Exception($"User {user.Id} does not have a llm set for {usageType}!");
        }
        else
        {
            throw new InvalidOperationException("No LLMDefinition could be deduced!");
        }

        ILLM? lLM = effectiveDefinition.Type switch
        {
            ILLM.LLMType.Gemini => (ILLM?)_serviceProvider.GetRequiredService(typeof(IGeminiClient)),
            ILLM.LLMType.OpenRouter => (ILLM?)_serviceProvider.GetRequiredService(typeof(IOpenRouterClient)),
            _ => throw new Exception($"Unsupported LLM type: {effectiveDefinition.Type}"),
        } ?? throw new InvalidOperationException($"LLM of type {effectiveDefinition.Type} could not be created.");

        lLM.SetModel(effectiveDefinition.Model);

        return lLM;
    }
}
