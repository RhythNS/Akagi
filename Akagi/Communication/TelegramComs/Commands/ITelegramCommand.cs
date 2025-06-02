using Akagi.Data;

namespace Akagi.Communication.TelegramComs.Commands;

internal interface ITelegramCommand
{
    public class Context : ContextBase
    {
        public required Telegram.Bot.Types.Message Message { get; init; }

        protected override Savable?[] ToTrack => [];
    }

    public void Init(Context context);
}
