using Telegram.Bot.Types;

namespace Akagi.Communication.TelegramComs;

internal class TelegramDocument : Document, IDisposable
{
    private readonly FileBase fileBase;
    private MemoryStream? _stream;

    public TelegramDocument(FileBase fileBase)
    {
        this.fileBase = fileBase;
    }

    public override string Name => fileBase.FileId;

    public void Dispose()
    {
        _stream?.Dispose();
        _stream = null;
    }

    public override async Task<MemoryStream?> GetStream()
    {
        if (_stream != null)
        {
            return _stream;
        }

        if (Communicator is not TelegramService telegramService)
        {
            throw new InvalidOperationException("Communicator is not a TelegramService.");
        }

        _stream = await telegramService.LoadFile(fileBase);
        return _stream;
    }
}
