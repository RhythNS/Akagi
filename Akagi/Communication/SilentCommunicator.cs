using Akagi.Characters;
using Akagi.Characters.Conversations;
using Akagi.Users;
using CommunicationCommand = Akagi.Communication.Commands.Command;

namespace Akagi.Communication;

/// <summary>
/// For LLM runs in the background: nothing they produce is sent to the user directly.
/// </summary>
internal sealed class SilentCommunicator : ICommunicator
{
    public string Name => "silent";
    public CommunicationCommand[] AvailableCommands => [];
    public Task SendMessage(User user, Character character, string message) => Task.CompletedTask;
    public Task SendMessage(User user, Character character, Message message) => Task.CompletedTask;
    public Task SendMessage(User user, string message) => Task.CompletedTask;
    public Task SendMessage(User user, Message message) => Task.CompletedTask;
    public Task SendAudio(User user, Character character, Stream stream, string fileName) => Task.CompletedTask;
    public Task SendAudio(User user, Stream stream, string fileName) => Task.CompletedTask;
}
