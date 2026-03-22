using Microsoft.Extensions.Options;

namespace Akagi.Receivers.Commands;

internal class ReadFileCommand : FileSystemCommand
{
    public ReadFileCommand(IOptionsMonitor<Options> options) : base(options) { }

    public override string Name => "ReadFile";

    public override string Description => "Read the contents of a file from the filesystem.";

    public override Argument[] GetDefaultArguments() =>
    [
        new Argument
        {
            Name = "Path",
            Description = "The path to the file to read, relative to the configured base path.",
            IsRequired = true,
            ArgumentType = Argument.Type.String
        }
    ];

    public override bool ContinueAfterExecution => true;

    public override async Task<Command[]> Execute(Context context)
    {
        if (Arguments.Length < 1 || string.IsNullOrWhiteSpace(Arguments[0].Value))
        {
            throw new ArgumentException("Path argument is required and cannot be empty.");
        }

        if (!TryResolvePath(Arguments[0].Value, out string fullPath, out string error))
        {
            context.Conversation.AddMessage(CreateCommandMessage(error));
            return Array.Empty<Command>();
        }

        if (!File.Exists(fullPath))
        {
            context.Conversation.AddMessage(CreateCommandMessage($"File not found: \"{fullPath}\""));
            return Array.Empty<Command>();
        }

        string contents = await File.ReadAllTextAsync(fullPath);
        string output = $"Contents of \"{fullPath}\":\n{contents}";
        context.Conversation.AddMessage(CreateCommandMessage(output));

        return Array.Empty<Command>();
    }
}
