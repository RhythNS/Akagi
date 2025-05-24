using Akagi.Puppeteers.SystemProcessors;
using Akagi.Users;

namespace Akagi.Communication.Commands;

internal class UploadSystemProcessorCommand : DocumentCommand
{
    public override string Name => "/uploadSystemProcessor";

    private readonly ISystemProcessorDatabase _systemProcessorDatabase;

    public UploadSystemProcessorCommand(ISystemProcessorDatabase systemProcessorDatabase)
    {
        _systemProcessorDatabase = systemProcessorDatabase;
    }

    public override async Task ExecuteAsync(User user, Document[] documents, string[] args)
    {
        if (documents.Length == 0)
        {
            await Communicator.SendMessage(user, "Please upload valid files or images.");
            return;
        }

        List<string> successNames = [];
        foreach (Document document in documents)
        {
            MemoryStream? stream = await document.GetStream();

            if (stream == null)
            {
                continue;
            }

            bool success = await _systemProcessorDatabase.SaveSystemProcessorFromFile(stream);
            if (success)
            {
                successNames.Add(document.Name);
            }
        }
        if (successNames.Count == 0)
        {
            await Communicator.SendMessage(user, "No valid system processors were uploaded.");
            return;
        }

        string successMessage = $"Successfully uploaded cards: {string.Join(", ", successNames)}";
        await Communicator.SendMessage(user, successMessage);
    }
}
