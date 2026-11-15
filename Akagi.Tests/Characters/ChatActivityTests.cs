using Akagi.Characters;
using Akagi.Characters.Conversations;
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
