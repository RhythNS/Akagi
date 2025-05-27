namespace Akagi.Communication.Commands;

internal abstract class DocumentCommand : Command
{
    public abstract Task ExecuteAsync(Context context, Document[] documents, string[] args);
}
