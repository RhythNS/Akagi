using Akagi.Bridge.Chat.Models;
using Akagi.Web.Models;
using Akagi.Web.Models.Chat;
using Akagi.Web.Pages.Chat;
using Akagi.Web.Services.Sockets;
using Akagi.Web.Services.Sockets.Requests;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net;
using TabBlazor.Services;
using static Akagi.Bridge.Chat.Transmissions.Responses.ChatActivityResponseTransmission;
using Character = Akagi.Web.Models.Chat.Character;
using Conversation = Akagi.Web.Models.Chat.Conversation;
using TextMessage = Akagi.Web.Models.Chat.TextMessage;

namespace Akagi.Web.Tests;

/// <summary>
/// The chat page with a group: each turn can be opened up to show its steps, and a running turn shows what the
/// characters are doing.
/// </summary>
public class ChatPageTests : BunitContext
{
    private sealed class Sockets : ISocketClientContainer
    {
        public SocketClient? Client { get; init; }
        public bool IsConnected => Client != null;
#pragma warning disable CS0067
        public event Action? ConnectionStateChanged;
#pragma warning restore CS0067
        public Task<SocketClient?> EnsureConnectedAsync() => Task.FromResult(Client);
        public Task TryConnectAsync() => Task.CompletedTask;
        public Task TryDisconnectAsync() => Task.CompletedTask;
    }

    private static readonly DateTime Time = new(2026, 11, 15, 15, 18, 0, DateTimeKind.Utc);

    private readonly SocketClient _client;
    private readonly HashSet<IRequest> _answered = [];

