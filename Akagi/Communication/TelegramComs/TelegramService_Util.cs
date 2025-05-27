using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace Akagi.Communication.TelegramComs;

internal partial class TelegramService : Communicator, IHostedService
{
    public async Task<MemoryStream?> LoadFile(FileBase fileBase)
    {
        if (_client == null)
        {
            _logger.LogWarning("Telegram client is not initialized");
            return null;
        }

        TGFile file = await _client.GetFile(fileBase.FileId);
        if (file == null || file.FilePath == null)
        {
            _logger.LogWarning("Failed to get file for FileId {FileId}", fileBase.FileId);
            return null;
        }

        MemoryStream stream = new();
        await _client.DownloadFile(file.FilePath, stream);
        return stream;
    }
}
