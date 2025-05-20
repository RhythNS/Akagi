using Akagi.Data;
using Akagi.Puppeteers.Commands;
using System.Text.Json.Serialization;

namespace Akagi.Puppeteers.SystemProcessors;

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
}
