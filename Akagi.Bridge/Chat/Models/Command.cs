using MessagePack;

namespace Akagi.Bridge.Chat.Models;

[MessagePackObject]
public class Command
{
    [Key(0)]
    public required string Name { get; set; }

    [Key(1)]
    public required string Description { get; set; }

    /// <summary>
    /// The description without its "Usage: ..." part.
    /// </summary>
    [Key(2)]
    public string Summary { get; set; } = string.Empty;

    /// <summary>
    /// Folder the command lives in, such as "Life" or "Groups"; "General" for the top level.
    /// </summary>
    [Key(3)]
    public string Category { get; set; } = string.Empty;

    /// <summary>
    /// The command works on attached files.
    /// </summary>
    [Key(4)]
    public bool AcceptsFiles { get; set; }

    [Key(5)]
    public bool AdminOnly { get; set; }

    /// <summary>
    /// The ways the command can be called; most have one, "/session start | /session end" has two.
    /// </summary>
    [Key(6)]
    public CommandForm[] Forms { get; set; } = [];
}

[MessagePackObject]
public class CommandForm
{
    /// <summary>
    /// The usage text this form was read from, such as "/session start [topic]".
    /// </summary>
    [Key(0)]
    public string Usage { get; set; } = string.Empty;

    [Key(1)]
    public CommandParameter[] Parameters { get; set; } = [];
}

public enum CommandParameterKind
{
    /// <summary>A fixed word such as "start" in "/session start".</summary>
    Literal,
    /// <summary>One value; values with spaces are sent as one argument.</summary>
    Text,
    /// <summary>One of <see cref="CommandParameter.Options"/>.</summary>
    Choice,
    /// <summary>The rest of the line, sent as typed.</summary>
    Rest,
    /// <summary>An attached file; not part of the command text.</summary>
    File
}

[MessagePackObject]
public class CommandParameter
{
    /// <summary>
    /// Label for the user, or the word itself for literals.
    /// </summary>
    [Key(0)]
    public string Name { get; set; } = string.Empty;

    [Key(1)]
    public CommandParameterKind Kind { get; set; }

    [Key(2)]
    public bool Optional { get; set; }

    /// <summary>
    /// Named parameters are sent as "Key=value", like "speaker=Me".
    /// </summary>
    [Key(3)]
    public string? Key { get; set; }

    /// <summary>
    /// Shown as placeholder; the command uses it when the parameter is left out.
    /// </summary>
    [Key(4)]
    public string? Default { get; set; }

    [Key(5)]
    public CommandOption[] Options { get; set; } = [];
}

[MessagePackObject]
public class CommandOption
{
    [Key(0)]
    public string Value { get; set; } = string.Empty;

    /// <summary>
    /// Parameters that follow when this option is picked, like the text after "summary" in "end [reflect|summary &lt;text&gt;]".
    /// </summary>
    [Key(1)]
    public CommandParameter[] Parameters { get; set; } = [];
}
