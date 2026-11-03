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
        services.AddHttpClient(OpenRouterModelCatalog.HttpClientName, client => client.Timeout = TimeSpan.FromSeconds(20));
        // One per provider; the directory asks all of them
        services.AddSingleton<ILLMModelCatalog, OpenRouterModelCatalog>();
        services.AddSingleton<ILLMModelDirectory, LLMModelDirectory>();
        services.AddSingleton<ILLMPreferenceService, LLMPreferenceService>();
    }
}
