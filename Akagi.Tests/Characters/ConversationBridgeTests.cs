using Akagi.Characters;
using Akagi.Characters.Conversations;
using Akagi.Receivers.Commands.Life;
using BridgeMessage = Akagi.Bridge.Chat.Models.TextMessage;

namespace Akagi.Tests.Characters;

/// <summary>
/// The chat history the website receives.
/// </summary>
public class ConversationBridgeTests
{
    [Fact]
    public void ToolCallsAreLeftOutAndVoiceMessagesShowTheirText()
    {
        DateTime time = new(2026, 11, 3, 20, 0, 0, DateTimeKind.Utc);
        Conversation conversation = new() { Id = 1, Time = time };
        conversation.AddMessage(new TextMessage { From = Message.Type.User, Text = "Hi", Time = time, CharacterId = null });
        conversation.AddMessage(new CommandMessage { Command = new RecordMoodCommand(), Output = "Recorded checkin", From = Message.Type.System, Time = time, CharacterId = null });
        conversation.AddMessage(new VoiceMessage { From = Message.Type.Character, Content = "Hey you", VoiceId = "v1", Time = time, CharacterId = null });
        conversation.AddMessage(new VoiceMessage { From = Message.Type.User, VoiceId = "v2", Time = time, CharacterId = null });

        Akagi.Bridge.Chat.Models.Conversation bridge = conversation.ToBridgeModel();

        Assert.Equal(["Hi", "Hey you", "(voice message)"], bridge.Messages.Select(m => m.Text));
        Assert.Equal(BridgeMessage.Type.Character, bridge.Messages[1].From);
    }
}
