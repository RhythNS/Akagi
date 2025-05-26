using Akagi.Characters;
using Akagi.Characters.Conversations;
using Akagi.Data;
using Akagi.Receivers.Commands;
using Akagi.Users;
using System.Text.Json.Serialization;

namespace Akagi.Receivers.SystemProcessors;

internal class SystemProcessor : Savable
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string SystemInstruction { get; set; } = string.Empty;
    public string[] CommandNames { get; set; } = [];

    [JsonIgnore]
    public Command[] Commands { get; set; } = [];

    public void InitCommands(Command[] commands)
    {
        if (commands.Length != CommandNames.Length)
        {
            throw new ArgumentException("Command names and command instances must have the same length.");
        }

        Commands = commands;
    }

    public virtual string CompileSystemPrompt(User user, Character character)
    {
        string systemInstruction = SystemInstruction;

        systemInstruction = systemInstruction.Replace("{{user}}", user.Name);
        systemInstruction = systemInstruction.Replace("{{character}}", character.Card.Name);
        systemInstruction = systemInstruction.Replace("{{description}}", character.Card.Description);

        return systemInstruction;
    }

    public virtual Message[] CompileMessages(User user, Character character)
    {
        List<Message> messages = [];

        IEnumerable<Conversation> conversations = character.Conversations.OrderBy(x => x.Time);
        foreach (Conversation conversation in conversations)
        {
            foreach (Message message in conversation.Messages.OrderBy(x => x.Time))
            {
                messages.Add(message);
            }
        }
        return [.. messages];
    }
}
