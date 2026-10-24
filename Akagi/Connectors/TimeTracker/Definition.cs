namespace Akagi.Connectors.TimeTracker;

public class Definition
{
    public string? Id { get; set; }
    public required string Name { get; set; }
    public required string UserId { get; set; }
    public List<FieldDefinition> Fields { get; set; } = [];
}

public class FieldDefinition
{
    public required string Name { get; set; }
    public FieldType Type { get; set; }
}

public enum FieldType
{
    Boolean,
    Int,
    Float,
    Text,
    DateTime,
    Time,
    Date
}