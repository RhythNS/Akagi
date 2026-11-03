using MessagePack;

namespace Akagi.Bridge.Chat.Models;

/// <summary>
/// A model a provider offers. Prices are in US dollars per million tokens; null when unknown.
/// </summary>
[MessagePackObject]
public class LLMModel
{
    [Key(0)]
    public string Provider { get; set; } = string.Empty;
    [Key(1)]
    public string Id { get; set; } = string.Empty;
    [Key(2)]
    public string Name { get; set; } = string.Empty;
    [Key(3)]
    public int ContextLength { get; set; }
    [Key(4)]
    public double? PromptPrice { get; set; }
    [Key(5)]
    public double? CompletionPrice { get; set; }
    [Key(6)]
    public bool SupportsTools { get; set; }
}

/// <summary>
/// The model one kind of work runs on. <see cref="Model"/> is null when the user has no preference, so
/// <see cref="DefaultModel"/> applies.
/// </summary>
[MessagePackObject]
public class LLMUsageSetting
{
    [Key(0)]
    public string Usage { get; set; } = string.Empty;
    [Key(1)]
    public string Description { get; set; } = string.Empty;
    /// <summary>
    /// The work consists of tool calls, so a model without tools cannot do it.
    /// </summary>
    [Key(2)]
    public bool NeedsTools { get; set; }
    [Key(3)]
    public string? Provider { get; set; }
    [Key(4)]
    public string? Model { get; set; }
    [Key(5)]
    public string? DefaultProvider { get; set; }
    [Key(6)]
    public string? DefaultModel { get; set; }
}
