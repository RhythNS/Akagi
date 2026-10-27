using Akagi.Data;
using Akagi.Graphs;
using System.Text;

namespace Akagi.Communication.Commands.Savables;

internal class UploadGraphCommand : DocumentCommand
{
    public override string Name => "/uploadGraph";

    public override string Description => "Uploads graph JSON files and creates all objects in the database. Graph references are resolved against graphs uploaded before or in the same message. Usage: /uploadGraph <json files> [operation] [name]";

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

        IGraphDefinitionDatabase definitionDatabase = _databaseFactory.GetDatabase<IGraphDefinitionDatabase>();
        List<(Document Document, GraphData Graph)> graphs = [];

        // Store every definition first so graphs in the same upload can reference each other.
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
                using StreamReader reader = new(stream, Encoding.UTF8);
                string json = await reader.ReadToEndAsync();
                GraphData graph = GraphData.Parse(json);
                await definitionDatabase.Store(graph.GraphId, document.Name, json, context.User.Id!);
                graphs.Add((document, graph));
            }
            catch (Exception ex)
            {
                errorMessages.Add($"{document.Name}: {ex.Message}");
            }
        }

        foreach ((Document document, GraphData graph) in graphs)
        {
            try
            {
                GraphLoader loader = new(_databaseFactory)
                {
                    Resolver = new DatabaseGraphResolver(definitionDatabase, context.User.Id!)
                };
                loader.LoadGraph(graph);
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
