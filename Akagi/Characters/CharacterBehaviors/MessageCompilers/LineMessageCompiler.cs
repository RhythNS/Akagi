using Akagi.Bridge.Attributes;
using Akagi.Characters.Conversations;
using Akagi.Receivers;
using MongoDB.Bson.Serialization.Attributes;

namespace Akagi.Characters.CharacterBehaviors.MessageCompilers;

internal class LineMessageCompiler : MessageCompiler
{
    [GraphNode]
    public class Definition
    {
        [NodeReference(typeof(MessageCompiler))]
        [BsonRepresentation(MongoDB.Bson.BsonType.ObjectId)]
        public required string MessageCompilerId { get; set; }
        [BsonIgnore]
        public MessageCompiler? MessageCompiler { get; set; } = null;
    }

    private Definition[] _definitions = [];

    [NodeReference(typeof(Definition))]
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

    public override async Task<List<Conversation>> FilterCompileAsync(Context context, List<Conversation> filteredConversations)
    {
        List<Conversation> current = filteredConversations;
        foreach (Definition definition in Definitions)
        {
            if (definition.MessageCompiler == null)
            {
                throw new InvalidOperationException("MessageCompiler cannot be null.");
            }

            current = await definition.MessageCompiler.FilterCompileAsync(context, current);
        }
        return current;
    }
}
