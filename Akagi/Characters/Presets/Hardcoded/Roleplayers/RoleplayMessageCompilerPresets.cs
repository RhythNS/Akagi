using Akagi.Characters.CharacterBehaviors.MessageCompilers;
using Akagi.Characters.CharacterBehaviors.MessageCompilers.Injections;
using Akagi.Data;
using Akagi.Utils.Attributes;
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

    internal class RoleplaySummarizedCompilerPreset : Preset
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
            SummarizedCompiler compiler = new()
            {
                Name = "Roleplay Summarized Message Compiler",
                Description = "A message compiler that summarizes past messages for roleplaying characters.",
                RecentWordLimit = 8000,
                LongSummaryWordLimit = 2000,
                ShortSummaryWordLimit = 500,
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

    [DependsOn(typeof(RoleplaySummarizedCompilerPreset))]
    internal class RoleplayReflectionCompilerPreset : Preset
    {
        private string _messageCompilerId = string.Empty;
        private string _innerMemoryInjectionCompilerId = string.Empty;

        [BsonRepresentation(BsonType.ObjectId)]
        public string MessageCompilerId
        {
            get => _messageCompilerId;
            set => SetProperty(ref _messageCompilerId, value);
        }

        [BsonRepresentation(BsonType.ObjectId)]
        public string InnerMemoryInjectionCompilerId
        {
            get => _innerMemoryInjectionCompilerId;
            set => SetProperty(ref _innerMemoryInjectionCompilerId, value);
        }

        protected override async Task CreateInnerAsync(IDatabaseFactory databaseFactory)
        {
            RoleplaySummarizedCompilerPreset summary = await Load<RoleplaySummarizedCompilerPreset>(databaseFactory, UserId);

            MemoryInjectionCompiler injection = new()
            {
                Name = "Roleplay Reflection Memory Injection Compiler",
                Description = "A message compiler that injects the character's reflections into the conversation.",
                Mode = MemoryInjectionCompiler.RenderMode.PerThought,
                Collections = MemoryInjectionCompiler.MemoryCollections.Short | MemoryInjectionCompiler.MemoryCollections.Long,
                Indexed = true,
                Type = InjectionCompiler.InjectionType.Message,
                Position = InjectionCompiler.InjectionPosition.End,
                MessageType = Conversations.Message.Type.Character
            };
            await Save(databaseFactory, injection, InnerMemoryInjectionCompilerId);
            InnerMemoryInjectionCompilerId = injection.Id!;


            LineMessageCompiler line = new()
            {
                Name = "Roleplay Reflection Line Message Compiler",
                Description = "A line message compiler that uses the summarized compiler for roleplaying characters.",
                Definitions =
                [
                    new LineMessageCompiler.Definition
                    {
                        MessageCompilerId = summary.MessageCompilerId,
                    },
                    new LineMessageCompiler.Definition
                    {
                        MessageCompilerId = injection.Id!,
                    }
                ]
            };
            await Save(databaseFactory, line, MessageCompilerId);
            MessageCompilerId = line.Id!;
        }
    }

    [DependsOn(typeof(RoleplaySummarizedCompilerPreset))]
    internal class RoleplayCompilerPreset : Preset
    {
        private string _messageCompilerId = string.Empty;
        private string _innerMemoryInjectionCompilerId = string.Empty;

        [BsonRepresentation(BsonType.ObjectId)]
        public string MessageCompilerId
        {
            get => _messageCompilerId;
            set => SetProperty(ref _messageCompilerId, value);
        }

        [BsonRepresentation(BsonType.ObjectId)]
        public string InnerMemoryInjectionCompilerId
        {
            get => _innerMemoryInjectionCompilerId;
            set => SetProperty(ref _innerMemoryInjectionCompilerId, value);
        }

        protected override async Task CreateInnerAsync(IDatabaseFactory databaseFactory)
        {
            RoleplaySummarizedCompilerPreset summary = await Load<RoleplaySummarizedCompilerPreset>(databaseFactory, UserId);

            MemoryInjectionCompiler injection = new()
            {
                Name = "Roleplay Memory Injection Compiler",
                Description = "A message compiler that injects the character's memories into the conversation.",
                Mode = MemoryInjectionCompiler.RenderMode.PerThought,
                Collections = MemoryInjectionCompiler.MemoryCollections.Short | MemoryInjectionCompiler.MemoryCollections.Long,
                Indexed = true,
                Type = InjectionCompiler.InjectionType.Message,
                Position = InjectionCompiler.InjectionPosition.End,
                MessageType = Conversations.Message.Type.Character
            };
            await Save(databaseFactory, injection, InnerMemoryInjectionCompilerId);
            InnerMemoryInjectionCompilerId = injection.Id!;


            LineMessageCompiler line = new()
            {
                Name = "Roleplay Line Message Compiler",
                Description = "A line message compiler that uses the summarized compiler for roleplaying characters.",
                Definitions =
                [
                    new LineMessageCompiler.Definition
                    {
                        MessageCompilerId = injection.Id!,
                    },
                    new LineMessageCompiler.Definition
                    {
                        MessageCompilerId = summary.MessageCompilerId,
                    }
                ]
            };
            await Save(databaseFactory, line, MessageCompilerId);
            MessageCompilerId = line.Id!;
        }
    }
}
