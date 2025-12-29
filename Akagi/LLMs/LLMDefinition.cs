using static Akagi.LLMs.ILLM;

namespace Akagi.LLMs;

internal class LLMDefinitions
{
    public required LLMDefinition[] Definitions { get; init; }

    public Dictionary<string, LLMDefinition> ToDictionary()
    {
        Dictionary<string, LLMDefinition> dict = [];
        Array.ForEach(Enum.GetValues<LLMUsage>(), x => dict[x.ToString()] = Definitions[0]);
        return dict;
    }
}

internal record LLMDefinition(LLMType Type, string Model);
