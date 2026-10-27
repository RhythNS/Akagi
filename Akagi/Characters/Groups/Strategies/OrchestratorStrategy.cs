using Akagi.Bridge.Attributes;
using Akagi.Characters.Conversations;
using Akagi.Communication;
using Akagi.Flow;
using Akagi.Receivers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MongoDB.Bson.Serialization.Attributes;

namespace Akagi.Characters.Groups.Strategies;

internal class OrchestratorStrategy : GroupStrategy
{
    private string _managerCharacterId = string.Empty;
    private int _maxRounds = 10;

    [NodeReference(typeof(Character))]
    [BsonRepresentation(MongoDB.Bson.BsonType.ObjectId)]
    public string ManagerCharacterId
    {
        get => _managerCharacterId;
        set => SetProperty(ref _managerCharacterId, value);
    }
    public int MaxRounds
    {
        get => _maxRounds;
        set => SetProperty(ref _maxRounds, value);
    }

    public override async Task ProcessAsync(GroupContext context)
    {
        ILogger logger = Globals.Instance.ServiceProvider.GetRequiredService<ILogger<OrchestratorStrategy>>();

        Character manager = context.Members.FirstOrDefault(m => m.Id == ManagerCharacterId)
            ?? throw new InvalidOperationException($"Manager character with ID {ManagerCharacterId} not found in group members.");

        List<Character> workers = [.. context.Members.Where(m => m.Id != ManagerCharacterId)];

        Conversation groupConversation = context.Group.Conversations.GetCurrentConversation();
        AggregateConversation aggregateConversation = new(groupConversation, context.Members.Select(w => w.Conversations.GetCurrentConversation()).ToArray());

        for (int round = 0; round < MaxRounds; round++)
        {
            context.CancellationToken.ThrowIfCancellationRequested();

            logger.LogInformation("Orchestrator round {Round}/{MaxRounds} for group {GroupId}, manager {ManagerId}",
                round + 1, MaxRounds, context.Group.Id, manager.Id);

            ICommunicator managerCommunicator = new GroupInternalCommunicator(
                context.Communicator, context.Group, manager, context.User, forwardToUser: false);

            await using Context managerContext = new()
            {
                Character = manager,
                Conversation = aggregateConversation,
                User = context.User,
                Communicator = managerCommunicator,
                LLMFactory = context.LLMFactory,
                DatabaseFactory = context.DatabaseFactory,
                Group = context.Group
            };

            await GroupMemberRunner.RunAsync(manager, managerContext);

            Message? managerOutput = aggregateConversation.Messages.LastOrDefault(m => m.CharacterId == manager.Id);

            if (managerOutput == null)
            {
                logger.LogWarning("Manager produced no output in round {Round}", round + 1);
                break;
            }

            string managerText = managerOutput is TextMessage tm ? tm.Text : string.Empty;

            Character? delegateTo = FindDelegateTarget(managerText, workers);
            if (delegateTo == null)
            {
                if (context.Group.UserVisibility.HasFlag(GroupVisibility.FinalOnly))
                {
                    await context.Communicator.SendMessage(context.User, managerOutput);
                }
                break;
            }

            logger.LogInformation("Manager delegating to character {CharacterId} ({CharacterName})",
                delegateTo.Id, delegateTo.Name);

            ICommunicator workerCommunicator = new GroupInternalCommunicator(
                context.Communicator, context.Group, delegateTo, context.User, forwardToUser: false);

            await using Context workerContext = new()
            {
                Character = delegateTo,
                Conversation = aggregateConversation,
                User = context.User,
                Communicator = workerCommunicator,
                LLMFactory = context.LLMFactory,
                DatabaseFactory = context.DatabaseFactory,
                Group = context.Group
            };

            await GroupMemberRunner.RunAsync(delegateTo, workerContext);

            Message? workerOutput = aggregateConversation.Messages.LastOrDefault(m => m.CharacterId == delegateTo.Id);

            if (workerOutput != null)
            {
                if (context.Group.UserVisibility.HasFlag(GroupVisibility.Intermediate))
                {
                    await context.Communicator.SendMessage(context.User, workerOutput);
                }
            }
        }

        if (context.Group.UserVisibility.HasFlag(GroupVisibility.CompletionNotice))
        {
            await context.Communicator.SendMessage(context.User, $"Group '{context.Group.Name}' has finished processing.");
        }
    }

    private static Character? FindDelegateTarget(string managerText, List<Character> workers)
    {
        if (string.IsNullOrWhiteSpace(managerText))
        {
            return null;
        }

        foreach (Character worker in workers)
        {
            string delegateTag = $"@{worker.Name}";
            if (managerText.Contains(delegateTag, StringComparison.OrdinalIgnoreCase))
            {
                return worker;
            }
        }

        return null;
    }
}
