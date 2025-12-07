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

internal static class RoleplayConversationSummaryPresets
{
    [DependsOn(typeof(RoleplayLastConversationCompilerPreset))]
    internal class RoleplayConversationSummaryProcessorPreset : Preset
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
            RoleplayLastConversationCompilerPreset compiler = await Load<RoleplayLastConversationCompilerPreset>(databaseFactory, UserId);

            SystemProcessor processor = new()
            {
                Name = "Roleplay Reflection Conversation Summary Processor",
                Description = "A processor that summarizes the conversations.",
                SystemInstruction = PromptCollection.ConversationSummaryPrompt,
                ReadableMessages = Message.Type.User | Message.Type.Character,
                Output = Message.Type.System,
                RunMode = LLMs.ILLM.RunMode.CommandsOnly,
                MessageCompilerId = compiler.MessageCompilerId,
            };

            await Save(databaseFactory, processor, ProcessorId);

            ProcessorId = processor.Id!;
        }
    }

    [DependsOn(typeof(RoleplayConversationSummaryProcessorPreset))]
    internal class RoleplayConversationSummaryReflectorPreset : Preset
    {
        public static readonly string ReflectorName = "Roleplay Conversation Summary Reflector";

        private string _reflectorId = string.Empty;

        [BsonRepresentation(BsonType.ObjectId)]
        public string ReflectorId
        {
            get => _reflectorId;
            set => SetProperty(ref _reflectorId, value);
        }

        protected override async Task CreateInnerAsync(IDatabaseFactory databaseFactory)
        {
            RoleplayConversationSummaryProcessorPreset preset = await Load<RoleplayConversationSummaryProcessorPreset>(databaseFactory, UserId);
            SystemProcessor processor = new()
            {
                Name = ReflectorName,
                Description = "A reflector that summarizes the conversation for roleplaying characters.",
                SystemInstruction = PromptCollection.ConversationSummaryPrompt,
                ReadableMessages = Message.Type.User | Message.Type.Character,
                Output = Message.Type.Character,
                RunMode = LLMs.ILLM.RunMode.CommandsOnly,
                MessageCompilerId = preset.ProcessorId,
            };

            await Save(databaseFactory, processor, ReflectorId);

            ReflectorId = processor.Id!;
        }
    }

    internal class RoleplayConversationSummaryTriggerReflectionActionPreset : Preset
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
            RoleplayConversationSummaryProcessorPreset processorPreset = await Load<RoleplayConversationSummaryProcessorPreset>(databaseFactory, UserId);
            TriggerReflect triggerReflect = new()
            {
                Name = "Roleplay Conversation Summary Reflection Action",
                Description = "An action that triggers reflection to summarize the conversation.",
                ReflectorName = RoleplayConversationSummaryReflectorPreset.ReflectorName,
            };

            await Save(databaseFactory, triggerReflect, TriggerActionId);

            TriggerActionId = triggerReflect.Id!;
        }
    }

    [DependsOn(typeof(RoleplayConversationSummaryTriggerReflectionActionPreset))]
    internal class RoleplayConversationSummaryTriggerPointPreset : Preset
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
            RoleplayConversationSummaryTriggerReflectionActionPreset actionPreset = await Load<RoleplayConversationSummaryTriggerReflectionActionPreset>(databaseFactory, UserId);
            TriggerPoint triggerPoint = new()
            {
                Name = "Roleplay Conversation Summary Trigger Point",
                Description = "A trigger point that activates when a roleplay conversation ends to summarize it.",
                TriggerActions =
                [
                    new TriggerPoint.TriggerActionEntry
                    {
                        Id = actionPreset.TriggerActionId,
                        OnTrigger = TriggerPoint.TriggerType.ConversationEnded,
                    },
                ],
            };

            await Save(databaseFactory, triggerPoint, TriggerPointId);

            TriggerPointId = triggerPoint.Id!;
        }
    }
}
