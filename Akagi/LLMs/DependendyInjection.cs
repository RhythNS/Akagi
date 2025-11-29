using Akagi.LLMs.Gemini;
using Microsoft.Extensions.DependencyInjection;

namespace Akagi.LLMs;

static class DependendyInjection
{
    public static void AddLLMs(this IServiceCollection services)
    {
        services.AddOptions<GeminiClient.Options>()
            .BindConfiguration("Gemini");
        services.AddOptions<LLMDefinitions>()
            .BindConfiguration("LLMDefinitions");
        services.AddSingleton<IGeminiClient, GeminiClient>();
        services.AddScoped<ILLMFactory, LLMFactory>();
    }
}
