using Akagi.Characters;
using Akagi.Puppeteers.Commands;
using Akagi.Puppeteers.SystemProcessors;

namespace Akagi.LLMs;

internal interface ILLM
{
    public Task<Command[]> GetNextSteps(SystemProcessor systemProcessor, Character character);

    public enum LLMType
    {
        Gemini,
        OpenAI
    }
}
