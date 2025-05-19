using Akagi.LLMs.Gemini;
using Akagi.Users;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Akagi.LLMs;

internal class LLMFactory : ILLMFactory
{
    internal class Options
    {
    }

    private readonly Options _config;
    private readonly IServiceProvider _serviceProvider;

    public LLMFactory(IServiceProvider serviceProvider, IOptions<Options> config)
    {
        _serviceProvider = serviceProvider;
        _config = config.Value;
    }

    public ILLM Create(User user)
    {
        ILLM? lLM = user.LLMType switch
        {
            ILLM.LLMType.Gemini => (ILLM?)_serviceProvider.GetRequiredService(typeof(GeminiClient)),
            ILLM.LLMType.OpenAI => throw new NotImplementedException("OpenAI LLM is not implemented yet."),
            _ => throw new ArgumentOutOfRangeException(nameof(user.LLMType), user.LLMType, null),
        };
        if (lLM == null)
        {
            throw new InvalidOperationException($"LLM of type {user.LLMType} could not be created.");
        }

        return lLM;
    }
}
