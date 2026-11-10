using Akagi.Web.Models.Chat;
using BridgeType = Akagi.Bridge.Chat.Models.TextMessage.Type;

namespace Akagi.Web.Tests;

/// <summary>
/// The chat shows messages by who sent them: the user, the character and the bot's notices, not instructions for the character.
/// </summary>
public class ChatMessageTests
{
    private static TextMessage From(BridgeType type) =>
        TextMessage.FromBridgeMessage(new Bridge.Chat.Models.TextMessage { From = type, Text = "text", Time = DateTime.UtcNow });

    [Fact]
    public void Only_instructions_for_the_character_stay_out_of_the_chat()
    {
        Assert.True(From(BridgeType.User).IsShownInChat);
        Assert.True(From(BridgeType.Character).IsShownInChat);
        Assert.True(From(BridgeType.Notice).IsShownInChat);
        Assert.False(From(BridgeType.System).IsShownInChat);
    }

    [Fact]
    public void Notices_survive_the_round_trip()
    {
        Assert.Equal(Message.Type.Notice, From(BridgeType.Notice).From);
        Assert.Equal(BridgeType.Notice, From(BridgeType.Notice).ToBridgeMessage().From);
    }
}
