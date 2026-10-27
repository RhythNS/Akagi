using Akagi.Characters;
using Akagi.Characters.Conversations;
using Akagi.Characters.Routines;
using Akagi.Characters.TriggerPoints;
using Akagi.Characters.TriggerPoints.Actions;
using Akagi.Life;
using Akagi.Life.Health;
using Akagi.Life.Mood;
using Akagi.Life.Study;
using Akagi.Scheduling.Tasks;
using Akagi.Tests.Support;
using Akagi.Users;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Bson;

namespace Akagi.Tests.Life;

public class RoutineTests
{
    // Tuesday 2026-10-27
    private static DateTime Utc(int hour, int minute = 0, int day = 27) => new(2026, 10, day, hour, minute, 0, DateTimeKind.Utc);

    private static RoutineState State(DateTime now, LifeProfile? profile = null, DateTime? lastUser = null) => new()
    {
        NowUtc = now,
        Profile = profile ?? new LifeProfile(),
        LastUserMessageUtc = lastUser
    };

    [Fact]
    public void Daily_FiresInsideWindowOncePerDay()
    {
        DailyRoutine routine = new() { TimeOfDay = "08:00", WindowMinutes = 60 };

        Assert.False(routine.Evaluate(State(Utc(7, 59))).Fire);
        RoutineDecision decision = routine.Evaluate(State(Utc(8, 10)));
        Assert.True(decision.Fire, decision.Reason);

        routine.OnFired(State(Utc(8, 10)));
        Assert.False(routine.Evaluate(State(Utc(8, 20))).Fire);
        Assert.False(routine.Evaluate(State(Utc(9, 1))).Fire);
        Assert.True(routine.Evaluate(State(Utc(8, 5, day: 28))).Fire);
    }

    [Fact]
    public void Daily_UsesLocalTimeAndWeekdays()
    {
        LifeProfile tokyo = new() { TimeZoneId = "Asia/Tokyo" };
        DailyRoutine routine = new() { TimeOfDay = "08:00", Days = Weekdays.Weekend };

        // 23:30 UTC Tuesday is 08:30 Wednesday in Tokyo
        Assert.False(routine.Evaluate(State(Utc(23, 30), tokyo)).Fire);

        routine.Days = Weekdays.Wednesday;
        Assert.True(routine.Evaluate(State(Utc(23, 30), tokyo)).Fire);
    }

    [Fact]
    public void Daily_WindowCrossingMidnight_DoesNotFireTwice()
    {
        DailyRoutine routine = new() { TimeOfDay = "23:00", WindowMinutes = 120, QuietHoursStart = "00:00", QuietHoursEnd = "00:00" };

        Assert.True(routine.Evaluate(State(Utc(23, 10))).Fire);
        routine.OnFired(State(Utc(23, 10)));
        Assert.False(routine.Evaluate(State(Utc(0, 30, day: 28))).Fire);
    }

    [Fact]
    public void QuietHours_AndActiveUser_SuppressRoutines()
    {
        InactivityRoutine routine = new() { AfterHours = 1 };

        Assert.False(routine.Evaluate(State(Utc(23, 0), lastUser: Utc(10))).Fire);
        Assert.True(routine.Evaluate(State(Utc(12, 0), lastUser: Utc(10))).Fire);

        DailyRoutine daily = new() { TimeOfDay = "12:00" };
        RoutineDecision busy = daily.Evaluate(State(Utc(12, 5), lastUser: Utc(12, 0)));
        Assert.False(busy.Fire);
        Assert.Contains("active", busy.Reason);
    }

    [Fact]
    public void Inactivity_FiresOncePerSilence()
    {
        InactivityRoutine routine = new() { AfterHours = 20 };
        DateTime lastUser = Utc(9, 0, day: 26);

        Assert.False(routine.Evaluate(State(Utc(9, 0, day: 26).AddHours(19), lastUser: lastUser)).Fire);
        Assert.True(routine.Evaluate(State(Utc(9), lastUser: lastUser)).Fire);

        routine.OnFired(State(Utc(9), lastUser: lastUser));
        Assert.False(routine.Evaluate(State(Utc(15), lastUser: lastUser)).Fire);

        // The user answered, then went silent again
        Assert.True(routine.Evaluate(State(Utc(12, day: 28), lastUser: Utc(10))).Fire);
        Assert.False(routine.Evaluate(State(Utc(12), lastUser: null)).Fire);
    }

