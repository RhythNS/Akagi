using MongoDB.Bson.Serialization.Attributes;
using System.Text.Json.Serialization;

namespace Akagi.Receivers.Commands;

// Stored tool calls from before IntValue, FloatValue and BoolValue were ignored still carry those fields
[BsonIgnoreExtraElements]
internal class Argument
{
    public enum Type
    {
        String,
        Int,
        Float,
        Bool
    }

    public required string Name { init; get; }
    public required string Description { init; get; }
    public bool IsRequired { init; get; }
    public Type ArgumentType { init; get; }
    public string Value { get; set; } = string.Empty;

    // The typed views read and write Value. They must not be stored: on loading, each setter would run after Value
    // and overwrite it with its own (mostly null) value, which emptied every replayed tool call's arguments.
    [BsonIgnore]
    [JsonIgnore]
    public int? IntValue
    {
        get => int.TryParse(Value, out int result) ? result : null;
        set => Value = value?.ToString() ?? "";
    }
    [BsonIgnore]
    [JsonIgnore]
    public float? FloatValue
    {
        get => float.TryParse(Value, out float result) ? result : null;
        set => Value = value?.ToString() ?? "";
    }
    [BsonIgnore]
    [JsonIgnore]
    public bool? BoolValue
    {
        get => bool.TryParse(Value, out bool result) ? result : null;
        set => Value = value?.ToString() ?? "";
    }
}
