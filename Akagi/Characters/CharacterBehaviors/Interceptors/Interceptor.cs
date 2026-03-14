using Akagi.Bridge.Attributes;
using Akagi.Characters.Conversations;
using Akagi.Users;

namespace Akagi.Characters.CharacterBehaviors.Interceptors;

[GraphNode]
internal abstract class Interceptor : CharacterBehavior
{
    public sealed override Task ProcessAsync() => Task.CompletedTask;

    public abstract Task SendMessageAsync(User user, Character character, Message message,
        Func<User, Character, Message, Task> next);
}
