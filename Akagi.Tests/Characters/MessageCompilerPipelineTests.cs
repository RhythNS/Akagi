using Akagi.Characters;
using Akagi.Characters.CharacterBehaviors.MessageCompilers;
using Akagi.Characters.Conversations;
using Akagi.Tests.Learning;
using System.Globalization;

namespace Akagi.Tests.Characters;

public class MessageCompilerPipelineTests
{
    private static Conversation Chat() => new()
    {
        Id = 1,
        Time = new DateTime(2026, 11, 7, 19, 0, 0, DateTimeKind.Utc),
        Messages =
        [
            new TextMessage { CharacterId = null, From = Message.Type.User, Text = "Hello can you hear me?", Time = new DateTime(2026, 11, 7, 19, 10, 0, DateTimeKind.Utc) },
            new TextMessage { CharacterId = "akagi", From = Message.Type.Character, Text = "I'm right here.", Time = new DateTime(2026, 11, 7, 19, 10, 20, DateTimeKind.Utc) }
        ]
    };

    private static async Task<LineMessageCompiler> Pipeline(Message.Type readable, params MessageCompiler[] steps)
    {
        InMemoryMessageCompilerDatabase database = new();
        foreach (MessageCompiler step in steps)
        {
            await database.SaveDocumentAsync(step);
        }
        LineMessageCompiler line = new() { Definitions = [.. steps.Select(s => new LineMessageCompiler.Definition { MessageCompilerId = s.Id! })] };
        await line.Init(readable, database);
        return line;
    }

    [Fact]
    public async Task StepsReadTheProcessorsMessageTypes()
    {
        // Akagi's pipeline starts with "Recent messages"; uninitialized, it read no type and dropped the whole chat
        LineMessageCompiler line = await Pipeline(Message.Type.All, new ForgetfulMessageCompiler { MaxMessages = 80 });

        List<Conversation> compiled = await line.FilterCompileAsync(null!, [Chat()]);

        Assert.Equal(["Hello can you hear me?", "I'm right here."], compiled.SelectMany(c => c.Messages).OfType<TextMessage>().Select(m => m.Text));
    }

    [Fact]
    public async Task StepsKeepOnlyWhatTheProcessorMayRead()
    {
        // Mood Reflection reads only the user's own messages
        LineMessageCompiler line = await Pipeline(Message.Type.User, new ForgetfulMessageCompiler { MaxMessages = 30 });

        List<Conversation> compiled = await line.FilterCompileAsync(null!, [Chat()]);

        Assert.Equal(["Hello can you hear me?"], compiled.SelectMany(c => c.Messages).OfType<TextMessage>().Select(m => m.Text));
    }

    [Theory]
    [InlineData("[ddd HH:mm]", "[Sat 19:10]")]
    [InlineData("ddd HH:mm", "[Sat 19:10]")]
    public void TimeStamps_AreBracketedOnce(string format, string expected)
    {
        CultureInfo culture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        try
        {
            Assert.Equal(expected, AddTimeStampCompiler.Stamp(new DateTime(2026, 11, 7, 19, 10, 0), format));
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
        }
    }
}
