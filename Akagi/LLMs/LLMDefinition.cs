using Akagi.Data;
using static Akagi.LLMs.ILLM;

namespace Akagi.LLMs;

internal class LLMDefinition : Savable
{
    private LLMType _type;
    private string _model = string.Empty;

    public LLMType Type
    {
        get => _type;
        set => SetProperty(ref _type, value);
    }

    public string Model
    {
        get => _model;
        set => SetProperty(ref _model, value);
    }

    public static Dictionary<string, string> CreateDummyDictionary(string id)
    {
        Dictionary<string, string> dict = [];
        Array.ForEach(Enum.GetValues<LLMUsage>(), x => dict[x.ToString()] = id);
        return dict;
    }
}
