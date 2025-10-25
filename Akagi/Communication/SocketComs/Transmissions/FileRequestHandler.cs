using Akagi.Bridge.Chat.Transmissions;
using Akagi.Bridge.Chat.Transmissions.Requests;
using Akagi.Bridge.Chat.Transmissions.Responses;
using Akagi.Characters.Cards;
using Akagi.Data;
using MongoDB.Bson;

namespace Akagi.Communication.SocketComs.Transmissions;

internal class FileRequestHandler : SocketTransmissionHandler
{
    public override string HandlesType => nameof(FileRequestTransmission);

    private readonly IFileDatabase _fileDatabase;
    private readonly ICardDatabase _cardDatabase;

    public FileRequestHandler(IFileDatabase fileDatabase, ICardDatabase cardDatabase)
    {
        _fileDatabase = fileDatabase;
        _cardDatabase = cardDatabase;
    }

    public override async Task ExecuteAsync(Context context, TransmissionWrapper transmissionWrapper)
    {
        FileRequestTransmission fileRequestTransmission = GetTransmission<FileRequestTransmission>(transmissionWrapper);
        string fileUrl = fileRequestTransmission.FileUrl;
        string[] strings = fileUrl.Split('/');

        if (strings.Length < 2)
        {
            ReturnError(context, fileRequestTransmission, "Invalid file URL format.");
            return;
        }

        string path = strings[0];
        string fileExtension = strings[^1].Split('.').LastOrDefault() ?? string.Empty;
        string fileName = strings[^1].Split('.').FirstOrDefault() ?? string.Empty;

        if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(fileExtension) || string.IsNullOrEmpty(fileName))
        {
            ReturnError(context, fileRequestTransmission, "Invalid file URL format.");
            return;
        }

        switch (path)
        {
            case "cards":
                if (strings.Length < 3)
                {
                    ReturnError(context, fileRequestTransmission, "Invalid card file URL.");
                    return;
                }
                string cardId = strings[1];
                Card? card = await _cardDatabase.GetDocumentByIdAsync(cardId);
                if (card == null)
                {
                    ReturnError(context, fileRequestTransmission, "Card not found.");
                    return;
                }
                switch (fileName)
                {
                    case "image":
                        {
                            if (fileExtension != "png")
                            {
                                ReturnError(context, fileRequestTransmission, "Unsupported image format.");
                                return;
                            }

                            using Stream stream = await _fileDatabase.DownloadFileAsync(ObjectId.Parse(card.ImageId));
                            byte[] data = new byte[stream.Length];
                            await stream.ReadAsync(data);
                            /*
                            byte[] data = new byte[9000];
                            for (int i = 0; i < data.Length; i++)
                            {
                                data[i] = 0xFF;
                            }
                             */
                            FileResponseTransmission response = new()
                            {
                                Data = data,
                                FileUrl = fileRequestTransmission.FileUrl,
                                Type = "image"
                            };

                            context.Session.SendTransmission(response);
                            return;
                        }
                    default:
                        ReturnError(context, fileRequestTransmission, "Invalid card request type.");
                        break;
                }
                break;
            default:
                ReturnError(context, fileRequestTransmission, "Unsupported file request path.");
                break;
        }

        ReturnError(context, fileRequestTransmission, "File request could not be processed.");
    }

    private static void ReturnError(Context context, FileRequestTransmission fileRequestTransmission, string error)
    {
        FileResponseTransmission response = new()
        {
            Data = [],
            Error = error,
            FileUrl = fileRequestTransmission.FileUrl,
            Type = "file"
        };

        context.Session.SendTransmission(response);
    }
}
