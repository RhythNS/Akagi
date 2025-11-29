using static Akagi.LLMs.ILLM;

namespace Akagi.LLMs;

internal class LLMDefinitions
{
    public required LLMDefinition[] Definitions { get; init; }
}

internal struct LLMDefinition
{
    public required LLMType Type { get; init; }
    public required string Model { get; init; }
}
