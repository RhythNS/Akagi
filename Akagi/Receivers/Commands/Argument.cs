namespace Akagi.Receivers.Commands;

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

    public int? IntValue
    {
        get => int.TryParse(Value, out int result) ? result : null;
        set => Value = value?.ToString() ?? "";
    }
    public float? FloatValue
    {
        get => float.TryParse(Value, out float result) ? result : null;
        set => Value = value?.ToString() ?? "";
    }
    public bool? BoolValue
    {
        get => bool.TryParse(Value, out bool result) ? result : null;
        set => Value = value?.ToString() ?? "";
    }
}
