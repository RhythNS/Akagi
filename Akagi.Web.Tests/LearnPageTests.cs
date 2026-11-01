using Akagi.Bridge.Chat.Models;
using Akagi.Bridge.Chat.Transmissions.Responses;
using Akagi.Web.Models;
using Akagi.Web.Pages.Learning;
using Akagi.Web.Services.Circuits;
using Akagi.Web.Services.Sockets;
using Akagi.Web.Services.Sockets.Requests;
using Akagi.Web.Services.Sockets.Transmissions;
using Bunit;
using MessagePack;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net;
using Akagi.Bridge.Chat.Transmissions;

namespace Akagi.Web.Tests;

/// <summary>
/// The Learn page against a client whose requests the test answers itself, as the server would.
/// </summary>
public class LearnPageTests : BunitContext
{
    private sealed class Circuit : ICircuitIdAccessor
    {
        public string CircuitId => "circuit";
#pragma warning disable CS0067
        public event Action? CircuitOpened;
#pragma warning restore CS0067
    }

    private sealed class Sockets : ISocketClientContainer
    {
        public SocketClient? Client { get; init; }
        public bool IsConnected => Client != null;
        public event Action? ConnectionStateChanged;
        public Task<SocketClient?> EnsureConnectedAsync() => Task.FromResult(Client);
        public Task TryConnectAsync() => Task.CompletedTask;
        public Task TryDisconnectAsync() => Task.CompletedTask;
        public void Raise() => ConnectionStateChanged?.Invoke();
    }

    private readonly SocketClient _client;

    public LearnPageTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        _client = new SocketClient(IPAddress.Loopback, 1, new User { GoogleId = "g", Name = "Tester", Email = "t" }, "circuit",
            NullLoggerFactory.Instance, [], null!);
        // Never connected; its finalizer would report the removal to a service that does not exist here
        GC.SuppressFinalize(_client);
        Services.AddSingleton<ICircuitIdAccessor>(new Circuit());
    }

    private IRenderedComponent<LearnPage> RenderOnline()
    {
        Services.AddSingleton<ISocketClientContainer>(new Sockets { Client = _client });
        return Render<LearnPage>();
    }

    private static LearningOverview Overview(LearningLesson? current = null) => new()
    {
        Tutor = new Character { Id = "mio", Name = "Mio", IsTutor = true },
        Struggles = [new LearningStruggle { Id = "jp:a", Kind = StruggleKind.Japanese, Title = "に vs で" }],
        Current = current,
        Recent = [new LearningLesson { Id = "old", Topic = "Particles", Status = LessonStatus.Finished, Score = 2, Questions = [new() { Verdict = LessonVerdict.Correct }] }]
    };

    private static LearningLesson Active() => new()
    {
        Id = "l1",
        Mode = LessonMode.MultipleChoice,
        Status = LessonStatus.Active,
        Topic = "Rust",
        Questions = [new LearningQuestion { Prompt = "Which one?", Options = ["a", "b", "c", "d"] }]
    };

    private void Respond(LearningResponseTransmission response)
    {
        LearningRequest? request = null;
        for (int i = 0; i < 200 && request == null; i++)
        {
            request = _client.GetRequests<LearningRequest>().FirstOrDefault();
            if (request == null)
            {
                Thread.Sleep(10);
            }
        }
        Assert.NotNull(request);
        request.Fulfill(response);
    }

    [Fact]
    public void WithoutAConnection_OffersARetry()
    {
        Services.AddSingleton<ISocketClientContainer>(new Sockets());

        IRenderedComponent<LearnPage> cut = Render<LearnPage>();

        cut.WaitForAssertion(() => Assert.Contains("Socket Service Offline", cut.Markup));
    }

    [Fact]
    public void ShowsTheTutorAndSetup_ThenPlaysALesson()
    {
        IRenderedComponent<LearnPage> cut = RenderOnline();
        Respond(new LearningResponseTransmission { Overview = Overview() });

        cut.WaitForAssertion(() => Assert.Contains("Lessons with Mio", cut.Markup));
        Assert.Contains("に vs で", cut.Markup);
        Assert.Contains("Particles", cut.Markup);
        Assert.Contains("/Chat/mio/", cut.Markup);

        cut.Find("[data-lesson-start]").Click();
        cut.WaitForAssertion(() => Assert.Contains("Mio is preparing your lesson", cut.Markup));
        Respond(new LearningResponseTransmission { Lesson = Active() });

        cut.WaitForAssertion(() => Assert.Contains("Which one?", cut.Markup));
        cut.Find("[data-option='1']").Click();

        LearningLesson answered = Active();
        answered.Questions[0].Verdict = LessonVerdict.Correct;
        answered.Questions[0].ChosenOption = 1;
        answered.Questions[0].CorrectOption = 1;
        answered.Questions[0].Feedback = "Exactly.";
        answered.Score = 1;
        Respond(new LearningResponseTransmission { Lesson = answered });

        cut.WaitForAssertion(() => Assert.Contains("Exactly.", cut.Markup));
        cut.Find("[data-lesson-results]").Click();

        answered.Status = LessonStatus.Finished;
        answered.Closing = "Nice work.";
        Respond(new LearningResponseTransmission { Lesson = answered });

        cut.WaitForAssertion(() => Assert.Contains("Nice work.", cut.Markup));
        Assert.Equal("1 / 1", cut.Find("[data-lesson-score]").TextContent.Trim());
    }

    [Fact]
    public void AFailedStart_KeepsItsErrorOnTheSetupScreen()
    {
        IRenderedComponent<LearnPage> cut = RenderOnline();
        Respond(new LearningResponseTransmission { Overview = Overview() });
        cut.WaitForAssertion(() => Assert.Contains("Lessons with Mio", cut.Markup));

        cut.Find("[data-lesson-start]").Click();
        Respond(new LearningResponseTransmission { Error = "Mio could not prepare the lesson." });
        Respond(new LearningResponseTransmission { Overview = Overview() });

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Mio could not prepare the lesson.", cut.Find(".alert").TextContent);
            Assert.NotEmpty(cut.FindAll("[data-lesson-start]"));
        });
    }

    [Fact]
    public void AnOpenLesson_IsResumed()
    {
        IRenderedComponent<LearnPage> cut = RenderOnline();

        Respond(new LearningResponseTransmission { Overview = Overview(Active()) });

        cut.WaitForAssertion(() => Assert.Contains("Which one?", cut.Markup));
    }

    [Fact]
    public async Task Responses_ReachOnlyTheRequestWithTheirId()
    {
        LearningRequest first = new(new());
        LearningRequest second = new(new());
        Task<LearningResponseTransmission> waitingFirst = first.Get(_client, TimeSpan.FromSeconds(5));
        Task<LearningResponseTransmission> waitingSecond = second.Get(_client, TimeSpan.FromSeconds(5));

        LearningResponseTransmission response = new() { RequestId = second.RequestId, Error = "for the second" };
        new LearningResponseHandler().Execute(new SocketTransmissionHandler.Context { SocketService = null!, SocketClient = _client }, new TransmissionWrapper
        {
            MessageType = response.MessageType,
            Version = 1,
            Payload = MessagePackSerializer.Serialize(response)
        });

        Assert.Equal("for the second", (await waitingSecond).Error);
        Assert.False(waitingFirst.IsCompleted);

        first.Fulfill(new LearningResponseTransmission());
        await waitingFirst;
    }
}
