using Akagi.Users;

namespace Akagi.Communication.Commands;

internal abstract class DocumentCommand : Command
{
    public abstract Task ExecuteAsync(User user, Document[] documents, string[] args);
}