    [Fact]
    public void Break_FiresAfterLongStretchEvenWhileChatting()
    {
        LifeProfile profile = new();
        profile.Work.Start("Akagi", "tests", Utc(9));
        BreakRoutine routine = new() { WorkMinutes = 90 };

        Assert.False(routine.Evaluate(State(Utc(10), profile, lastUser: Utc(10))).Fire);
        RoutineDecision decision = routine.Evaluate(State(Utc(10, 31), profile, lastUser: Utc(10, 30)));
        Assert.True(decision.Fire);
        Assert.Contains("Akagi", decision.Details);

        routine.OnFired(State(Utc(10, 31), profile));
        Assert.False(routine.Evaluate(State(Utc(11, 30), profile)).Fire);

        profile.Work.Stop(Utc(11, 40), "");
        Assert.False(routine.Evaluate(State(Utc(14), profile)).Fire);
    }

    [Fact]
    public void FollowUp_MarksConcernsAsFollowedUp()
    {
        LifeProfile profile = new();
        profile.AddConcern(new Concern { Text = "N2 mock exam", Kind = MoodKind.Event, FollowUpAt = Utc(8) });
        profile.AddConcern(new Concern { Text = "dentist", FollowUpAt = Utc(20) });
        FollowUpRoutine routine = new();

        RoutineDecision decision = routine.Evaluate(State(Utc(9), profile));
        Assert.True(decision.Fire);
        Assert.Contains("N2 mock exam", decision.Details);
        Assert.DoesNotContain("dentist", decision.Details);

        routine.OnFired(State(Utc(9), profile));
        Assert.False(routine.Evaluate(State(Utc(10), profile)).Fire);
        Assert.True(profile.Dirty);
    }

    [Fact]
    public void ShortSleep_FiresOnlyForShortLastNight()
    {
        LifeProfile profile = new();
        ShortSleepRoutine routine = new() { MinSleepHours = 6, NotBefore = "08:00" };

        Assert.False(routine.Evaluate(State(Utc(9), profile)).Fire);

        HealthImport.LogSleep(profile, 7, Utc(7), "test");
        Assert.False(routine.Evaluate(State(Utc(9), profile)).Fire);

        HealthImport.LogSleep(profile, 4, Utc(7), "test");
        Assert.False(routine.Evaluate(State(Utc(7, 30), profile)).Fire);
        Assert.True(routine.Evaluate(State(Utc(9), profile)).Fire);
    }

    [Fact]
    public void StudyNudge_ProtectsExistingStreak()
    {
        LifeProfile profile = new();
        StudyNudgeRoutine routine = new() { TimeOfDay = "19:30" };
        profile.Study.GetDay("2026-10-26").Minutes = 20;

        Assert.False(routine.Evaluate(State(Utc(18), profile)).Fire);
        Assert.True(routine.Evaluate(State(Utc(20), profile)).Fire);

        profile.Study.GetDay("2026-10-27").Reviews = 3;
        Assert.False(routine.Evaluate(State(Utc(20), profile)).Fire);
    }

    [Fact]
    public void Engine_BuildsInstructionInCharacterVoice()
    {
        DailyRoutine routine = new() { Name = "Morning", Instruction = "Say good morning." };

        string text = RoutineEngine.BuildInstruction(routine, RoutineDecision.Yes("daily", "Slept 5h"));

        Assert.StartsWith(TextMessage.SystemMessagePrefix + RoutineEngine.ProactivePrefix, text);
        Assert.Contains("Say good morning.", text);
        Assert.Contains("Slept 5h", text);
        Assert.Contains("own voice", text);
    }

