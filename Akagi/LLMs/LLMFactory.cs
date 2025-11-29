using Akagi.LLMs.Gemini;
using Akagi.Users;
using Microsoft.Extensions.DependencyInjection;

namespace Akagi.LLMs;

internal class LLMFactory : ILLMFactory
{
    private readonly IServiceProvider _serviceProvider;

    public LLMFactory(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public ILLM Create(User user, LLMDefinition? overrideModel)
    {
        LLMDefinition effectiveDefinition = overrideModel ?? user.LLMDefinition ?? throw new InvalidOperationException("No LLM definition provided.");

        ILLM? lLM = effectiveDefinition.Type switch
        {
            ILLM.LLMType.Gemini => (ILLM?)_serviceProvider.GetRequiredService(typeof(IGeminiClient)),
            ILLM.LLMType.OpenAI => throw new NotImplementedException("OpenAI LLM is not implemented yet."),
            _ => throw new Exception($"Unsupported LLM type: {effectiveDefinition.Type}"),
        } ?? throw new InvalidOperationException($"LLM of type {effectiveDefinition.Type} could not be created.");

        lLM.SetModel(effectiveDefinition.Model);

        return lLM;
    }
}
