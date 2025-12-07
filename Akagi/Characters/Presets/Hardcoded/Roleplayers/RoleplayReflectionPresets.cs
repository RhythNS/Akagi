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

internal static class RoleplayReflectionPresets
{
    [DependsOn(typeof(RoleplayDefaultCompilerPreset))]
    internal class RoleplayReflectionProcessorPreset : Preset
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
            RoleplayDefaultCompilerPreset compiler = await Load<RoleplayDefaultCompilerPreset>(databaseFactory, UserId);

            SystemProcessor processor = new()
            {
                Name = "Roleplay Reflection Processor",
                Description = "A processor that reflects on past conversations to improve future interactions.",
                SystemInstruction = PromptCollection.ReflectionPrompt,
                ReadableMessages = Message.Type.User | Message.Type.Character | Message.Type.System,
                Output = Message.Type.Character,
                RunMode = LLMs.ILLM.RunMode.CommandsOnly,
                MessageCompilerId = compiler.MessageCompilerId,
            };

            await Save(databaseFactory, processor, ProcessorId);

            ProcessorId = processor.Id!;
        }
    }

    [DependsOn(typeof(RoleplayReflectionProcessorPreset))]
    internal class RoleplayReflectionReflectorPreset : Preset
    {
        public static readonly string ReflectorName = "Roleplay Reflection Reflector";

        private string _reflectorId = string.Empty;

        [BsonRepresentation(BsonType.ObjectId)]
        public string ReflectorId
        {
            get => _reflectorId;
            set => SetProperty(ref _reflectorId, value);
        }

        protected override async Task CreateInnerAsync(IDatabaseFactory databaseFactory)
        {
            RoleplayReflectionProcessorPreset processor = await Load<RoleplayReflectionProcessorPreset>(databaseFactory, UserId);
            SingleReflector reflector = new()
            {
                Name = ReflectorName,
                SystemProcessorId = processor.ProcessorId,
            };

            await Save(databaseFactory, reflector, ReflectorId);

            ReflectorId = reflector.Id!;
        }
    }

    internal class RoleplayReflectionActionPreset : Preset
    {
        private string _reflectorId = string.Empty;

        [BsonRepresentation(BsonType.ObjectId)]
        public string ReflectorId
        {
            get => _reflectorId;
            set => SetProperty(ref _reflectorId, value);
        }

        protected override async Task CreateInnerAsync(IDatabaseFactory databaseFactory)
        {
            RoleplayReflectionProcessorPreset processorPreset = await Load<RoleplayReflectionProcessorPreset>(databaseFactory, UserId);
            TriggerReflect triggerReflect = new()
            {
                Name = "Roleplay Reflection Action",
                Description = "An action that triggers reflection to enhance roleplaying interactions.",
                ReflectorName = RoleplayReflectionReflectorPreset.ReflectorName,
            };
            ReflectorId = triggerReflect.Id!;
        }
    }

    [DependsOn(typeof(RoleplayReflectionActionPreset))]
    internal class RoleplayReflectionTriggerAfterTimesActionPreset : Preset
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
            RoleplayReflectionActionPreset reflectionActionPreset = await Load<RoleplayReflectionActionPreset>(databaseFactory, UserId);
            TriggerAfterTimes triggerAfterTimes = new()
            {
                Name = "Roleplay Reflection Trigger After Times Action",
                Description = "An action that triggers reflection after a specified number of interactions.",
                ActionId = reflectionActionPreset.ReflectorId,
                Times = 20,
            };
            TriggerActionId = triggerAfterTimes.Id!;
        }
    }

    [DependsOn(typeof(RoleplayReflectionTriggerAfterTimesActionPreset))]
    internal class RoleplayReflectionTriggerPointPreset : Preset
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
            RoleplayReflectionTriggerAfterTimesActionPreset action = await Load<RoleplayReflectionTriggerAfterTimesActionPreset>(databaseFactory, UserId);
            TriggerPoint triggerPoint = new()
            {
                Name = "Roleplay Reflection Trigger Point",
                Description = "A trigger point that initiates reflection based on roleplaying interactions.",
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
