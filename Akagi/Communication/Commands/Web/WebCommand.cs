namespace Akagi.Communication.Commands.Web;

internal abstract class WebCommand : TextCommand
{
    protected readonly string UserNotConfiguredError = "User is not connected to akagi.web!";

    protected async Task<string?> TryGetGoogleIdOrPrintError(Context context)
    {
        string? googleId = context.User?.GoogleUser?.Id;
        if (googleId == null)
        {
            await Communicator.SendMessage(context.User!, UserNotConfiguredError);
            return googleId;
        }

        return googleId;
    }
}
