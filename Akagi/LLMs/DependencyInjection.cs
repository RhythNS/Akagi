using Akagi.LLMs.OpenRouter;
using Microsoft.Extensions.DependencyInjection;

namespace Akagi.LLMs;

static class DependencyInjection
{
    public static void AddLLMs(this IServiceCollection services)
    {
        services.AddOptions<OpenRouterClient.Options>()
            .BindConfiguration("OpenRouter");
        services.AddSingleton<ILLMDefinitionDatabase, LLMDefinitionDatabase>();
        services.AddSingleton<IOpenRouterClient, OpenRouterClient>();
        services.AddScoped<ILLMFactory, LLMFactory>();
    }
}
