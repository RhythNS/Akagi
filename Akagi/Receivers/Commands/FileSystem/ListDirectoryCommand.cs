using Microsoft.Extensions.Options;
using System.Text;

namespace Akagi.Receivers.Commands;

internal class ListDirectoryCommand : FileSystemCommand
{
    public ListDirectoryCommand(IOptionsMonitor<Options> options) : base(options) { }

    public override string Name => "ListDirectory";

    public override string Description => "List the files and subdirectories inside a directory.";

    public override Argument[] GetDefaultArguments() =>
    [
        new Argument
        {
            Name = "Path",
            Description = "The path to the directory to list, relative to the configured base path.",
            IsRequired = true,
            ArgumentType = Argument.Type.String
        },
        new Argument
        {
            Name = "Recursive",
            Description = "Whether to list contents recursively (true) or only the top-level entries (false, default).",
            IsRequired = false,
            ArgumentType = Argument.Type.Bool
        }
    ];

    public override bool ContinueAfterExecution => true;

    public override Task<Command[]> Execute(Context context)
    {
        if (Arguments.Length < 1 || string.IsNullOrWhiteSpace(Arguments[0].Value))
        {
            throw new ArgumentException("Path argument is required and cannot be empty.");
        }

        if (!TryResolvePath(Arguments[0].Value, out string fullPath, out string error))
        {
            context.Conversation.AddMessage(CreateCommandMessage(error));
            return Task.FromResult(Array.Empty<Command>());
        }

        if (!Directory.Exists(fullPath))
        {
            context.Conversation.AddMessage(CreateCommandMessage($"Directory not found: \"{fullPath}\""));
            return Task.FromResult(Array.Empty<Command>());
        }

        bool recursive = Arguments.Length > 1 && (Arguments[1].BoolValue ?? false);
        SearchOption searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

        string[] directories = Directory.GetDirectories(fullPath, "*", searchOption);
        string[] files = Directory.GetFiles(fullPath, "*", searchOption);

        StringBuilder sb = new();
        sb.AppendLine($"Listing of \"{fullPath}\" ({(recursive ? "recursive" : "top-level")}):");

        foreach (string dir in directories)
        {
            sb.AppendLine($"  [DIR]  {dir}");
        }
        foreach (string file in files)
        {
            sb.AppendLine($"  [FILE] {file}");
        }

        sb.AppendLine($"Total: {directories.Length} director{(directories.Length == 1 ? "y" : "ies")}, {files.Length} file{(files.Length == 1 ? "" : "s")}.");

        context.Conversation.AddMessage(CreateCommandMessage(sb.ToString().TrimEnd()));

        return Task.FromResult(Array.Empty<Command>());
    }
}
