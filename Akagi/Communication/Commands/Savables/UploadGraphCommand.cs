using Akagi.Data;
using Akagi.Graphs;
using System.Text;

namespace Akagi.Communication.Commands.Savables;

internal class UploadGraphCommand : DocumentCommand
{
    public override string Name => "/uploadGraph";

    public override string Description =>
        "Uploads graph JSON files and creates their objects. Send a bundle exported by the CharacterEditor (or --bundle), " +
        "or a graph together with its subgraphs in any order: only graphs no other uploaded graph references are created, " +
        "the rest are stored so references resolve. --all creates every uploaded graph. " +
        "Usage: /uploadGraph <json files> create|update [name] [--all]";

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
        bool instantiateAll = args.Any(a => string.Equals(a, "--all", StringComparison.OrdinalIgnoreCase));
        args = [.. args.Where(a => !string.Equals(a, "--all", StringComparison.OrdinalIgnoreCase))];
        if (args.Length == 0)
        {
            await Communicator.SendMessage(context.User, "Please specify an operation: create or update.");
            return CommandResult.Fail("No operation specified.");
        }
        if (Enum.TryParse(args[0], true, out OperationType operation) == false)
        {
            await Communicator.SendMessage(context.User, "Invalid operation specified. Please use create or update.");
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
        List<GraphUploadPlan.UploadedGraph> uploads = [];

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
                uploads.Add(new GraphUploadPlan.UploadedGraph(document.Name, GraphData.Parse(json)));
            }
            catch (Exception ex)
            {
                errorMessages.Add($"{document.Name}: {ex.Message}");
            }
        }

        // Store every definition first, so the order of the files does not matter for references
        GraphUploadPlan plan = GraphUploadPlan.Create(uploads, instantiateAll);
        foreach (GraphUploadPlan.Definition definition in plan.Definitions)
        {
            await definitionDatabase.Store(definition.GraphId, definition.FileName, definition.Json, context.User.Id!);
        }

        foreach (GraphUploadPlan.UploadedGraph upload in plan.Roots)
        {
            string documentName = upload.FileName;
            GraphData graph = upload.Graph;
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
                    successNames.Add($"{documentName} ({createdObjects.Count} objects)");
                }
                else
                {
                    errorMessages.Add($"{documentName}: No objects created");
                }
            }
            catch (Exception ex)
            {
                errorMessages.Add($"{documentName}: {ex.Message}");
            }
        }

        StringBuilder response = new();
        int embedded = uploads.Sum(u => u.Graph.Embedded?.Count ?? 0);
        if (plan.Subgraphs.Count > 0 || embedded > 0)
        {
            response.AppendLine($"Stored {plan.Subgraphs.Count + embedded} subgraph(s) for references:");
            foreach ((GraphUploadPlan.UploadedGraph subgraph, string usedBy) in plan.Subgraphs)
            {
                response.AppendLine($"  - {subgraph.FileName} (used by {usedBy})");
            }
            foreach (GraphUploadPlan.UploadedGraph bundle in uploads.Where(u => u.Graph.IsBundle))
            {
                response.AppendLine($"  - {string.Join(", ", bundle.Graph.Embedded!.Select(e => e.Path))} (embedded in {bundle.FileName})");
            }
            response.AppendLine();
        }

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
