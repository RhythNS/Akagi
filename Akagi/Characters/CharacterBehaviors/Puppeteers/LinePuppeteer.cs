using Akagi.Characters.CharacterBehaviors.SystemProcessors;
using Akagi.LLMs;
using MongoDB.Bson.Serialization.Attributes;

namespace Akagi.Characters.CharacterBehaviors.Puppeteers;

internal class LinePuppeteer : Puppeteer
{
    public class Definition
    {
        [BsonRepresentation(MongoDB.Bson.BsonType.ObjectId)]
        public required string SystemProcessorId { get; set; }
        [BsonIgnore]
        public SystemProcessor? SystemProcessor { get; set; } = null;
    }

    private Definition[] _definitions = [];
    public Definition[] Definitions
    {
        get => _definitions;
        set => SetProperty(ref _definitions, value);
    }

    protected override async Task InnerInit()
    {
        for (int i = 0; i < Definitions.Length; i++)
        {
            Definitions[i].SystemProcessor = await SystemProcessorDatabase.GetSystemProcessor(Definitions[i].SystemProcessorId
                ?? throw new InvalidOperationException($"System processor with ID {Definitions[i].SystemProcessorId} not found."));
        }
    }

    public override async Task ProcessAsync()
    {
        foreach (Definition definition in Definitions)
        {
            ILLM llm = LLMFactory.Create(User, definition.SystemProcessor!.SpecificLLM);
            await DefaultNextSteps(llm, definition.SystemProcessor!);
        }
    }
}
