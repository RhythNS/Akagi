using Akagi.Data;

namespace Akagi.Communication.Commands.Macros;

internal class Macro : Savable
{
    public string Name { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public List<MacroStep> Steps { get; set; } = [];
    public Dictionary<string, string> StaticVariables { get; set; } = [];
    public List<string> DynamicVariableNames { get; set; } = [];
}
