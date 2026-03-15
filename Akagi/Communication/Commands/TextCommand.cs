namespace Akagi.Communication.Commands;

internal abstract class TextCommand : Command
{
    public abstract Task<CommandResult> ExecuteAsync(Context context, string[] args);
}
