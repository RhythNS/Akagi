using System.Text.RegularExpressions;

namespace Akagi.Web.Models.Chat;

/// <summary>
/// One turn of a chat: what started it (the user's message or an instruction such as a routine), every step the
/// characters took (tool calls, consultations, what members said to each other, reflections) and the bubbles the user
/// sees among them.
/// </summary>
public sealed class ChatTurn
{
    public required string Key { get; init; }
    public List<Message> Steps { get; } = [];

    public IEnumerable<TextMessage> Bubbles => Steps.OfType<TextMessage>().Where(m => m.IsShownInChat);

    /// <summary>How many steps the chat itself does not show.</summary>
    public int HiddenCount => Steps.Count(m => !m.IsShownInChat);
}

public static partial class ChatTurns
{
    /// <summary>
    /// A turn starts with the user's message or an instruction for the characters (a routine, a call report);
    /// a consultation between members is part of the turn that asked for it.
    /// </summary>
    public static bool StartsTurn(Message message) =>
        message is TextMessage { Tool: null, Internal: false } text &&
        (text.From == Message.Type.User || text.From == Message.Type.System);

    public static List<ChatTurn> Build(int conversationId, IReadOnlyList<Message> messages)
    {
        List<ChatTurn> turns = [];
        ChatTurn? current = null;
        for (int i = 0; i < messages.Count; i++)
        {
            Message message = messages[i];
            if (current == null || (StartsTurn(message) && current.Steps.Count > 0))
            {
                current = new ChatTurn { Key = $"{conversationId}:{i}" };
                turns.Add(current);
            }
            current.Steps.Add(message);
        }
        return turns;
    }

    /// <summary>
    /// One line about a step, e.g. "Akagi used ShowPlan" or "Akagi asks Ren", with <paramref name="name"/> turning a
    /// character id into its name.
    /// </summary>
    public static string Describe(Message message, Func<string?, string> name)
    {
        if (message is not TextMessage text)
        {
            return "Step";
        }
        if (text.Tool != null)
        {
            return $"{name(text.CharacterId)} used {text.Tool.Name}";
        }
        if (Consultation().Match(text.Text) is { Success: true } consultation)
        {
            return $"{consultation.Groups["persona"].Value} asks {consultation.Groups["advisor"].Value}";
        }
        return text.From switch
        {
            Message.Type.User => "You wrote",
            Message.Type.System => "Instruction",
            Message.Type.Notice => "Notice",
            _ => text.Internal ? $"{name(text.CharacterId)} answered" : $"{name(text.CharacterId)} wrote"
        };
    }

    /// <summary>
    /// A step's text without what its title already says: a consultation shows only the request, an instruction
    /// loses its "[System]" mark.
    /// </summary>
    public static string StepText(TextMessage text)
    {
        if (Consultation().Match(text.Text) is { Success: true } consultation)
        {
            string request = text.Text[consultation.Length..];
            return ConsultationReminder().Replace(request, string.Empty).Trim();
        }
        return text.Text.StartsWith(SystemPrefix, StringComparison.Ordinal) ? text.Text[SystemPrefix.Length..] : text.Text;
    }

    private const string SystemPrefix = "[System] ";

    [GeneratedRegex(@"^\[System\] \[Consultation for (?<advisor>.+?) from (?<persona>.+?)\]")]
    private static partial Regex Consultation();

    // The closing line every consultation request carries, telling the advisor whom to answer
    [GeneratedRegex(@"\n[^\n]+: answer [^\n]+ directly and concisely; [^\n]+ will talk to the user\.\s*$")]
    private static partial Regex ConsultationReminder();
}
