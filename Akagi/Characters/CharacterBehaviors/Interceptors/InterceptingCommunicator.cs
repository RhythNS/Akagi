using Akagi.Characters.Conversations;
using Akagi.Communication;
using Akagi.Communication.Commands;
using Akagi.Users;

namespace Akagi.Characters.CharacterBehaviors.Interceptors;

internal sealed class InterceptingCommunicator : ICommunicator
{
    private readonly ICommunicator _inner;
    private readonly Interceptor[] _interceptors;
    private readonly Func<User, Character, Message, Task> _pipeline;

    public string Name => _inner.Name;
    public Command[] AvailableCommands => _inner.AvailableCommands;

    public InterceptingCommunicator(ICommunicator inner, Interceptor[] interceptors)
    {
        _inner = inner;
        _interceptors = interceptors;

        Func<User, Character, Message, Task> pipeline = (u, c, m) => _inner.SendMessage(u, c, m);

        for (int i = interceptors.Length - 1; i >= 0; i--)
        {
            var next = pipeline;
            var interceptor = interceptors[i];
            pipeline = (u, c, m) => interceptor.SendMessageAsync(u, c, m, next);
        }

        _pipeline = pipeline;
    }

    public InterceptingCommunicator CreateFrom(Interceptor from)
    {
        int index = Array.IndexOf(_interceptors, from);

        if (index < 0)
        {
            throw new ArgumentException("The given interceptor was not found in the pipeline.", nameof(from));
        }

        return new InterceptingCommunicator(_inner, _interceptors[(index + 1)..]);
    }

    public Task SendMessage(User user, Character character, Message message) =>
        _pipeline(user, character, message);

    public Task SendMessage(User user, Character character, string message) =>
        _inner.SendMessage(user, character, message);

    public Task SendMessage(User user, string message) =>
        _inner.SendMessage(user, message);

    public Task SendMessage(User user, Message message) =>
        _inner.SendMessage(user, message);

    public Task SendAudio(User user, Character character, Stream stream, string fileName) =>
        _inner.SendAudio(user, character, stream, fileName);

    public Task SendAudio(User user, Stream stream, string fileName) =>
        _inner.SendAudio(user, stream, fileName);
}