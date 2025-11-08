using Akagi.Characters;
using Akagi.Characters.Conversations;
using Akagi.Data;
using Akagi.Users;
using System.Text.Json.Serialization;

namespace Akagi.Receivers.MessageCompilers;

internal abstract class MessageCompiler : Savable
{
    private string _name = string.Empty;
    private string _description = string.Empty;

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }
    public string Description
    {
        get => _description;
        set => SetProperty(ref _description, value);
    }
    [JsonIgnore]
    protected Message.Type ReadableMessages { get; private set; }

    public virtual Task Init(Message.Type readableMessages, IMessageCompilerDatabase messageCompilerDatabase)
    {
        ReadableMessages = readableMessages;
        return Task.CompletedTask;
    }

    public Message[] Compile(User user, Character character)
    {
        List<Conversation> filteredConversations = [];
        foreach (Conversation conversation in character.Conversations
            .OrderBy(c => c.Time)
            .Select(c => c.Copy()))
        {
            conversation.Messages = [.. conversation.Messages
                .Where(m => (m.VisibleTo & ReadableMessages) != 0)
                .OrderBy(m => m.Time)];
            if (conversation.Messages.Count > 0)
            {
                filteredConversations.Add(conversation);
            }
        }

        FilterCompile(user, character, ref filteredConversations);

        return [.. filteredConversations.SelectMany(c => c.Messages.OrderBy(m => m.Time))];
    }

    public abstract void FilterCompile(User user, Character character, ref List<Conversation> filteredConversations);
}
