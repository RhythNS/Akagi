using Akagi.Characters.CharacterBehaviors.SystemProcessors;
using Akagi.Receivers;
using Akagi.Receivers.Commands;

namespace Akagi.LLMs;

internal abstract record LLMStreamEvent;

internal record TextDeltaEvent(string Text) : LLMStreamEvent;

internal record ToolCallDeltaEvent(int Index, string? Id, string? Name, string? ArgumentsDelta) : LLMStreamEvent;

internal record StreamCompleteEvent(Command[] FinalCommands, string CompleteText) : LLMStreamEvent;

internal interface ILLM
{
    public void SetModel(string model);

    public Task<Command[]> GetNextSteps(SystemProcessor systemProcessor, Context context);

    public IAsyncEnumerable<LLMStreamEvent> GetNextStepsStreamAsync(
        SystemProcessor systemProcessor,
        Context context,
        CancellationToken cancellationToken = default);

    public enum LLMType
    {
        OpenRouter
    }

    public enum LLMUsage
    {
        TextRoleplay,
        TextGeneral,
        CommandsRoleplay,
        CommandsGeneral,
        ReflectionRoleplay,
        ReflectionGeneral
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

    public abstract IAsyncEnumerable<LLMStreamEvent> GetNextStepsStreamAsync(
        SystemProcessor systemProcessor,
        Context context,
        CancellationToken cancellationToken = default);
}
