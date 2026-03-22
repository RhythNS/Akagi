using Microsoft.Extensions.Options;

namespace Akagi.Receivers.Commands;

internal class WriteFileCommand : FileSystemCommand
{
    public WriteFileCommand(IOptionsMonitor<Options> options) : base(options) { }

    public override string Name => "WriteFile";

    public override string Description => "Write content to a file, creating it if it does not exist or overwriting it if it does.";

    public override Argument[] GetDefaultArguments() =>
    [
        new Argument
        {
            Name = "Path",
            Description = "The path to the file to write, relative to the configured base path.",
            IsRequired = true,
            ArgumentType = Argument.Type.String
        },
        new Argument
        {
            Name = "Content",
            Description = "The content to write to the file.",
            IsRequired = true,
            ArgumentType = Argument.Type.String
        }
    ];

    public override bool ContinueAfterExecution => true;

    public override async Task<Command[]> Execute(Context context)
    {
        if (Arguments.Length < 2 ||
            string.IsNullOrWhiteSpace(Arguments[0].Value))
        {
            throw new ArgumentException("Path and Content arguments are required.");
        }

        if (!TryResolvePath(Arguments[0].Value, out string fullPath, out string error))
        {
            context.Conversation.AddMessage(CreateCommandMessage(error));
            return Array.Empty<Command>();
        }

        string content = Arguments[1].Value;

        string? directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllTextAsync(fullPath, content);

        string output = $"Successfully wrote {content.Length} character(s) to \"{fullPath}\".";
        context.Conversation.AddMessage(CreateCommandMessage(output));

        return Array.Empty<Command>();
    }
}
