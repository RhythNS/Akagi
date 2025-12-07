using Akagi.Characters.CharacterBehaviors.Reflectors;
using Akagi.Characters.CharacterBehaviors.SystemProcessors;
using Akagi.Characters.Conversations;
using Akagi.Characters.TriggerPoints;
using Akagi.Characters.TriggerPoints.Actions;
using Akagi.Data;
using Akagi.Utils.Attributes;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using static Akagi.Characters.Presets.Hardcoded.Roleplayers.RoleplayMessageCompilerPresets;

namespace Akagi.Characters.Presets.Hardcoded.Roleplayers;

internal static class RoleplayConversationEndedPresets
{
    [DependsOn(typeof(RoleplayCurrentConversationCompilerPreset))]
    internal class RoleplayConversationEndedProcessorPreset : Preset
    {
        private string _processorId = string.Empty;

        [BsonRepresentation(BsonType.ObjectId)]
        public string ProcessorId
        {
            get => _processorId;
            set => SetProperty(ref _processorId, value);
        }

        protected override async Task CreateInnerAsync(IDatabaseFactory databaseFactory)
        {
            RoleplayCurrentConversationCompilerPreset compiler = await Load<RoleplayCurrentConversationCompilerPreset>(databaseFactory, UserId);

            SystemProcessor processor = new()
            {
                Name = "Roleplay Reflection Conversation Ended Processor",
                Description = "A processor that checks if the roleplay conversation has ended.",
                SystemInstruction = PromptCollection.FindConversationEndPrompt,
                ReadableMessages = Message.Type.User | Message.Type.Character,
                Output = Message.Type.System,
                RunMode = LLMs.ILLM.RunMode.CommandsOnly,
                MessageCompilerId = compiler.MessageCompilerId,
            };

            await Save(databaseFactory, processor, ProcessorId);

            ProcessorId = processor.Id!;
        }
    }

    [DependsOn(typeof(RoleplayConversationEndedProcessorPreset))]
    internal class RoleplayConversationEndedReflectorPreset : Preset
    {
        public static readonly string ReflectorName = "Roleplay Conversation Ended Reflector";

        private string _reflectorId = string.Empty;

        [BsonRepresentation(BsonType.ObjectId)]
        public string ReflectorId
        {
            get => _reflectorId;
            set => SetProperty(ref _reflectorId, value);
        }

        protected override async Task CreateInnerAsync(IDatabaseFactory databaseFactory)
        {
            RoleplayConversationEndedProcessorPreset processor = await Load<RoleplayConversationEndedProcessorPreset>(databaseFactory, UserId);
            SingleReflector reflector = new()
            {
                Name = ReflectorName,
                SystemProcessorId = processor.ProcessorId,
            };

            await Save(databaseFactory, reflector, ReflectorId);

            ReflectorId = reflector.Id!;
        }
    }

    internal class RoleplayConversationEndedTriggerReflectionActionPreset : Preset
    {
        private string _triggerActionId = string.Empty;

        [BsonRepresentation(BsonType.ObjectId)]
        public string TriggerActionId
        {
            get => _triggerActionId;
            set => SetProperty(ref _triggerActionId, value);
        }

        protected override async Task CreateInnerAsync(IDatabaseFactory databaseFactory)
        {
            TriggerReflect triggerReflect = new()
            {
                Name = "Roleplay Conversation Ended Reflection Action",
                Description = "An action that triggers reflection when the roleplay conversation has ended.",
                ReflectorName = RoleplayConversationEndedReflectorPreset.ReflectorName,
            };

            await Save(databaseFactory, triggerReflect, TriggerActionId);
            
            TriggerActionId = triggerReflect.Id!;
        }
    }

    [DependsOn(typeof(RoleplayConversationEndedTriggerReflectionActionPreset))]
    internal class RoleplayConversationEndedTriggerAfterTimesActionPreset : Preset
    {
        private string _triggerActionId = string.Empty;

        [BsonRepresentation(BsonType.ObjectId)]
        public string TriggerActionId
        {
            get => _triggerActionId;
            set => SetProperty(ref _triggerActionId, value);
        }

        protected override async Task CreateInnerAsync(IDatabaseFactory databaseFactory)
        {
            RoleplayConversationEndedTriggerReflectionActionPreset action = await Load<RoleplayConversationEndedTriggerReflectionActionPreset>(databaseFactory, UserId);
            TriggerAfterTimes triggerAfterTimes = new()
            {
                Name = "Roleplay Conversation Ended Trigger Action",
                Description = "An action that triggers after the roleplay conversation has ended.",
                Times = 5,
                ActionId = action.TriggerActionId,
            };

            await Save(databaseFactory, triggerAfterTimes, TriggerActionId);

            TriggerActionId = triggerAfterTimes.Id!;
        }
    }

    [DependsOn(typeof(RoleplayConversationEndedTriggerAfterTimesActionPreset))]
    internal class RoleplayConversationEndedTriggerPointPreset : Preset
    {
        private string _triggerPointId = string.Empty;

        [BsonRepresentation(BsonType.ObjectId)]
        public string TriggerPointId
        {
            get => _triggerPointId;
            set => SetProperty(ref _triggerPointId, value);
        }

        protected override async Task CreateInnerAsync(IDatabaseFactory databaseFactory)
        {
            RoleplayConversationEndedTriggerAfterTimesActionPreset action = await Load<RoleplayConversationEndedTriggerAfterTimesActionPreset>(databaseFactory, UserId);
            TriggerPoint triggerPoint = new()
            {
                Name = "Roleplay Conversation Ended Trigger Point",
                Description = "A trigger point that activates when the roleplay conversation has ended.",
                TriggerActions =
                [
                    new TriggerPoint.TriggerActionEntry
                    {
                        Id = action.TriggerActionId,
                        OnTrigger = TriggerPoint.TriggerType.MessageProcessed,
                    },
                ],
            };

            await Save(databaseFactory, triggerPoint, TriggerPointId);

            TriggerPointId = triggerPoint.Id!;
        }
    }
}
