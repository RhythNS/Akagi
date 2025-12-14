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
        OpenRouter
    }

    public enum RunMode
    {
        TextOnly,
        CommandsOnly,
        Mixed
    }
}

internal abstract class LLM : ILLM
{
    protected string? Model { get; set; }

    public void SetModel(string model)
    {
        Model = model;
    }

    public abstract Task<Command[]> GetNextSteps(SystemProcessor systemProcessor, Context context);
}
