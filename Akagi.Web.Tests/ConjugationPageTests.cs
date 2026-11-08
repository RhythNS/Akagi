using Akagi.Bridge.Chat.Models;
using Akagi.Bridge.Chat.Transmissions.Requests;
using Akagi.Bridge.Chat.Transmissions.Responses;
using Akagi.Bridge.Japanese;
using Akagi.Web.Models;
using Akagi.Web.Pages.Learning;
using Akagi.Web.Pages.Learning.Conjugations;
using Akagi.Web.Services.Sockets;
using Akagi.Web.Services.Sockets.Requests;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net;

namespace Akagi.Web.Tests;

/// <summary>
/// The conjugation practice: asking, checking, recording and reading progress, against a client whose requests the
/// test answers itself; and how the drill picks questions.
/// </summary>
public class ConjugationPageTests : BunitContext
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

    private readonly SocketClient _client;
    private readonly HashSet<IRequest> _answered = [];

    public ConjugationPageTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        _client = new SocketClient(IPAddress.Loopback, 1, new User { GoogleId = "g", Name = "Tester", Email = "t" }, "circuit",
            NullLoggerFactory.Instance, [], null!);
        GC.SuppressFinalize(_client);
        Services.AddSingleton<ISocketClientContainer>(new Sockets { Client = _client });
    }

    private ConjugationRequestTransmission Respond(Func<ConjugationRequestTransmission, ConjugationResponseTransmission> answer)
    {
        ConjugationRequest? request = null;
        for (int i = 0; i < 300 && request == null; i++)
        {
            request = _client.GetRequests<ConjugationRequest>().FirstOrDefault(r => !_answered.Contains(r));
            if (request == null)
            {
                Thread.Sleep(10);
            }
        }
        Assert.NotNull(request);
        _answered.Add(request);
        request.Fulfill(answer(request.Transmission));
        return request.Transmission;
    }

    private static readonly ConjugationVerbView Kaku = new() { Id = "書く[かく]", Written = "書く", Reading = "かく", Class = VerbClass.Godan, Meaning = "to write", Rank = 500 };
    private static readonly ConjugationVerbView Taberu = new() { Id = "食べる[たべる]", Written = "食べる", Reading = "たべる", Class = VerbClass.Ichidan, Meaning = "to eat", Rank = 900 };

    private static ConjugationResponseTransmission Loaded(params ConjugationItemView[] items) => new()
    {
        Settings = new ConjugationSettingsView { Forms = [ConjugationForm.Te], Ichidan = false, Irregular = false, ShowEnglish = true },
        Verbs = [Kaku, Taberu],
        Items = items
    };

    private IRenderedComponent<ConjugationPage> Open(ConjugationResponseTransmission loaded)
    {
        IRenderedComponent<ConjugationPage> cut = Render<ConjugationPage>();
        ConjugationRequestTransmission load = Respond(_ => loaded);
        Assert.Equal(ConjugationAction.Load, load.Action);
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("[data-question]")));
        return cut.Settled();
    }

    private async Task<ConjugationRequestTransmission> AnswerAsync(IRenderedComponent<ConjugationPage> cut, string answer, ConjugationItemView? stored = null)
    {
        await cut.Find("[data-answer]").InputAsync(new ChangeEventArgs { Value = answer });
        Task click = cut.Find("[data-check]").ClickAsync(new());
        ConjugationRequestTransmission record = Respond(r => new ConjugationResponseTransmission { Item = stored ?? new ConjugationItemView { Verb = r.Verb, Form = r.Form, Recent = r.Correct ? "1" : "0", Attempts = 1 } });
        await click;
        return record;
    }

    [Fact]
    public async Task A_right_answer_is_recorded_and_counted()
    {
        IRenderedComponent<ConjugationPage> cut = Open(Loaded());
        Assert.Equal("書く[かく]|2|0", cut.Find("[data-question]").GetAttribute("data-question"));
        Assert.Contains("て Form", cut.Find("[data-prompt]").TextContent);
        Assert.Contains("Godan", cut.Find("[data-type]").TextContent);
        Assert.Contains("to write", cut.Find("[data-english]").TextContent);

        ConjugationRequestTransmission record = await AnswerAsync(cut, "kaite");

        Assert.Equal(ConjugationAction.Record, record.Action);
        Assert.Equal("書く[かく]", record.Verb);
        Assert.Equal(ConjugationForm.Te, record.Form);
        Assert.True(record.Correct);
        Assert.Equal("kaite", record.Answer);
        cut.WaitForAssertion(() => Assert.Equal("1/0/100%", cut.Find("[data-score]").TextContent.Trim()));
    }

    [Fact]
    public async Task A_wrong_answer_shows_the_solution_and_lands_in_progress()
    {
        IRenderedComponent<ConjugationPage> cut = Open(Loaded());

        ConjugationRequestTransmission record = await AnswerAsync(cut, "かきて");

        Assert.False(record.Correct);
        cut.WaitForAssertion(() => Assert.Equal("wrong", cut.Find("[data-result]").GetAttribute("data-result")));
        Assert.Contains("いて", cut.Find("[data-solution]").TextContent);
        Assert.Contains("く → いて", cut.Find("[data-result]").TextContent);
        Assert.Equal("0/1/0%", cut.Find("[data-score]").TextContent.Trim());

        Assert.NotEmpty(cut.FindAll("[data-trouble-rule='Te:godan-く']"));
        Assert.NotEmpty(cut.FindAll("[data-trouble-verb='書く[かく]']"));
        await cut.Find("[data-tab='Mistakes']").ClickAsync(new());
        Assert.Contains("かきて", cut.Find("[data-mistakes]").TextContent);
        Assert.Contains("書いて", cut.Find("[data-mistakes]").TextContent);
    }

    [Fact]
    public async Task Drill_narrows_the_questions_to_a_rule()
    {
        ConjugationItemView troubled = new() { Verb = Kaku.Id, Written = "書く", Reading = "かく", Class = VerbClass.Godan, Form = ConjugationForm.Te, Attempts = 3, Correct = 1, Recent = "100", WrongAnswers = ["かきて"] };
        IRenderedComponent<ConjugationPage> cut = Open(Loaded(troubled));

        await cut.Find("[data-trouble-rule='Te:godan-く'] button").ClickAsync(new());

        Assert.Contains("く → いて", cut.Find("[data-focus]").TextContent);
        Assert.Equal("書く[かく]|2|0", cut.Find("[data-question]").GetAttribute("data-question"));
        await cut.Find("[data-focus-clear]").ClickAsync(new());
        Assert.Empty(cut.FindAll("[data-focus]"));
    }

    [Fact]
    public async Task Settings_are_saved_and_change_the_questions()
    {
        IRenderedComponent<ConjugationPage> cut = Open(Loaded());
        Assert.Contains("1 questions", cut.Find("[data-question-count]").TextContent);

        Task toggle = cut.Find("[data-setting='ichidan']").ChangeAsync(new ChangeEventArgs { Value = true });
        ConjugationRequestTransmission saved = Respond(r => new ConjugationResponseTransmission { Settings = r.Settings! });
        await toggle;

        Assert.Equal(ConjugationAction.SaveSettings, saved.Action);
        Assert.True(saved.Settings!.Ichidan);
        Assert.Contains("2 questions", cut.Find("[data-question-count]").TextContent);

        Task none = cut.FindAll("[data-settings] button").First(b => b.TextContent == "None").ClickAsync(new());
        Respond(r => new ConjugationResponseTransmission { Settings = r.Settings! });
        await none;
        Assert.NotEmpty(cut.FindAll("[data-nothing]"));
    }

    [Fact]
    public async Task Reverse_asks_for_the_dictionary_form()
    {
        ConjugationResponseTransmission loaded = Loaded();
        loaded.Settings.Reverse = true;
        IRenderedComponent<ConjugationPage> cut = Open(loaded);
        // 書 carries its furigana か in an rt element
        Assert.Equal("書かいて", cut.Find("[data-question]").TextContent.Trim());
        Assert.Contains("Dictionary Form", cut.Find("[data-prompt]").TextContent);

        ConjugationRequestTransmission record = await AnswerAsync(cut, "かく");
        Assert.True(record.Correct);
        Assert.True(record.Reverse);
    }

    [Fact]
    public void Missed_questions_come_back_and_trouble_is_asked_more()
    {
        ConjugationSettingsView settings = new() { Forms = [ConjugationForm.Te, ConjugationForm.Negative], Ichidan = true };
        ConjugationDrill drill = new([Kaku, Taberu], [], settings, new Random(4));
        DrillQuestion first = drill.Next()!;
        drill.Record(first, "nope", false);
        List<string> next = [.. Enumerable.Range(0, ConjugationDrill.RetryAfter).Select(_ => drill.Next()!.Key)];
        Assert.Contains(first.Key, next);

        Assert.True(ConjugationDrill.Weight(ConjugationMode.Mixed, ConjugationStatus.Trouble, false) > ConjugationDrill.Weight(ConjugationMode.Mixed, ConjugationStatus.New, false));
        Assert.True(ConjugationDrill.Weight(ConjugationMode.Focus, ConjugationStatus.New, true) > ConjugationDrill.Weight(ConjugationMode.Focus, ConjugationStatus.New, false));
        Assert.Equal(0, ConjugationDrill.Weight(ConjugationMode.TroubleOnly, ConjugationStatus.Fine, false));
    }

    [Fact]
    public void The_pool_follows_the_settings()
    {
        ConjugationVerbView heard = new() { Id = "サボる[さぼる]", Written = "サボる", Reading = "さぼる", Class = VerbClass.Godan, PartnerUses = 3 };
        ConjugationDrill drill = new([Kaku, Taberu, heard], [], new ConjugationSettingsView { CommonLimit = 600 });
        Assert.Equal(["書く"], drill.Verbs().Select(v => v.Written));

        drill.Apply(new ConjugationSettingsView { Pool = ConjugationPool.Calls });
        Assert.Equal(["サボる"], drill.Verbs().Select(v => v.Written));

        drill.Apply(new ConjugationSettingsView { Pool = ConjugationPool.Both, CommonLimit = 5_000, Godan = false });
        Assert.Equal(["食べる"], drill.Verbs().Select(v => v.Written));
    }
}