    [Fact]
    public void Engine_PicksLeastRecentlyFiredRoutine()
    {
        InactivityRoutine recent = new() { AfterHours = 1, LastFiredUtc = Utc(1), QuietHoursStart = "00:00", QuietHoursEnd = "00:00" };
        DailyRoutine stale = new() { TimeOfDay = "11:00", LastFiredUtc = Utc(11, day: 20) };

        (Routine Routine, RoutineDecision Decision)? selected = RoutineEngine.SelectDue([recent, stale], State(Utc(11, 30), lastUser: Utc(2)));

        Assert.Same(stale, selected?.Routine);
    }
}

public class RoutineRunnerTests
{
    private static readonly DateTime Now = new(2026, 10, 27, 8, 30, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Tick_DeliversOutreachAndPersistsBeforeDelivery()
    {
        InMemoryCharacterDatabase characters = new();
        InMemoryUserDatabase users = new();
        InMemoryRoutineDatabase routines = new();
        InMemoryLifeProfileDatabase profiles = new();

        User user = new() { Id = ObjectId.GenerateNewId().ToString() };
        await users.SaveDocumentAsync(user);

        DailyRoutine morning = new() { Name = "Morning", TimeOfDay = "08:00" };
        await routines.SaveDocumentAsync(morning);
        InactivityRoutine silence = new() { AfterHours = 48 };
        await routines.SaveDocumentAsync(silence);

        Character character = new() { Name = "Akagi", UserId = user.Id!, RoutineIds = [morning.Id!, silence.Id!] };
        await characters.SaveDocumentAsync(character);
        Character muted = new() { Name = "Muted", UserId = user.Id!, RoutineIds = [morning.Id!], AllowAutomaticProcessing = false };
        await characters.SaveDocumentAsync(muted);

        List<(Character Character, Message Message)> delivered = [];
        RoutineRunner runner = new(characters, users, routines, profiles, (c, _, m) =>
        {
            // The routine must already be persisted as fired when delivery happens
            Assert.Equal(Now, morning.LastFiredUtc);
            delivered.Add((c, m));
            return Task.CompletedTask;
        }, NullLogger.Instance);

        Assert.Equal(1, await runner.TickAsync(Now));
        (Character to, Message message) = Assert.Single(delivered);
        Assert.Same(character, to);
        Assert.Equal(Message.Type.System, message.From);
        Assert.Contains("Morning", ((TextMessage)message).Text);

        Assert.Equal(0, await runner.TickAsync(Now.AddMinutes(1)));
    }

    [Fact]
    public async Task Tick_SurvivesFailingDelivery()
    {
        InMemoryCharacterDatabase characters = new();
        InMemoryUserDatabase users = new();
        InMemoryRoutineDatabase routines = new();
        User user = new() { Id = ObjectId.GenerateNewId().ToString() };
        await users.SaveDocumentAsync(user);
        DailyRoutine morning = new() { TimeOfDay = "08:00" };
        await routines.SaveDocumentAsync(morning);
        await characters.SaveDocumentAsync(new Character { UserId = user.Id!, RoutineIds = [morning.Id!] });

        RoutineRunner runner = new(characters, users, routines, new InMemoryLifeProfileDatabase(),
            (_, _, _) => throw new InvalidOperationException("LLM down"), NullLogger.Instance);

        Assert.Equal(0, await runner.TickAsync(Now));
        // Marked as fired, so it does not retry every minute
        Assert.Equal(Now, morning.LastFiredUtc);
    }
}

public class TriggerSendMessageTests
{
    [Fact]
    public async Task CreateTask_SchedulesOutreachForCharacter()
    {
        LifeTestContext test = new();
        TriggerSendMessage action = new() { Instruction = "Ask how the presentation went.", DelayMinutes = 0 };
        await action.Init(new TriggerPoint.TriggerContext { Character = test.Character, User = test.User, DatabaseFactory = test.Database });
        DateTime now = DateTime.UtcNow;

        SendSystemMessageTask task = action.CreateTask(now);

        Assert.Equal(test.Character.Id, task.CharacterId);
        Assert.Equal(test.User.Id, task.UserId);
        Assert.Contains("Ask how the presentation went.", task.Message);
        Assert.Equal(now.AddMinutes(1), task.Time);
    }
}
