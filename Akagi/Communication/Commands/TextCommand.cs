namespace Akagi.Communication.Commands;

internal abstract class TextCommand : Command
{
    public abstract Task ExecuteAsync(Context context, string[] args);
}
