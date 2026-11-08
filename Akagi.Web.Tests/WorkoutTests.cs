using Akagi.Web.Models;
using Akagi.Web.Models.TimeTrackers;
using Akagi.Web.Models.Workouts;
using Akagi.Web.Pages.Workouts;
using Akagi.Web.Services.Workouts;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Akagi.Web.Tests;

internal sealed class FakeClock : TimeProvider
{
    public DateTime UtcNow { get; set; } = new(2026, 11, 8, 18, 0, 0, DateTimeKind.Utc);
    public override DateTimeOffset GetUtcNow() => new(UtcNow);
    public void Advance(double seconds) => UtcNow = UtcNow.AddSeconds(seconds);
}

internal sealed class FakeWorkouts : IWorkoutService
{
    public List<WorkoutRoutine> Routines { get; } = [];
    public List<Definition> Trackers { get; } = [];
    public List<WorkoutResult> Logged { get; } = [];

    public Task<List<WorkoutRoutine>> ListAsync(string userId) => Task.FromResult(Routines.Where(r => r.UserId == userId).ToList());
    public Task<WorkoutRoutine?> GetAsync(string userId, string id) => Task.FromResult(Routines.FirstOrDefault(r => r.Id == id && r.UserId == userId));
    public Task<WorkoutRoutine> SaveAsync(WorkoutRoutine routine)
    {
        routine.Id ??= Guid.NewGuid().ToString("N")[..24];
        Routines.RemoveAll(r => r.Id == routine.Id);
        Routines.Add(routine);
        return Task.FromResult(routine);
    }
    public Task DeleteAsync(string userId, string id)
    {
        Routines.RemoveAll(r => r.Id == id);
        return Task.CompletedTask;
    }
    public Task<List<Definition>> TrackersAsync(string userId) => Task.FromResult(Trackers.ToList());
    public Task<Definition?> LogAsync(string userId, WorkoutRoutine routine, WorkoutResult result)
    {
        Logged.Add(result);
        return Task.FromResult<Definition?>(new Definition { Id = "d1", Name = WorkoutLog.DefinitionName, UserId = userId });
    }
}

/// <summary>
/// The interval timer: how a routine is laid out and when sounds play, pausing and coming back after a refresh, the
/// time tracker entry, and the editor and runner pages.
/// </summary>
public class WorkoutTests : BunitContext
{
    private const string UserId = "6adcc28c4f351a3450e30ab7";

    /// <summary>
    /// The example from the request: 15 s to get ready, two 30 s works with 10 s rest between, no rest at the end.
    /// </summary>
    private static WorkoutRoutine Example(bool skipLastRest = true) => new()
    {
        Id = "r1", UserId = UserId, Name = "Example", StartSeconds = 15, WorkSeconds = 30, RestSeconds = 10, Rounds = 2, SkipLastRest = skipLastRest
    };

    [Fact]
    public void The_example_runs_start_work_rest_work()
    {
        WorkoutPlan plan = new(Example());

        Assert.Equal(
            [(SegmentKind.Start, 0.0, 15.0), (SegmentKind.Work, 15, 30), (SegmentKind.Rest, 45, 10), (SegmentKind.Work, 55, 30)],
            plan.Segments.Select(s => (s.Kind, s.Start, s.Duration)));
        Assert.Equal(85, plan.Total);
        Assert.Equal(
            [(0.0, WorkoutCue.GetReady), .. Countdown(15), (15, WorkoutCue.Go), (35, WorkoutCue.AlmostThere), .. Countdown(45),
             (45, WorkoutCue.RestTime), .. Countdown(55), (55, WorkoutCue.Go), (75, WorkoutCue.AlmostThere), .. Countdown(85), (85, WorkoutCue.GreatJob)],
            plan.Cues());

        WorkoutPlan withLastRest = new(Example(skipLastRest: false));
        Assert.Equal(95, withLastRest.Total);
        Assert.Equal(SegmentKind.Rest, withLastRest.Segments[^1].Kind);
    }

    [Fact]
    public void Short_works_and_no_countdown_leave_out_their_sounds()
    {
        WorkoutPlan plan = new(new WorkoutRoutine { StartSeconds = 0, WorkSeconds = 10, RestSeconds = 0, Rounds = 2 });
        Assert.Equal([(0.0, WorkoutCue.Go), .. Countdown(10), (10, WorkoutCue.Go), .. Countdown(20), (20, WorkoutCue.GreatJob)], plan.Cues());
    }

