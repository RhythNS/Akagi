using Akagi.Bridge.Attributes;
using Akagi.Characters.Conversations;
using Akagi.Communication;
using Akagi.Flow;
using Akagi.Receivers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MongoDB.Bson.Serialization.Attributes;

namespace Akagi.Characters.Groups.Strategies;

/// <summary>
/// One persona talks to the user; every other member is a private advisor the persona can ask with the Consult tool.
/// The user only ever hears the persona's voice, while specialists (tutor, health, time tracking) work behind the scenes.
/// </summary>
[NodeDocu("The persona answers the user directly; other members are advisors it consults privately with the 'Consult' tool.")]
internal class PersonaStrategy : GroupStrategy
{
    private string _personaCharacterId = string.Empty;

    [NodeReference(typeof(Character))]
    [NodeDocu("The character that speaks to the user. Give its SystemProcessor the 'Consult' command.")]
    [BsonRepresentation(MongoDB.Bson.BsonType.ObjectId)]
    public string PersonaCharacterId
    {
        get => _personaCharacterId;
        set => SetProperty(ref _personaCharacterId, value);
    }

    public override async Task ProcessAsync(GroupContext context)
    {
        ILogger logger = Globals.Instance.ServiceProvider.GetRequiredService<ILogger<PersonaStrategy>>();
        context.CancellationToken.ThrowIfCancellationRequested();

        Character persona = context.Members.FirstOrDefault(m => m.Id == PersonaCharacterId)
            ?? throw new InvalidOperationException($"Persona character with ID {PersonaCharacterId} is not a member of group {context.Group.Id}.");

        Conversation groupConversation = context.Group.Conversations.GetCurrentConversation();
        AggregateConversation conversation = new(groupConversation, [.. context.Members.Select(m => m.Conversations.GetCurrentConversation())]);

        // The persona's replies go straight to the user
        ICommunicator communicator = new GroupInternalCommunicator(context.Communicator, context.Group, persona, context.User, forwardToUser: true);

        await using Context personaContext = new()
        {
            Character = persona,
            Conversation = conversation,
            User = context.User,
            Communicator = communicator,
            LLMFactory = context.LLMFactory,
            DatabaseFactory = context.DatabaseFactory,
            EmbeddingService = context.EmbeddingService,
            Group = context.Group
        };

        logger.LogInformation("Persona {PersonaId} answering for group {GroupId}", persona.Id, context.Group.Id);
        await GroupMemberRunner.RunAsync(persona, personaContext);
    }
}
