using Akagi.Characters.Cards;
using Akagi.Users;

namespace Akagi.Communication.Commands;

internal class UploadCardCommand : DocumentCommand
{
    public override string Name => "/uploadCard";

    private readonly ICardDatabase _cardDatabase;

    public UploadCardCommand(ICardDatabase cardDatabase)
    {
        _cardDatabase = cardDatabase;
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

            bool success = await _cardDatabase.SaveCardFromImage(stream);
            if (success)
            {
                successNames.Add(document.Name);
            }
        }
        if (successNames.Count == 0)
        {
            await Communicator.SendMessage(user, "No valid cards were uploaded.");
            return;
        }
        string successMessage = $"Successfully uploaded cards: {string.Join(", ", successNames)}";
        await Communicator.SendMessage(user, successMessage);
    }
}