    /// <summary>
    /// 5, 4, 3, 2, 1 in the last five seconds of a stage that ends at <paramref name="end"/>.
    /// </summary>
    private static (double, WorkoutCue)[] Countdown(double end) =>
        [(end - 5, WorkoutCue.Count5), (end - 4, WorkoutCue.Count4), (end - 3, WorkoutCue.Count3), (end - 2, WorkoutCue.Count2), (end - 1, WorkoutCue.Count1)];

    [Fact]
    public void Long_rests_say_get_ready_ten_seconds_before_they_end()
    {
        WorkoutPlan plan = new(new WorkoutRoutine { StartSeconds = 0, WorkSeconds = 20, RestSeconds = 15, Rounds = 2, SkipLastRest = true });
        // Work 0-20, rest 20-35, work 35-55
        Assert.Contains((25.0, WorkoutCue.GetReady), plan.Cues());
        Assert.Equal([WorkoutCue.GetReady], plan.CuesBetween(24.9, 25.1));
        Assert.Equal([WorkoutCue.Count3], plan.CuesBetween(31.9, 32.1));

        WorkoutPlan shortRest = new(new WorkoutRoutine { StartSeconds = 0, WorkSeconds = 20, RestSeconds = 14, Rounds = 2, SkipLastRest = true });
        Assert.DoesNotContain(shortRest.Cues(), c => c.Cue == WorkoutCue.GetReady);
    }

    [Fact]
    public void A_stage_shorter_than_the_countdown_counts_only_what_it_has()
    {
        WorkoutPlan plan = new(new WorkoutRoutine { StartSeconds = 3, WorkSeconds = 30, RestSeconds = 0, Rounds = 1 });
        // The 3 s start: get ready at 0, then 2 and 1; the 3 would fall on the get ready
        Assert.Equal([(0.0, WorkoutCue.GetReady), (1, WorkoutCue.Count2), (2, WorkoutCue.Count1), (3, WorkoutCue.Go)], plan.Cues().Take(4));
    }

    [Fact]
    public void Sounds_play_once_and_a_gap_plays_only_the_latest()
    {
        WorkoutPlan plan = new(Example());
        Assert.Equal([WorkoutCue.GetReady], plan.CuesBetween(-1, 0));
        Assert.Empty(plan.CuesBetween(0, 9.9));
        Assert.Equal([WorkoutCue.Count5], plan.CuesBetween(9.9, 10.1));
        Assert.Equal([WorkoutCue.Go], plan.CuesBetween(14.9, 15.1));
        Assert.Equal([WorkoutCue.RestTime], plan.CuesBetween(15.1, 47));
    }

    [Fact]
    public void Position_counts_what_is_done()
    {
        WorkoutPosition position = new WorkoutPlan(Example()).At(50);
        Assert.Equal(SegmentKind.Rest, position.Segment!.Kind);
        Assert.Equal(5, position.SegmentRemaining);
        Assert.Equal(1, position.CompletedRounds);
        Assert.Equal(30, position.WorkSeconds);
        Assert.Equal(5, position.RestSeconds);
        Assert.Equal(SegmentKind.Work, position.Next!.Kind);

        WorkoutPosition end = new WorkoutPlan(Example()).At(200);
        Assert.True(end.Finished);
        Assert.Equal(2, end.CompletedRounds);
        Assert.Equal(60, end.WorkSeconds);
    }

    [Theory]
    [InlineData("90", 90)]
    [InlineData("1:30", 90)]
    [InlineData(" 0:05 ", 5)]
    [InlineData("1:00:00", 3600)]
    [InlineData("1:75", null)]
    [InlineData("abc", null)]
    [InlineData("-5", null)]
    public void Durations_read_as_seconds_or_minutes(string text, int? seconds)
    {
        Assert.Equal(seconds, WorkoutPlan.Parse(text));
    }

    [Fact]
    public void Pausing_stops_the_clock()
    {
        DateTime t = new(2026, 11, 8, 18, 0, 0, DateTimeKind.Utc);
        WorkoutRun run = WorkoutRun.Start(UserId, Example(), t);
        run.Pause(t.AddSeconds(20));
        Assert.Equal(20, run.Elapsed(t.AddSeconds(45)));
        run.Resume(t.AddSeconds(50));
        Assert.Equal(30, run.Elapsed(t.AddSeconds(60)));

        run.Finish(t.AddSeconds(70), 40, aborted: true);
        Assert.Equal(40, run.Elapsed(t.AddSeconds(500)));
    }

