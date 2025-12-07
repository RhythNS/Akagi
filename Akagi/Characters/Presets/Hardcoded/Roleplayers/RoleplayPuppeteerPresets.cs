using Akagi.Characters.CharacterBehaviors.Puppeteers;
using Akagi.Characters.CharacterBehaviors.SystemProcessors;
using Akagi.Characters.Conversations;
using Akagi.Data;
using Akagi.Receivers.Commands;
using Akagi.Utils.Attributes;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using static Akagi.Characters.Presets.Hardcoded.Roleplayers.RoleplayMessageCompilerPresets;

namespace Akagi.Characters.Presets.Hardcoded.Roleplayers;

internal static class RoleplayPuppeteerPresets
{
    [DependsOn(typeof(RoleplayDefaultCompilerPreset))]
    internal class RoleplayProcessorPreset : Preset
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
            RoleplayDefaultCompilerPreset roleplayCompiler = await Load<RoleplayDefaultCompilerPreset>(databaseFactory, UserId);

            SystemProcessor processor = new()
            {
                Name = "Roleplay Processor",
                Description = "A system processor designed for roleplaying scenarios, enhancing character interactions and immersion.",
                SystemInstruction = PromptCollection.RoleplayPrompt,
                ReadableMessages = Message.Type.User | Message.Type.Character | Message.Type.System,
                Output = Message.Type.Character,
                RunMode = LLMs.ILLM.RunMode.Mixed,
                MessageCompilerId = roleplayCompiler.MessageCompilerId,
                CommandNames = [typeof(RemindCommand).FullName!]
            };

            await Save(databaseFactory, processor, ProcessorId);

            ProcessorId = processor.Id!;
        }
    }

    [DependsOn(typeof(RoleplayProcessorPreset))]
    internal class RoleplayPuppeteerPreset : Preset
    {
        private string _puppeteerId = string.Empty;

        [BsonRepresentation(BsonType.ObjectId)]
        public string PuppeteerId
        {
            get => _puppeteerId;
            set => SetProperty(ref _puppeteerId, value);
        }

        protected override async Task CreateInnerAsync(IDatabaseFactory databaseFactory)
        {
            RoleplayProcessorPreset roleplayProcessor = await Load<RoleplayProcessorPreset>(databaseFactory, UserId);

            SinglePuppeteer singlePuppeteer = new()
            {
                Name = "Roleplay Puppeteer",
                SystemProcessorId = roleplayProcessor.ProcessorId!,
            };

            await Save(databaseFactory, singlePuppeteer, PuppeteerId);

            PuppeteerId = singlePuppeteer.Id!;
        }
    }
}
