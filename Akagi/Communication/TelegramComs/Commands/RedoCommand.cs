namespace Akagi.Communication.TelegramComs.Commands;

internal class RedoCommand : TelegramTextCommand
{
    public override string Name => "/redo";

    public override string Description => "Reply to a message to redo this command.";

    public override async Task ExecuteAsync(Context context, string[] args)
    {
        if (TelegramContext.Message.ReplyToMessage == null)
        {
            await Communicator.SendMessage(context.User, "Please reply to a message to redo it");
            return;
        }
        await (Communicator as TelegramService)!.HandleCommand(TelegramContext.Message.ReplyToMessage, context.User);
    }
}
