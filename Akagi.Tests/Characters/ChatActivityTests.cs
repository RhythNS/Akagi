using Akagi.Characters;
using Akagi.Characters.CharacterBehaviors.MessageCompilers;
using Akagi.Characters.CharacterBehaviors.MessageCompilers.Injections;
using Akagi.Characters.Conversations;
using Akagi.Tests.Learning;
using Akagi.Tests.Support;
using Akagi.Users;
using static Akagi.Bridge.Chat.Transmissions.Responses.ChatActivityResponseTransmission;

namespace Akagi.Tests.Characters;

/// <summary>
/// The live progress the website shows while a turn runs.
/// </summary>
public class ChatActivityTests
{
    private static readonly User User = new() { Id = "user", Name = "Rhyth" };

    private static TextMessage Say(string text, Message.Type from = Message.Type.Character) =>
        new() { From = from, Text = text, Time = DateTime.UtcNow, CharacterId = null };

    private static (ChatActivityHub Hub, List<(string Chat, ActivityKind Kind, string? Text)> Seen) Hub()
    {
        ChatActivityHub hub = new();
        List<(string, ActivityKind, string?)> seen = [];
        hub.Published += (_, chat, kind, message) => seen.Add((chat, kind, (message as TextMessage)?.Text));
        return (hub, seen);
    }

    [Fact]
    public void ATurnReportsItsStepsOnceEachAndEnds()
    {
        (ChatActivityHub hub, List<(string Chat, ActivityKind Kind, string? Text)> seen) = Hub();
        Conversation group = new() { Id = 1, Time = DateTime.UtcNow };
        Conversation memberCopy = new() { Id = 1, Time = DateTime.UtcNow };
        AggregateConversation conversation = new(group, [memberCopy]);

        conversation.AddMessage(Say("before the turn"));
        using (hub.BeginTurn(User, "group"))
        {
            Assert.True(hub.IsBusy("user", "group"));
            conversation.AddMessage(Say("hi", Message.Type.User));
            // A reflection inside the turn joins it instead of starting another
            using (hub.BeginTurn(User, "group"))
            {
                conversation.AddMessage(Say("thinking"));
            }
            conversation.AddMessage(Say("hey"));
        }

        Assert.False(hub.IsBusy("user", "group"));
        Assert.Equal(
        [
            ("group", ActivityKind.TurnStarted, null),
            ("group", ActivityKind.Step, "hi"),
            ("group", ActivityKind.Step, "thinking"),
            ("group", ActivityKind.Step, "hey"),
            ("group", ActivityKind.TurnEnded, null)
        ], seen);
        // The member's copy got every message, it was only reported once
        Assert.Equal(4, memberCopy.Messages.Count);
    }

    [Fact]
    public async Task OnlyTheFlowRunningTheTurnReportsToIt()
    {
        (ChatActivityHub hub, List<(string Chat, ActivityKind Kind, string? Text)> seen) = Hub();
        Conversation conversation = new() { Id = 1, Time = DateTime.UtcNow };

        using (hub.BeginTurn(User, "akagi"))
        {
            // Another request handled at the same time is not part of this turn
            Thread other = new(() => conversation.AddMessage(Say("elsewhere")));
            other.UnsafeStart();
            other.Join();
            await Task.Yield();
            conversation.AddMessage(Say("here"));
        }

        Assert.Equal(["here"], seen.Where(s => s.Kind == ActivityKind.Step).Select(s => s.Text));
    }

    [Fact]
    public async Task BuildingThePromptIsNotAStep()
    {
        // Before every model call the prompt is built from copies of the whole history; reported, the website got the
        // entire conversation again as steps of the turn on every call
        (ChatActivityHub hub, List<(string Chat, ActivityKind Kind, string? Text)> seen) = Hub();
        LifeTestContext life = new();
        Conversation chat = life.Character.Conversations.GetCurrentConversation();
        chat.AddMessage(Say("earlier", Message.Type.User));
        chat.AddMessage(Say("an answer"));

        InMemoryMessageCompilerDatabase database = new();
        MessageCompiler[] steps =
        [
            new ForgetfulMessageCompiler { MaxMessages = 80 },
            new TextMessageInjectionCompiler
            {
                InjectionMessage = Say("injected memory", Message.Type.System),
                Position = InjectionCompiler.InjectionPosition.End
            }
        ];
        foreach (MessageCompiler step in steps)
        {
            await database.SaveDocumentAsync(step);
        }
        LineMessageCompiler line = new() { Definitions = [.. steps.Select(s => new LineMessageCompiler.Definition { MessageCompilerId = s.Id! })] };
        await line.Init(Message.Type.All, database);

        using (hub.BeginTurn(User, "akagi"))
        using (Conversation.BeginInternal())
        {
            chat.AddMessage(Say("new question", Message.Type.User));
            Message[] prompt = await line.CompileAsync(life.Context);
            Conversation scratch = new() { Id = -1, Time = DateTime.UtcNow, IsScratch = true };
            scratch.AddMessage(Say("a call transcript to analyse", Message.Type.System));

            Assert.Equal(4, prompt.Length);
            // Copies keep the messages as they are; only what is new in a consultation becomes internal
            Assert.Equal([false, false, true], chat.Copy().Messages.Select(m => m.Internal));
        }

        Assert.Equal(["new question"], seen.Where(s => s.Kind == ActivityKind.Step).Select(s => s.Text));
        // The compile did not change the chat itself
        Assert.Equal(3, chat.Messages.Count);
    }

    [Fact]
    public void AFailingListenerNeverBreaksATurn()
    {
        ChatActivityHub hub = new();
        hub.Published += (_, _, _, _) => throw new InvalidOperationException("socket gone");
        Conversation conversation = new() { Id = 1, Time = DateTime.UtcNow };

        using (hub.BeginTurn(User, "akagi"))
        {
            conversation.AddMessage(Say("still fine"));
        }

        Assert.Single(conversation.Messages);
        Assert.False(hub.IsBusy("user", "akagi"));
    }
}