    public ChatPageTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        _client = new SocketClient(IPAddress.Loopback, 1, new User { GoogleId = "g", Name = "Tester", Email = "t" }, "circuit",
            NullLoggerFactory.Instance, [], null!);
        GC.SuppressFinalize(_client);
        Services.AddSingleton<ISocketClientContainer>(new Sockets { Client = _client });
        Services.AddSingleton<ToastService>();
    }

    private T Answer<T>(Action<T> fulfill) where T : class, IRequest
    {
        T? request = null;
        for (int i = 0; i < 300 && request == null; i++)
        {
            request = _client.GetRequests<T>().FirstOrDefault(r => !_answered.Contains(r));
            if (request == null)
            {
                Thread.Sleep(10);
            }
        }
        Assert.NotNull(request);
        _answered.Add(request);
        fulfill(request);
        return request;
    }

    private static TextMessage Say(Message.Type from, string text, string? characterId = null, bool isInternal = false, int second = 0) =>
        new() { From = from, Text = text, Time = Time.AddSeconds(second), CharacterId = characterId, Internal = isInternal };

    private static TextMessage Tool(string name, string output, string characterId, int second) => new()
    {
        From = Message.Type.Character,
        Time = Time.AddSeconds(second),
        CharacterId = characterId,
        Internal = characterId == "ren",
        Tool = new ToolCall { Name = name, Output = output, Arguments = [new ToolArgument { Name = "Project", Value = "Move out" }] }
    };

    private IRenderedComponent<ChatCharacterChatPage> Open(bool busy = false)
    {
        IRenderedComponent<ChatCharacterChatPage> cut = Render<ChatCharacterChatPage>(p => p.Add(x => x.CharacterId, "group1"));
        Answer<CharacterListRequest>(r => r.Fulfill([new Character { Id = "group1", Name = "Companion", IsGroup = true, MemberIds = ["akagi", "ren"] }]));
        Answer<ConversationRequest>(r => r.Fulfill(new ConversationHistory(
        [
            new Conversation
            {
                Id = 1,
                Time = Time,
                Messages =
                [
                    Say(Message.Type.User, "Do we need to think of anything else?"),
                    Say(Message.Type.System, "[System] [Consultation for Ren from Akagi] Update the board", isInternal: true, second: 1),
                    Tool("AddTicketUpdate", "Added update 2 to #23.", "ren", 2),
                    Say(Message.Type.Character, "Both tickets updated.", "ren", isInternal: true, second: 3),
                    Say(Message.Type.Character, "Done, both are on the board!", "akagi", second: 4),
                ]
            }
        ], busy)));
        Answer<CharacterListRequest>(r => r.Fulfill(
        [
            new Character { Id = "akagi", Name = "Akagi" },
            new Character { Id = "ren", Name = "Ren" }
        ]));
        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll(".bubble").Count));
        return cut;
    }

    [Fact]
    public void ATurnOpensUpFromAnyOfItsMessages()
    {
        IRenderedComponent<ChatCharacterChatPage> cut = Open();

        Assert.Equal(["Do we need to think of anything else?", "Done, both are on the board!"], cut.FindAll(".bubble-text").Select(b => b.TextContent.Trim()));
        Assert.Equal(["3 steps", "3 steps"], cut.FindAll(".step-toggle").Select(b => b.TextContent.Trim()));
        Assert.Empty(cut.FindAll(".turn-steps"));

        cut.FindAll(".step-toggle")[1].Click();

        Assert.Equal(["You wrote", "Akagi asks Ren", "Ren used AddTicketUpdate", "Ren answered", "Akagi wrote"],
            cut.FindAll(".turn-step .step-title").Select(t => t.TextContent));
        Assert.All(cut.FindAll(".step-toggle"), b => Assert.Equal("Hide steps", b.TextContent.Trim()));

        cut.FindAll(".step-toggle")[0].Click();
        Assert.Empty(cut.FindAll(".turn-steps"));
    }

    [Fact]
    public void ARunningTurnShowsEachStepAsItHappens()
    {
        IRenderedComponent<ChatCharacterChatPage> cut = Open();

        _client.OnChatActivityInternal("group1", ActivityKind.TurnStarted, null);
        _client.OnChatActivityInternal("group1", ActivityKind.Step, Say(Message.Type.User, "Is the plan done?", second: 10));
        _client.OnChatActivityInternal("group1", ActivityKind.Step, Say(Message.Type.System, "[System] [Consultation for Ren from Akagi] Show the plan", isInternal: true, second: 11));
        _client.OnChatActivityInternal("group1", ActivityKind.Step, Tool("ShowPlan", "10 open tickets", "ren", 12));
        // Another chat's progress is not shown here
        _client.OnChatActivityInternal("akagi", ActivityKind.Step, Tool("RecordMood", "Recorded", "akagi", 13));

        cut.WaitForAssertion(() => Assert.Contains("Companion is working… · Ren used ShowPlan", cut.Find(".turn-status.is-live").TextContent));
        Assert.Contains("Generating", cut.Find(".chat-header").TextContent);
        Assert.Equal("Is the plan done?", cut.FindAll(".bubble-text").Last().TextContent.Trim());

        cut.Find(".turn-status.is-live").Click();
        Assert.Equal(["You wrote", "Akagi asks Ren", "Ren used ShowPlan"], cut.FindAll(".turn-steps .turn-step:not(.live) .step-title").Select(t => t.TextContent));
        Assert.NotNull(cut.Find(".turn-step.live"));

        // The reply comes as a message and as a step: one bubble
        TextMessage reply = Say(Message.Type.Character, "Yes, it finishes Jan 4.", "akagi", second: 14);
        _client.OnMessageRecievedInternal("akagi", reply);
        _client.OnChatActivityInternal("group1", ActivityKind.Step, reply);
        _client.OnChatActivityInternal("group1", ActivityKind.TurnEnded, null);

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll(".turn-status.is-live")));
        Assert.Single(cut.FindAll(".bubble-text"), b => b.TextContent.Trim() == "Yes, it finishes Jan 4.");
        Assert.DoesNotContain("Generating", cut.Find(".chat-header").TextContent);
        Assert.Empty(cut.FindAll(".turn-step.live"));
    }

    [Fact]
    public void StepsTheChatAlreadyHoldsAreNotAddedAgain()
    {
        IRenderedComponent<ChatCharacterChatPage> cut = Open();

        _client.OnChatActivityInternal("group1", ActivityKind.TurnStarted, null);
        _client.OnChatActivityInternal("group1", ActivityKind.Step, Say(Message.Type.User, "Is the plan done?", second: 10));
        // The history sent again, as an older bot did on every model call
        _client.OnChatActivityInternal("group1", ActivityKind.Step, Say(Message.Type.System, "[System] [Consultation for Ren from Akagi] Update the board", isInternal: true, second: 1));
        _client.OnChatActivityInternal("group1", ActivityKind.Step, Tool("AddTicketUpdate", "Added update 2 to #23.", "ren", 2));
        _client.OnChatActivityInternal("group1", ActivityKind.Step, Tool("ShowPlan", "10 open tickets", "ren", 12));

        cut.WaitForAssertion(() => Assert.Contains("Ren used ShowPlan", cut.Find(".turn-status.is-live").TextContent));
        cut.Find(".turn-status.is-live").Click();
        Assert.Equal(["You wrote", "Ren used ShowPlan"], cut.FindAll(".turn-steps .turn-step:not(.live) .step-title").Select(t => t.TextContent));
    }

    [Fact]
    public void OpeningAChatWhileATurnRunsShowsThatItIsWorking()
    {
        IRenderedComponent<ChatCharacterChatPage> cut = Open(busy: true);

        Assert.Contains("Companion is working…", cut.Find(".turn-status.is-live").TextContent);
    }
}
