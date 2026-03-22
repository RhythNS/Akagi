using Microsoft.Extensions.Options;

namespace Akagi.Receivers.Commands;

internal class DeleteFileCommand : FileSystemCommand
{
    public DeleteFileCommand(IOptionsMonitor<Options> options) : base(options) { }

    public override string Name => "DeleteFile";

    public override string Description => "Delete a file from the filesystem.";

    public override Argument[] GetDefaultArguments() =>
    [
        new Argument
        {
            Name = "Path",
            Description = "The path to the file to delete, relative to the configured base path.",
            IsRequired = true,
            ArgumentType = Argument.Type.String
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

        if (!File.Exists(fullPath))
        {
            context.Conversation.AddMessage(CreateCommandMessage($"File not found: \"{fullPath}\""));
            return Task.FromResult(Array.Empty<Command>());
        }

        File.Delete(fullPath);

        string output = $"Successfully deleted \"{fullPath}\".";
        context.Conversation.AddMessage(CreateCommandMessage(output));

        return Task.FromResult(Array.Empty<Command>());
    }
}
