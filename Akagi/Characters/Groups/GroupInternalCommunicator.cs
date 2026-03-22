using Akagi.Characters.Conversations;
using Akagi.Communication;
using Akagi.Communication.Commands;
using Akagi.Users;

namespace Akagi.Characters.Groups;

internal class GroupInternalCommunicator : ICommunicator
{
    private readonly ICommunicator _outer;
    private readonly CharacterGroup _group;
    private readonly Character _member;
    private readonly User _user;
    private readonly bool _forwardToUser;

    public string Name => _outer.Name;
    public Command[] AvailableCommands => _outer.AvailableCommands;

    public GroupInternalCommunicator(ICommunicator outer, CharacterGroup group, Character member, User user, bool forwardToUser)
    {
        _outer = outer;
        _group = group;
        _member = member;
        _user = user;
        _forwardToUser = forwardToUser;
    }

    public async Task SendMessage(User user, Character character, string message)
    {
        if (_forwardToUser || _group.UserVisibility.HasFlag(GroupVisibility.Intermediate))
        {
            await _outer.SendMessage(user, character, message);
        }
    }

    public async Task SendMessage(User user, Character character, Message message)
    {
        message.CharacterId ??= character.Id;

        if (_forwardToUser || _group.UserVisibility.HasFlag(GroupVisibility.Intermediate))
        {
            await _outer.SendMessage(user, character, message);
        }
    }

    public Task SendMessage(User user, string message)
    {
        return _outer.SendMessage(user, message);
    }

    public Task SendMessage(User user, Message message)
    {
        return _outer.SendMessage(user, message);
    }

    public async Task SendAudio(User user, Character character, Stream stream, string fileName)
    {
        if (_forwardToUser || _group.UserVisibility.HasFlag(GroupVisibility.Intermediate))
        {
            await _outer.SendAudio(user, character, stream, fileName);
        }
    }

    public Task SendAudio(User user, Stream stream, string fileName)
    {
        return _outer.SendAudio(user, stream, fileName);
    }
}
