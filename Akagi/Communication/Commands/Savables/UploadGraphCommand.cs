using Akagi.Data;
using Akagi.Graphs;
using System.Text;

namespace Akagi.Communication.Commands.Savables;

internal class UploadGraphCommand : DocumentCommand
{
    public override string Name => "/uploadGraph";

    public override string Description => "Uploads a graph JSON file and creates all objects in the database. Usage: /uploadGraph <json file> [operation] [name]";

    private readonly IDatabaseFactory _databaseFactory;

    private enum OperationType
    {
        Update,
        Create
    }

    public UploadGraphCommand(IDatabaseFactory databaseFactory)
    {
        _databaseFactory = databaseFactory;
    }

    public override async Task<CommandResult> ExecuteAsync(Context context, Document[] documents, string[] args)
    {
        if (documents.Length == 0)
        {
            await Communicator.SendMessage(context.User, "Please upload a valid JSON graph file.");
            return CommandResult.Fail("No documents provided.");
        }
        if (args.Length == 0)
        {
            await Communicator.SendMessage(context.User, "Please specify an operation: New or Update.");
            return CommandResult.Fail("No operation specified.");
        }
        if (Enum.TryParse(args[0], true, out OperationType operation) == false)
        {
            await Communicator.SendMessage(context.User, "Invalid operation specified. Please use New or Update.");
            return CommandResult.Fail("Invalid operation.");
        }
        string? graphName = null;
        if (args.Length > 1)
        {
            graphName = args[1];
        }
        if (operation == OperationType.Create && string.IsNullOrWhiteSpace(graphName))
        {
            await Communicator.SendMessage(context.User, "Please specify a name for the new graph.");
            return CommandResult.Fail("No graph name specified.");
        }

        List<string> successNames = [];
        List<string> errorMessages = [];

        foreach (Document document in documents)
        {
            using MemoryStream? stream = await document.GetStream();

            if (stream == null)
            {
                errorMessages.Add($"{document.Name}: Failed to read file");
                continue;
            }

            try
            {
                if (stream.Length == 0)
                {
                    errorMessages.Add($"{document.Name}: File is empty");
                    continue;
                }

                stream.Position = 0;

                GraphLoader loader = new(_databaseFactory);
                await loader.LoadGraphFromStream(stream);
                List<Savable> createdObjects = [];
                createdObjects = operation switch
                {
                    OperationType.Update => await loader.Update(context.User.Id!, graphName),
                    OperationType.Create => await loader.Create(context.User.Id!, graphName!),
                    _ => throw new InvalidOperationException("Unsupported operation"),
                };
                if (createdObjects.Count > 0)
                {
                    successNames.Add($"{document.Name} ({createdObjects.Count} objects)");
                }
                else
                {
                    errorMessages.Add($"{document.Name}: No objects created");
                }
            }
            catch (Exception ex)
            {
                errorMessages.Add($"{document.Name}: {ex.Message}");
            }
        }

        StringBuilder response = new();

        if (successNames.Count > 0)
        {
            response.AppendLine($"Successfully loaded graphs:");
            foreach (string name in successNames)
            {
                response.AppendLine($"  - {name}");
            }
        }

        if (errorMessages.Count > 0)
        {
            if (response.Length > 0)
            {
                response.AppendLine();
            }
            response.AppendLine($"Failed to load:");
            foreach (string error in errorMessages)
            {
                response.AppendLine($"  - {error}");
            }
        }

        if (successNames.Count == 0 && errorMessages.Count == 0)
        {
            response.AppendLine("No valid graph files were uploaded.");
        }

        await Communicator.SendMessage(context.User, response.ToString().TrimEnd());
        return successNames.Count > 0 ? CommandResult.Ok : CommandResult.Fail("No graphs uploaded successfully.");
    }
}
