using Akagi.Characters;
using Akagi.Receivers.Commands;
using Akagi.Receivers.SystemProcessors;
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
