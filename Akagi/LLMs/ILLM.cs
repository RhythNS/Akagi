using Akagi.Characters.CharacterBehaviors.SystemProcessors;
using Akagi.Receivers;
using Akagi.Receivers.Commands;

namespace Akagi.LLMs;

internal interface ILLM
{
    public void SetModel(string model);

    public Task<Command[]> GetNextSteps(SystemProcessor systemProcessor, Context context);

    public enum LLMType
    {
        Gemini,
        OpenAI
    }

    public enum RunMode
    {
        TextOnly,
        CommandsOnly,
        Mixed
    }
}
