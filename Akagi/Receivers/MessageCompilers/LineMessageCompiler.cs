using Akagi.Characters;
using Akagi.Characters.Conversations;
using Akagi.Users;
using MongoDB.Bson.Serialization.Attributes;

namespace Akagi.Receivers.MessageCompilers;

internal class LineMessageCompiler : MessageCompiler
{
    public class Definition
    {
        [BsonRepresentation(MongoDB.Bson.BsonType.ObjectId)]
        public required string MessageCompilerId { get; set; }
        [BsonIgnore]
        public MessageCompiler? MessageCompiler { get; set; } = null;
    }

    private Definition[] _definitions = [];
    public Definition[] Definitions
    {
        get => _definitions;
        set => SetProperty(ref _definitions, value);
    }

    public override async Task Init(Message.Type readableMessages, IMessageCompilerDatabase messageCompilerDatabase)
    {
        await base.Init(readableMessages, messageCompilerDatabase);

        foreach (Definition definition in Definitions)
        {
            if (definition.MessageCompilerId == null)
            {
                throw new InvalidOperationException("MessageCompilerId cannot be null.");
            }
            definition.MessageCompiler = await messageCompilerDatabase.GetDocumentByIdAsync(definition.MessageCompilerId)
                ?? throw new InvalidOperationException($"MessageCompiler with ID {definition.MessageCompilerId} not found.");
        }
    }

    public override void FilterCompile(User user, Character character, ref List<Conversation> filteredConversations)
    {
        foreach (Definition definition in Definitions)
        {
            if (definition.MessageCompiler == null)
            {
                throw new InvalidOperationException("MessageCompilerId cannot be null.");
            }

            definition.MessageCompiler.FilterCompile(user, character, ref filteredConversations);
        }
    }
}
