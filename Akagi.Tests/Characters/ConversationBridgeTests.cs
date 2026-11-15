using Akagi.Characters;
using Akagi.Characters.Conversations;
using Akagi.Receivers.Commands.Life;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using BridgeMessage = Akagi.Bridge.Chat.Models.TextMessage;

namespace Akagi.Tests.Characters;

/// <summary>
/// The chat history the website receives.
/// </summary>
public class ConversationBridgeTests
{
    [Fact]
    public void ToolCallsComeWithTheirArgumentsAndVoiceMessagesShowTheirText()
    {
        DateTime time = new(2026, 11, 3, 20, 0, 0, DateTimeKind.Utc);
        Conversation conversation = new() { Id = 1, Time = time };
        conversation.AddMessage(new TextMessage { From = Message.Type.User, Text = "Hi", Time = time, CharacterId = null });
        RecordMoodCommand mood = new();
        mood.Arguments = mood.GetDefaultArguments();
        mood.Arguments.Single(a => a.Name == "Note").Value = "tired";
        conversation.AddMessage(new CommandMessage { Command = mood, Output = new string('x', BridgeMessages.MaxOutputLength + 10), From = Message.Type.System, Time = time, CharacterId = "akagi" });
        conversation.AddMessage(new VoiceMessage { From = Message.Type.Character, Content = "Hey you", VoiceId = "v1", Time = time, CharacterId = null });
        conversation.AddMessage(new VoiceMessage { From = Message.Type.User, VoiceId = "v2", Time = time, CharacterId = null });

        Akagi.Bridge.Chat.Models.Conversation bridge = conversation.ToBridgeModel();

        Assert.Equal(["Hi", "Hey you", "(voice message)"], bridge.Messages.Where(m => m.Tool == null).Select(m => m.Text));
        Assert.Equal(BridgeMessage.Type.Character, bridge.Messages[2].From);
        Akagi.Bridge.Chat.Models.ToolCall tool = bridge.Messages[1].Tool!;
        Assert.Equal("RecordMood", tool.Name);
        Assert.Equal("akagi", bridge.Messages[1].CharacterId);
        // Only the arguments it was given
        Assert.Equal("tired", Assert.Single(tool.Arguments).Value);
        Assert.EndsWith("… (10 more characters)", tool.Output);
    }

    [Fact]
    public void AConsultedAdvisorsAnswerComesFlaggedAsInternal()
    {
        DateTime time = new(2026, 11, 14, 20, 29, 0, DateTimeKind.Utc);
        Conversation persona = new() { Id = 1, Time = time };
        Conversation group = new() { Id = 1, Time = time };
        AggregateConversation conversation = new(group, [persona]);
        conversation.AddMessage(new TextMessage { From = Message.Type.User, Text = "Add the workout import to Life", Time = time, CharacterId = null });
        using (Conversation.BeginInternal())
        {
            conversation.AddMessage(new TextMessage { From = Message.Type.System, Text = "[System] [Consultation for Ren from Akagi] add it", Time = time, CharacterId = null });
            conversation.AddMessage(new CommandMessage { Command = new RecordMoodCommand(), Output = "Created #1", From = Message.Type.Character, Time = time, CharacterId = "ren" });
            conversation.AddMessage(new TextMessage { From = Message.Type.Character, Text = "Done, created #1.", Time = time, CharacterId = "ren" });
        }
        conversation.AddMessage(new TextMessage { From = Message.Type.Character, Text = "I made my first ticket!", Time = time, CharacterId = "akagi" });

        // The website shows the chat and keeps the rest for when a turn is opened up
        List<BridgeMessage> bridge = conversation.ToBridgeModel().Messages;
        Assert.Equal(["Add the workout import to Life", "I made my first ticket!"], bridge.Where(m => !m.Internal).Select(m => m.Text));
        Assert.Equal(["[System] [Consultation for Ren from Akagi] add it", "", "Done, created #1."], bridge.Where(m => m.Internal).Select(m => m.Text));
        Assert.Equal(bridge.Count, persona.ToBridgeModel().Messages.Count);
        // The members' models still read the answer as the advisor's own words
        TextMessage answer = group.Messages.OfType<TextMessage>().Single(m => m.CharacterId == "ren");
        Assert.Equal(Message.Type.Character, answer.From);
        Assert.True(answer.Internal);
        Assert.True(answer.Copy().Internal);
    }

    [Fact]
    public void OnlyInternalMessagesStoreTheFlag()
    {
        DateTime time = new(2026, 11, 14, 20, 29, 0, DateTimeKind.Utc);
        TextMessage shown = new() { From = Message.Type.Character, Text = "Hi", Time = time, CharacterId = null };
        TextMessage hidden = new() { From = Message.Type.Character, Text = "Hi", Time = time, CharacterId = null, Internal = true };

        Assert.False(shown.ToBsonDocument().Contains(nameof(Message.Internal)));
        Assert.True(hidden.ToBsonDocument()[nameof(Message.Internal)].AsBoolean);
        Assert.True(BsonSerializer.Deserialize<TextMessage>(hidden.ToBsonDocument()).Internal);
    }
}
