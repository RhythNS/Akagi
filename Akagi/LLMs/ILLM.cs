using Akagi.Characters;
using Akagi.Puppeteers.Commands;
using Akagi.Puppeteers.SystemProcessors;
using Akagi.Users;

namespace Akagi.LLMs;

internal interface ILLM
{
    public Task<Command[]> GetNextSteps(SystemProcessor systemProcessor, Character character, User user);

    public enum LLMType
    {
        Gemini,
        OpenAI
    }
}
