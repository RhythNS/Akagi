using Akagi.Data;
using Akagi.Graphs;
using System.Text;

namespace Akagi.Communication.Commands.Savables;

internal class UploadGraphCommand : DocumentCommand
{
    public override string Name => "/uploadGraph";

    public override string Description => "Uploads a graph JSON file and creates all objects in the database. Usage: /uploadGraph <json file>";

    private readonly IDatabaseFactory _databaseFactory;

    public UploadGraphCommand(IDatabaseFactory databaseFactory)
    {
        _databaseFactory = databaseFactory;
    }

    public override async Task ExecuteAsync(Context context, Document[] documents, string[] args)
    {
        if (documents.Length == 0)
        {
            await Communicator.SendMessage(context.User, "Please upload a valid JSON graph file.");
            return;
        }

        GraphLoader loader = new(_databaseFactory);
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

                List<Savable> createdObjects = await loader.LoadGraphFromStreamForUser(stream, context.User.Id);

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
    }
}
