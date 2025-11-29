using Akagi.Characters.CharacterBehaviors.SystemProcessors;
using Akagi.LLMs;
using MongoDB.Bson.Serialization.Attributes;

namespace Akagi.Characters.CharacterBehaviors.Puppeteers;

internal class SinglePuppeteer : Puppeteer
{
    private string _systemProcessorId = string.Empty;

    [BsonRepresentation(MongoDB.Bson.BsonType.ObjectId)]
    public string SystemProcessorId
    {
        get => _systemProcessorId;
        set => SetProperty(ref _systemProcessorId, value);
    }

    private SystemProcessor? _systemProcessor;

    protected override async Task InnerInit()
    {
        _systemProcessor = await SystemProcessorDatabase.GetSystemProcessor(SystemProcessorId);
    }

    public override async Task ProcessAsync()
    {
        if (_systemProcessor == null)
        {
            throw new InvalidOperationException($"System processor with ID {SystemProcessorId} not found.");
        }

        ILLM llm = LLMFactory.Create(User, _systemProcessor.SpecificLLM);
        await DefaultNextSteps(llm, _systemProcessor);
    }
}
