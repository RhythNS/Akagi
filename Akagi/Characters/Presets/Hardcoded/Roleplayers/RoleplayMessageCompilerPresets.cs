using Akagi.Characters.CharacterBehaviors.MessageCompilers;
using Akagi.Data;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Akagi.Characters.Presets.Hardcoded.Roleplayers;

internal static class RoleplayMessageCompilerPresets
{
    internal class RoleplayDefaultCompilerPreset : Preset
    {
        private string _messageCompilerId = string.Empty;

        [BsonRepresentation(BsonType.ObjectId)]
        public string MessageCompilerId
        {
            get => _messageCompilerId;
            set => SetProperty(ref _messageCompilerId, value);
        }

        protected override async Task CreateInnerAsync(IDatabaseFactory databaseFactory)
        {
            DefaultMessageCompiler compiler = new()
            {
                Name = "Roleplay Default Message Compiler",
                Description = "The default message compiler for roleplaying characters.",
            };

            await Save(databaseFactory, compiler, MessageCompilerId);

            MessageCompilerId = compiler.Id!;
        }
    }

    internal class RoleplayCurrentConversationCompilerPreset : Preset
    {
        private string _messageCompilerId = string.Empty;

        [BsonRepresentation(BsonType.ObjectId)]
        public string MessageCompilerId
        {
            get => _messageCompilerId;
            set => SetProperty(ref _messageCompilerId, value);
        }

        protected override async Task CreateInnerAsync(IDatabaseFactory databaseFactory)
        {
            LatestUnCompletedConversationCompiler compiler = new()
            {
                Name = "Roleplay Current Conversation Compiler",
                Description = "A message compiler that includes only the current conversation messages.",
            };

            await Save(databaseFactory, compiler, MessageCompilerId);

            MessageCompilerId = compiler.Id!;
        }
    }

    internal class RoleplayLastConversationCompilerPreset : Preset
    {
        private string _messageCompilerId = string.Empty;

        [BsonRepresentation(BsonType.ObjectId)]
        public string MessageCompilerId
        {
            get => _messageCompilerId;
            set => SetProperty(ref _messageCompilerId, value);
        }

        protected override async Task CreateInnerAsync(IDatabaseFactory databaseFactory)
        {
            LatestCompletedConversationCompiler compiler = new()
            {
                Name = "Roleplay Last Conversation Compiler",
                Description = "A message compiler that includes only the last completed conversation messages.",
            };

            await Save(databaseFactory, compiler, MessageCompilerId);

            MessageCompilerId = compiler.Id!;
        }
    }
}