    [Fact]
    public void A_refresh_continues_where_it_was_and_a_long_absence_pauses()
    {
        DateTime t = new(2026, 11, 8, 18, 0, 0, DateTimeKind.Utc);
        WorkoutRun refreshed = WorkoutRun.Deserialize(WorkoutRun.Start(UserId, Example(), t).Serialize())!;
        refreshed.LastSeenUtc = t.AddSeconds(10);
        refreshed.Restore(t.AddSeconds(13));
        Assert.False(refreshed.Paused);
        Assert.Equal(10, refreshed.Elapsed(t.AddSeconds(13)), 3);

        WorkoutRun away = WorkoutRun.Start(UserId, Example(), t);
        away.LastSeenUtc = t.AddSeconds(10);
        away.Restore(t.AddMinutes(30));
        Assert.True(away.Paused);
        Assert.Equal(10, away.Elapsed(t.AddHours(2)), 3);
        Assert.Equal("Example", away.Routine.Name);
    }

    [Fact]
    public void The_workouts_tracker_gets_every_field()
    {
        DateTime t = new(2026, 11, 8, 18, 0, 0, DateTimeKind.Utc);
        WorkoutRun run = WorkoutRun.Start(UserId, Example(), t);
        WorkoutPlan plan = new(run.Routine);
        run.Finish(t.AddSeconds(60), 60, aborted: true);

        WorkoutResult done = WorkoutResult.From(run, plan, asCompleted: false);
        // 60 s in: get ready 15, work 30, rest 10, and 5 s into the second work
        Assert.Equal((1, 2, 35, 10, 60, false), (done.Rounds, done.PlannedRounds, done.WorkSeconds, done.RestSeconds, done.ActiveSeconds, done.Completed));
        WorkoutResult full = WorkoutResult.From(run, plan, asCompleted: true);
        Assert.Equal((2, 60, 10, 85, true), (full.Rounds, full.WorkSeconds, full.RestSeconds, full.ActiveSeconds, full.Completed));

        Entry entry = WorkoutLog.Build(new Definition { Id = "d1", Name = "Workouts", UserId = UserId, Fields = WorkoutLog.Fields() }, done);
        Assert.Equal("Example", entry.Values["Workout"]);
        Assert.Equal(DateOnly.FromDateTime(t.ToLocalTime()), entry.Values["Date"]);
        Assert.Equal(TimeOnly.FromDateTime(t.ToLocalTime()), entry.Values["Start"]);
        Assert.Equal(TimeOnly.FromDateTime(t.AddSeconds(60).ToLocalTime()), entry.Values["End"]);
        Assert.Equal(1, entry.Values["Rounds"]);
        Assert.Equal(35, entry.Values["Work seconds"]);
        Assert.Equal(false, entry.Values["Completed"]);
    }

    [Fact]
    public void Another_tracker_gets_the_fields_it_has_by_name()
    {
        WorkoutResult result = new("Tabata", new DateTime(2026, 11, 8, 19, 0, 0), new DateTime(2026, 11, 8, 19, 4, 0), 8, 8, 160, 70, 240, true);
        Definition mine = new()
        {
            Id = "d2", Name = "Sport", UserId = UserId,
            Fields = [new() { Name = "Activity", Type = FieldType.Text }, new() { Name = "Minutes", Type = FieldType.Float },
                      new() { Name = "Start Time", Type = FieldType.Time }, new() { Name = "Notes", Type = FieldType.Text }]
        };
        Entry entry = WorkoutLog.Build(mine, result);
        Assert.Equal("Tabata", entry.Values["Activity"]);
        Assert.Equal(4.0f, entry.Values["Minutes"]);
        Assert.Equal(new TimeOnly(19, 0), entry.Values["Start Time"]);
        Assert.Equal(string.Empty, entry.Values["Notes"]);
    }

    // Pages

    private readonly FakeClock _clock = new();
    private readonly FakeWorkouts _workouts = new();
    private readonly BunitJSModuleInterop _module;
    private readonly List<string> _saved = [];

