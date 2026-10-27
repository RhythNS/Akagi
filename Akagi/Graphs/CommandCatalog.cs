using Akagi.Receivers.Commands;
using Akagi.Utils;
using System.Runtime.CompilerServices;

namespace Akagi.Graphs;

/// <summary>
/// The LLM tools (Receivers commands) a SystemProcessor can list in CommandNames.
/// </summary>
internal static class CommandCatalog
{
    public class CommandInfo
    {
        public string Name { get; init; } = string.Empty;
        public string Description { get; init; } = string.Empty;
        public bool ContinueAfterExecution { get; init; }
        public List<ArgumentInfo> Arguments { get; init; } = [];
    }

    public class ArgumentInfo
    {
        public string Name { get; init; } = string.Empty;
        public string Type { get; init; } = string.Empty;
        public bool Required { get; init; }
        public string Description { get; init; } = string.Empty;
    }

    private static List<CommandInfo>? _commands;

    public static IReadOnlyList<CommandInfo> Commands => _commands ??= Build();

    public static bool Exists(string name) => Commands.Any(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));

    private static List<CommandInfo> Build()
    {
        List<CommandInfo> commands = [];
        foreach (Type type in TypeUtils.GetNonAbstractTypesExtendingFrom<Command>())
        {
            Command? command = Instantiate(type);
            if (command == null)
            {
                continue;
            }

            Argument[] arguments;
            try
            {
                arguments = command.GetDefaultArguments();
            }
            catch
            {
                arguments = [];
            }

            commands.Add(new CommandInfo
            {
                Name = command.Name,
                Description = command.Description,
                ContinueAfterExecution = command.ContinueAfterExecution,
                Arguments = [.. arguments.Select(a => new ArgumentInfo
                {
                    Name = a.Name,
                    Type = a.ArgumentType.ToString(),
                    Required = a.IsRequired,
                    Description = a.Description
                })]
            });
        }
        return [.. commands.OrderBy(c => c.Name, StringComparer.Ordinal)];
    }

    /// <summary>
    /// Commands are normally created by DI. Ones with constructor dependencies are created without running a constructor;
    /// their Name, Description and arguments do not depend on injected services.
    /// </summary>
    private static Command? Instantiate(Type type)
    {
        try
        {
            return type.GetConstructor(Type.EmptyTypes) != null
                ? (Command)Activator.CreateInstance(type)!
                : (Command)RuntimeHelpers.GetUninitializedObject(type);
        }
        catch
        {
            return null;
        }
    }
}
