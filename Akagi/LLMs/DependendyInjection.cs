using Akagi.LLMs.Gemini;
using Akagi.LLMs.OpenRouter;
using Microsoft.Extensions.DependencyInjection;

namespace Akagi.LLMs;

static class DependendyInjection
{
    public static void AddLLMs(this IServiceCollection services)
    {
        services.AddOptions<GeminiClient.Options>()
            .BindConfiguration("Gemini");
        services.AddOptions<OpenRouterClient.Options>()
            .BindConfiguration("OpenRouter");
        services.AddSingleton<ILLMDefinitionDatabase, LLMDefinitionDatabase>();
        services.AddSingleton<IGeminiClient, GeminiClient>();
        services.AddSingleton<IOpenRouterClient, OpenRouterClient>();
        services.AddScoped<ILLMFactory, LLMFactory>();
    }
}
