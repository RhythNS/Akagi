using MessagePack;

namespace Akagi.Bridge.Chat.Models;

/// <summary>
/// A tool a character used during a turn, with what it was given and what came back.
/// </summary>
[MessagePackObject]
public class ToolCall
{
    [Key(0)]
    public string Name { get; set; } = string.Empty;
    [Key(1)]
    public List<ToolArgument> Arguments { get; set; } = [];
    [Key(2)]
    public string Output { get; set; } = string.Empty;
}

[MessagePackObject]
public class ToolArgument
{
    [Key(0)]
    public string Name { get; set; } = string.Empty;
    [Key(1)]
    public string Value { get; set; } = string.Empty;
}
