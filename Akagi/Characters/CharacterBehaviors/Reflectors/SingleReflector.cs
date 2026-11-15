using Akagi.Bridge.Attributes;
using Akagi.Characters.CharacterBehaviors.SystemProcessors;
using Akagi.Characters.Conversations;
using Akagi.LLMs;
using Microsoft.Extensions.Logging;
using MongoDB.Bson.Serialization.Attributes;

namespace Akagi.Characters.CharacterBehaviors.Reflectors;

internal class SingleReflector : Reflector
{
    private string _systemProcessorId = string.Empty;

    [NodeReference(typeof(SystemProcessor))]
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

    private bool? _onlyNewUserMessages;

    [NodeDocu("Run only when the user wrote something since this reflector last ran, and accept only those new messages as evidence. Older messages stay visible for context. Without it every run re-reads the same messages and records days-old events again as if they just happened. On by default.")]
    public bool OnlyNewUserMessages
    {
        get => _onlyNewUserMessages ?? true;
        set => SetProperty(ref _onlyNewUserMessages, (bool?)value);
    }

    public override async Task ProcessAsync()
    {
        if (_systemProcessor == null)
        {
            throw new InvalidOperationException($"System processor with ID {SystemProcessorId} not found.");
        }

        string key = Id ?? Name;
        DateTime? reflectedUntil = Character.ReflectedUntil.TryGetValue(key, out DateTime until) ? until : null;
        DateTime? newestUser = Context.Conversation.Messages
            .Where(m => m.From == Message.Type.User)
            .Select(m => (DateTime?)m.Time)
            .Max();
        if (OnlyNewUserMessages && (newestUser == null || newestUser <= reflectedUntil))
        {
            Logger.LogInformation("Skipping reflector {ReflectorName} for character {CharacterId}: nothing new from the user since {ReflectedUntil}",
                Name, Character.Id, reflectedUntil);
            return;
        }

        ILLM llm = await LLMFactory.Create(User, _systemProcessor.SpecificLLM, _systemProcessor.Usage);
        DateTime? outer = Context.EvidenceAfterUtc;
        if (OnlyNewUserMessages)
        {
            Context.EvidenceAfterUtc = reflectedUntil;
        }
        try
        {
            await DefaultNextSteps(llm, _systemProcessor);
        }
        finally
        {
            Context.EvidenceAfterUtc = outer;
        }

        if (OnlyNewUserMessages && newestUser != null)
        {
            Character.ReflectedUntil[key] = newestUser.Value;
            Character.Dirty = true;
        }
    }
}
