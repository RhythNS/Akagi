using Akagi.Characters;
using Akagi.Characters.CharacterBehaviors.MessageCompilers;
using Akagi.Characters.Conversations;
using Akagi.Receivers.Commands.Messages;
using System.Globalization;

namespace Akagi.Tests.Characters;

/// <summary>
/// Replies that copy the "[Sun 14:43]" stamps: what is taken off, what a streamed reply shows, and what the model
/// sees of its own earlier replies.
/// </summary>
public class TimeStampTests
{
    [Theory]
    [InlineData("[Sun 14:43] [Sun 14:43] RAG tuning", "RAG tuning")]
    [InlineData("  [Sat 19:29]\nFiddling with my memory", "Fiddling with my memory")]
    [InlineData("[2026-11-08 14:43] Morning", "Morning")]
    [InlineData("Loud and clear [Sun 14:43]", "Loud and clear [Sun 14:43]")]
    [InlineData("[link](https://example.com) here", "[link](https://example.com) here")]
    [InlineData("[sighs] fine", "[sighs] fine")]
    public void Only_leading_stamps_with_a_time_come_off(string reply, string kept)
    {
        Assert.Equal(kept, MessageStamps.StripLeading(reply));
    }

    [Fact]
    public void A_streamed_stamp_is_never_shown()
    {
        LeadingStampFilter filter = new();
        string[] chunks = ["[Sun", " 14:4", "3] [Sun 14:43]", " RAG", " tuning"];
        string shown = string.Concat(chunks.Select(filter.Push)) + filter.Flush();
        Assert.Equal("RAG tuning", shown);

        LeadingStampFilter plain = new();
        Assert.Equal("Loud", plain.Push("Loud"));
        Assert.Equal(" and clear", plain.Push(" and clear"));

        LeadingStampFilter bracket = new();
        Assert.Equal(string.Empty, bracket.Push("[sigh"));
        Assert.Equal("[sighs] fine", bracket.Push("s] fine"));

        LeadingStampFilter onlyStamp = new();
        Assert.Equal(string.Empty, onlyStamp.Push("[Sun 14:43] "));
        Assert.Equal(string.Empty, onlyStamp.Flush());
    }

    [Fact]
    public async Task A_reply_is_stored_without_the_stamps()
    {
        Conversation conversation = new() { Id = 1, Messages = [] };
        TextMessageCommand reply = new() { From = Message.Type.Character, FromCharacterId = "akagi" };
        reply.SetMessage("[Sun 14:43] [Sun 14:43] RAG tuning");

        await reply.Execute(new Akagi.Receivers.Context { Conversation = conversation, Character = null!, User = null!, Communicator = null!, LLMFactory = null!, DatabaseFactory = null! });

        Assert.Equal("RAG tuning", Assert.IsType<TextMessage>(Assert.Single(conversation.Messages)).Text);
    }

    private static Conversation History() => new()
    {
        Id = 1,
        Messages =
        [
            new TextMessage { CharacterId = null, From = Message.Type.User, Text = "It's probably fine.", Time = new DateTime(2026, 11, 8, 14, 43, 13, DateTimeKind.Utc) },
            new TextMessage { From = Message.Type.Character, CharacterId = "akagi", Text = "[Sat 19:29] Fiddling with my memory", Time = new DateTime(2026, 11, 8, 14, 43, 22, DateTimeKind.Utc) }
        ]
    };

    [Fact]
    public async Task The_model_sees_stamps_on_the_users_messages_but_not_its_own()
    {
        CultureInfo culture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        try
        {
            List<Conversation> compiled = await new AddTimeStampCompiler { Format = "[ddd HH:mm]" }.FilterCompileAsync(null!, [History()]);
            string[] texts = [.. compiled[0].Messages.OfType<TextMessage>().Select(m => m.Text)];
            string stamp = AddTimeStampCompiler.Stamp(AddTimeStampCompiler.LocalTime(new DateTime(2026, 11, 8, 14, 43, 13, DateTimeKind.Utc), TimeZoneInfo.Local), "[ddd HH:mm]");
            Assert.Equal([$"{stamp} It's probably fine.", "Fiddling with my memory"], texts);

            List<Conversation> all = await new AddTimeStampCompiler { Format = "[ddd HH:mm]", StampOwnMessages = true }.FilterCompileAsync(null!, [History()]);
            Assert.Matches(@"^\[Sun \d\d:43\] Fiddling with my memory$", all[0].Messages.OfType<TextMessage>().Last().Text);
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
        }
    }

    [Fact]
    public void Stamps_are_in_the_users_time_zone()
    {
        TimeZoneInfo berlin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");
        DateTime local = AddTimeStampCompiler.LocalTime(new DateTime(2026, 11, 8, 14, 43, 0, DateTimeKind.Utc), berlin);
        Assert.Equal(new DateTime(2026, 11, 8, 15, 43, 0), local);
    }
}
