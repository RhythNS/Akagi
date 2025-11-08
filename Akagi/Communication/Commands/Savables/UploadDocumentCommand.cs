using Akagi.Data;

namespace Akagi.Communication.Commands.Savables;

internal abstract class UploadDocumentCommand<T> : DocumentCommand where T : Savable
{
    public enum SaveType
    {
        File,
        BSON
    }

    protected abstract IDatabase<T> Database { get; }
    protected virtual SaveType SaveMethod => SaveType.File;

    public override async Task ExecuteAsync(Context context, Document[] documents, string[] args)
    {
        if (documents.Length == 0)
        {
            await Communicator.SendMessage(context.User, "Please upload valid files or images.");
            return;
        }
        List<string> successNames = [];
        foreach (Document document in documents)
        {
            using MemoryStream? stream = await document.GetStream();
            if (stream == null)
            {
                continue;
            }
            bool success = false;
            success = SaveMethod switch
            {
                SaveType.File => await Database.SaveFromFile(stream),
                SaveType.BSON => await Database.SaveFromBSON(stream),
                _ => throw new Exception($"Invalid save method: {SaveMethod}"),
            };
            if (success)
            {
                successNames.Add(document.Name);
            }
        }
        if (successNames.Count == 0)
        {
            await Communicator.SendMessage(context.User, "No valid files found.");
            return;
        }
        string successMessage = $"Successfully uploaded: {string.Join(", ", successNames)}";
        await Communicator.SendMessage(context.User, successMessage);
    }
}
