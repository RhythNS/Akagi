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

    public ILLM Create(User user)
    {
        ILLM? lLM = user.LLMType switch
        {
            ILLM.LLMType.Gemini => (ILLM?)_serviceProvider.GetRequiredService(typeof(IGeminiClient)),
            ILLM.LLMType.OpenAI => throw new NotImplementedException("OpenAI LLM is not implemented yet."),
            _ => throw new ArgumentOutOfRangeException(nameof(user), user, null),
        } ?? throw new InvalidOperationException($"LLM of type {user.LLMType} could not be created.");
        
        return lLM;
    }
}
