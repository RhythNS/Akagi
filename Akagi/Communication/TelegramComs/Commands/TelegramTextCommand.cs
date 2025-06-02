using Akagi.Communication.Commands;

namespace Akagi.Communication.TelegramComs.Commands;

internal abstract class TelegramTextCommand : TextCommand, ITelegramCommand
{
    protected ITelegramCommand.Context TelegramContext => _telegramContext ??
        throw new InvalidOperationException("TelegramTextCommand has not been initialized with a Telegram context.");
    private ITelegramCommand.Context? _telegramContext = null!;

    public override Type[] CompatibleFor => [typeof(TelegramService)];

    public void Init(ITelegramCommand.Context telegramContext)
    {
        _telegramContext = telegramContext;
    }
}
