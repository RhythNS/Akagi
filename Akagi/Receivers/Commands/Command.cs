using Akagi.Characters.Conversations;

namespace Akagi.Receivers.Commands;

internal abstract class Command
{
    public abstract string Name { get; }
    public abstract string Description { get; }
    public abstract bool ContinueAfterExecution { get; }
    public Argument[] Arguments { get; set; } = [];
    public Message.Type From { get; set; } = 0;
    public string? FromCharacterId { get; set; }

    public abstract Argument[] GetDefaultArguments();

    public abstract Task<Command[]> Execute(Context context);

    public void SetFrom(Message.Type from, string fromCharacterId)
    {
        From = from;
        FromCharacterId = fromCharacterId;
    }

    /// <summary>
    /// Value of the named argument, or null when it is missing or blank.
    /// </summary>
    protected string? GetArgument(string name)
    {
        string? value = Arguments.FirstOrDefault(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase))?.Value;
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    protected string RequireArgument(string name)
    {
        return GetArgument(name) ?? throw new ArgumentException($"Argument '{name}' is required for {Name}.");
    }

    protected int? GetIntArgument(string name)
    {
        double? value = GetNumberArgument(name);
        return value.HasValue ? (int)Math.Round(value.Value) : null;
    }

    /// <summary>
    /// Parses numbers culture independently; LLMs send "6.5" regardless of the server locale.
    /// </summary>
    protected double? GetNumberArgument(string name)
    {
        string? value = GetArgument(name);
        if (value == null)
        {
            return null;
        }
        if (double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double invariant))
        {
            return invariant;
        }
        return double.TryParse(value, out double local) ? local : null;
    }

    protected bool? GetBoolArgument(string name)
    {
        string? value = GetArgument(name);
        return value != null && bool.TryParse(value, out bool result) ? result : null;
    }

    protected CommandMessage CreateCommandMessage(string output)
    {
        return new CommandMessage
        {
            Command = this,
            Output = output,
            From = From,
            CharacterId = FromCharacterId,
            Time = DateTime.UtcNow
        };
    }
}
