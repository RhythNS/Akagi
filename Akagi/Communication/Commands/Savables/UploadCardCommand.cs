using Akagi.Characters.Cards;

namespace Akagi.Communication.Commands.Savables;

internal class UploadCardCommand : DocumentCommand
{
    public override string Name => "/uploadCard";

    public override string Description => "Uploads a card image to create or update a character's card. Usage: /uploadCard <image files>";

    private readonly ICardDatabase _cardDatabase;

    public UploadCardCommand(ICardDatabase cardDatabase)
    {
        _cardDatabase = cardDatabase;
    }

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

            bool success = await _cardDatabase.SaveCardFromImage(stream);
            if (success)
            {
                successNames.Add(document.Name);
            }
        }
        if (successNames.Count == 0)
        {
            await Communicator.SendMessage(context.User, "No valid cards were uploaded.");
            return;
        }
        string successMessage = $"Successfully uploaded cards: {string.Join(", ", successNames)}";
        await Communicator.SendMessage(context.User, successMessage);
    }
}