    public WorkoutTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        _module = JSInterop.SetupModule(WorkoutRunPage.ModulePath);
        _module.Setup<bool>("play", _ => true).SetResult(true);
        _module.Setup<bool>("unlock").SetResult(true);
        _module.Setup<bool>("save", i => (string)i.Arguments[0]! == WorkoutRunPage.RunKey)
            .SetResult(true);
        Services.AddSingleton<TimeProvider>(_clock);
        Services.AddSingleton<IWorkoutService>(_workouts);
        AddAuthorization();
    }

    private void Stored(WorkoutRun run) => _module.Setup<string?>("load", WorkoutRunPage.RunKey).SetResult(run.Serialize());

    private WorkoutRun LastSaved() => WorkoutRun.Deserialize((string)_module.Invocations["save"].Last(i => (string)i.Arguments[0]! == WorkoutRunPage.RunKey).Arguments[1]!)!;

    private IRenderedComponent<TComponent> RenderWithUser<TComponent>(Action<ComponentParameterCollectionBuilder<TComponent>>? parameters = null) where TComponent : IComponent =>
        Render<TComponent>(p =>
        {
            p.AddCascadingValue("CurrentUser", new User { Id = UserId, GoogleId = "g", Name = "Tester", Email = "t" });
            parameters?.Invoke(p);
        });

    private IEnumerable<string> Played() => _module.Invocations["play"].Select(i => (string)i.Arguments[0]!);

    [Fact]
    public void The_runner_counts_down_plays_sounds_and_saves_the_finished_workout()
    {
        Stored(WorkoutRun.Start(UserId, Example(), _clock.UtcNow));
        IRenderedComponent<WorkoutRunPage> cut = RenderWithUser<WorkoutRunPage>();

        cut.WaitForAssertion(() => Assert.Equal("start", cut.Find("[data-phase]").GetAttribute("data-phase")));
        Assert.Equal("0:15", cut.Find("[data-clock]").TextContent);
        cut.WaitForAssertion(() => Assert.Contains("GetReady", Played()));

        _clock.Advance(16);
        cut.WaitForAssertion(() => Assert.Equal("work", cut.Find("[data-phase]").GetAttribute("data-phase")));
        Assert.Contains("Round 1 of 2", cut.Find("[data-round]").TextContent);
        cut.WaitForAssertion(() => Assert.Contains("Go", Played()));

        _clock.Advance(30);
        cut.WaitForAssertion(() => Assert.Equal("rest", cut.Find("[data-phase]").GetAttribute("data-phase")));
        cut.WaitForAssertion(() => Assert.Contains("RestTime", Played()));

        _clock.Advance(60);
        cut.WaitForAssertion(() => Assert.Equal("done", cut.Find("[data-summary]").GetAttribute("data-summary")));
        cut.WaitForAssertion(() => Assert.Contains("GreatJob", Played()));
        WorkoutResult logged = Assert.Single(_workouts.Logged);
        Assert.Equal((2, 60, 10, true), (logged.Rounds, logged.WorkSeconds, logged.RestSeconds, logged.Completed));
        Assert.Contains("Workouts", cut.Find("[data-logged]").TextContent);
        Assert.True(LastSaved().Logged);
    }

    [Fact]
    public async Task Pause_holds_every_timer_until_resume()
    {
        Stored(WorkoutRun.Start(UserId, Example(), _clock.UtcNow));
        IRenderedComponent<WorkoutRunPage> cut = RenderWithUser<WorkoutRunPage>();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("[data-pause]")));
        _clock.Advance(5);
        cut.WaitForAssertion(() => Assert.Equal("0:10", cut.Find("[data-clock]").TextContent));

        await cut.Find("[data-pause]").ClickAsync(new());
        Assert.NotNull(LastSaved().PausedAtUtc);
        _clock.Advance(120);
        Thread.Sleep(450);
        Assert.Equal("0:10", cut.Find("[data-clock]").TextContent);
        Assert.Contains("Paused", cut.Find("[data-phase-label]").TextContent);

        await cut.Find("[data-resume]").ClickAsync(new());
        _clock.Advance(3);
        cut.WaitForAssertion(() => Assert.Equal("0:07", cut.Find("[data-clock]").TextContent));
    }

    [Theory]
    [InlineData("data-abort-nothing", 0, 0)]
    [InlineData("data-abort-done", 1, 0)]
    [InlineData("data-abort-complete", 1, 2)]
    public async Task Abort_saves_nothing_what_was_done_or_everything(string button, int logs, int rounds)
    {
        Stored(WorkoutRun.Start(UserId, Example(), _clock.UtcNow));
        IRenderedComponent<WorkoutRunPage> cut = RenderWithUser<WorkoutRunPage>();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("[data-abort]")));
        _clock.Advance(25);
        cut.WaitForAssertion(() => Assert.Equal("work", cut.Find("[data-phase]").GetAttribute("data-phase")));

        await cut.Find("[data-abort]").ClickAsync(new());
        Assert.NotNull(LastSaved().PausedAtUtc);
        await cut.Find($"[{button}]").ClickAsync(new());

        cut.WaitForAssertion(() => Assert.Equal("aborted", cut.Find("[data-summary]").GetAttribute("data-summary")));
        Assert.Equal(logs, _workouts.Logged.Count);
        if (logs > 0)
        {
            WorkoutResult result = _workouts.Logged[0];
            Assert.Equal(rounds, result.Rounds);
            Assert.Equal(rounds == 2 ? 60 : 10, result.WorkSeconds);
        }
        else
        {
            Assert.Contains("Nothing was saved", cut.Find("[data-logged]").TextContent);
        }
    }

    [Fact]
    public void After_a_refresh_the_runner_is_where_it_was()
    {
        WorkoutRun run = WorkoutRun.Start(UserId, Example(), _clock.UtcNow.AddSeconds(-50));
        run.HeardUntil = 48;
        run.LastSeenUtc = _clock.UtcNow.AddSeconds(-2);
        Stored(run);

        IRenderedComponent<WorkoutRunPage> cut = RenderWithUser<WorkoutRunPage>();

        // 50 s since the start, 2 of them away: 48 s in, the rest after the first work
        cut.WaitForAssertion(() => Assert.Equal("rest", cut.Find("[data-phase]").GetAttribute("data-phase")));
        Assert.Equal("0:07", cut.Find("[data-clock]").TextContent);
        Assert.Empty(_module.Invocations["play"]);
    }

    [Fact]
    public async Task The_editor_reads_times_and_shows_the_plan()
    {
        IRenderedComponent<WorkoutEditPage> cut = RenderWithUser<WorkoutEditPage>();
        await cut.Find("[data-field='name']").InputAsync(new ChangeEventArgs { Value = "Example" });
        await cut.Find("[data-field='start']").InputAsync(new ChangeEventArgs { Value = "15" });
        await cut.Find("[data-field='work']").InputAsync(new ChangeEventArgs { Value = "0:30" });
        await cut.Find("[data-field='rest']").InputAsync(new ChangeEventArgs { Value = "10" });
        await cut.Find("[data-field='rounds']").InputAsync(new ChangeEventArgs { Value = "2" });

        Assert.Equal("1:25", cut.Find("[data-total]").TextContent);
        Assert.Equal(["Start", "Work", "Rest", "Work"], cut.FindAll("[data-preview-step]").Select(e => e.GetAttribute("data-preview-step")));

        await cut.Find("[data-save]").ClickAsync(new());
        WorkoutRoutine saved = Assert.Single(_workouts.Routines);
        Assert.Equal(("Example", 15, 30, 10, 2, true, UserId), (saved.Name, saved.StartSeconds, saved.WorkSeconds, saved.RestSeconds, saved.Rounds, saved.SkipLastRest, saved.UserId));
    }

    [Fact]
    public async Task The_editor_refuses_unreadable_times()
    {
        IRenderedComponent<WorkoutEditPage> cut = RenderWithUser<WorkoutEditPage>();
        await cut.Find("[data-field='name']").InputAsync(new ChangeEventArgs { Value = "Broken" });
        await cut.Find("[data-field='work']").InputAsync(new ChangeEventArgs { Value = "half a minute" });
        await cut.Find("[data-save]").ClickAsync(new());

        Assert.NotEmpty(cut.FindAll("[data-error-for='work']"));
        Assert.Empty(_workouts.Routines);
    }

    [Fact]
    public async Task Starting_from_the_list_stores_the_run()
    {
        _workouts.Routines.Add(Example());
        _module.Setup<string?>("load", WorkoutRunPage.RunKey).SetResult(null);
        IRenderedComponent<WorkoutListPage> cut = RenderWithUser<WorkoutListPage>();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("[data-start='r1']")));
        Assert.Contains("15 to get ready", cut.Find("[data-routine-summary]").TextContent.Replace("0:15", "15"));

        await cut.Find("[data-start='r1']").ClickAsync(new());

        WorkoutRun run = LastSaved();
        Assert.Equal("Example", run.Routine.Name);
        Assert.Equal(_clock.UtcNow, run.StartedAtUtc);
        Assert.EndsWith("TimeTracking/Workouts/run", Services.GetRequiredService<NavigationManager>().Uri);
    }
}
