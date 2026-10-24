namespace Akagi.Connectors.TimeTracker;

public class Entry
{
    public string? Id { get; set; }
    public required string DefinitionId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public Dictionary<string, object> Values { get; set; } = [];
}