using Akagi.Characters.CharacterBehaviors.SystemProcessors;
using Akagi.LLMs;
using Akagi.Receivers;
using MongoDB.Bson.Serialization.Attributes;

namespace Akagi.Characters.CharacterBehaviors.Reflectors;

internal class DefaultReflector : Reflector
{
    private string _conversationSystemProccessorId = string.Empty;

    [BsonRepresentation(MongoDB.Bson.BsonType.ObjectId)]
    public string ConversationSystemProccessorId
    {
        get => _conversationSystemProccessorId;
        set => SetProperty(ref _conversationSystemProccessorId, value);
    }

    private SystemProcessor? _conversationSystemProccessor;

    protected override async Task InnerInit()
    {
        _conversationSystemProccessor = await SystemProcessorDatabase.GetSystemProcessor(ConversationSystemProccessorId);
    }

    public override async Task ProcessAsync()
    {
        if (_conversationSystemProccessor == null)
        {
            throw new InvalidOperationException($"System processor with ID {ConversationSystemProccessorId} not found.");
        }

        await ReflectConversations();
    }

    protected virtual async Task ReflectConversations()
    {
        ILLM llm = LLMFactory.Create(Context.User, _conversationSystemProccessor!.SpecificLLM);
        List<Conversation> conversationsToReflect = GetConversationsToReflect();
        foreach (Conversation conversation in conversationsToReflect)
        {
            await ReflectConversation(llm, _conversationSystemProccessor, conversation);
        }
    }

    protected virtual List<Conversation> GetConversationsToReflect()
    {
        return [.. Character.Conversations.Where(c =>
            c.IsCompleted && Character.Memory.Conversations.Thoughts.FirstOrDefault(t => t.ConversationId == c.Id) == null)];
    }

    protected virtual async Task ReflectConversation(ILLM llm, SystemProcessor systemProcessor, Conversation conversation)
    {
        Context.Conversation = conversation;

        await DefaultNextSteps(llm, systemProcessor);
    }
}
