using Akagi.Users;

namespace Akagi.Communication.Commands;

internal abstract class TextCommand : Command
{
    public abstract Task ExecuteAsync(User user, string[] args);
}
