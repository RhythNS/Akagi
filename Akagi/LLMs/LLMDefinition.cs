using static Akagi.LLMs.ILLM;

namespace Akagi.LLMs;

internal class LLMDefinitions
{
    public required LLMDefinition[] Definitions { get; init; }
}

internal record LLMDefinition(LLMType Type, string Model);
