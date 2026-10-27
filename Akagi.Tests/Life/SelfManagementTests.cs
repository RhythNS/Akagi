using Akagi.Characters;
using Akagi.Characters.Conversations;
using Akagi.Characters.Routines;
using Akagi.Characters.Upgrades;
using Akagi.Communication.Commands.Life;
using Akagi.Life;
using Akagi.Receivers.Commands.Life;
using Akagi.Tests.Support;
using Akagi.Users;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Bson;

namespace Akagi.Tests.Life;

public class SelfScheduledRoutineTests
{
    private static readonly DateTime Now = new(2026, 10, 27, 23, 30, 0, DateTimeKind.Utc);

    private static RoutineState State(DateTime now, DateTime? lastUser = null) => new()
    {
        NowUtc = now,
        Profile = new LifeProfile(),
        LastUserMessageUtc = lastUser
    };

    [Fact]
    public void OneTime_FiresOnceAtItsTimeEvenDuringQuietHours()
    {
        OneTimeRoutine routine = new() { DueUtc = Now };

        Assert.False(routine.Evaluate(State(Now.AddMinutes(-1))).Fire);
        Assert.True(routine.Evaluate(State(Now)).Fire);

        routine.OnFired(State(Now));

        Assert.False(routine.Enabled);
        Assert.True(routine.IsDone);
        Assert.False(routine.Evaluate(State(Now.AddMinutes(5))).Fire);
    }

    [Fact]
    public void OneTime_GivesUpWhenLongOverdue()
    {
        OneTimeRoutine routine = new() { DueUtc = Now, ExpireAfterHours = 2 };

        RoutineDecision decision = routine.Evaluate(State(Now.AddHours(3)));

        Assert.False(decision.Fire);
        Assert.Equal("missed", decision.Reason);
    }

    [Fact]
    public void OneTime_WaitsForALiveExchangeToPause()
    {
        OneTimeRoutine routine = new() { DueUtc = Now };

        Assert.False(routine.Evaluate(State(Now, lastUser: Now.AddMinutes(-1))).Fire);
        Assert.True(routine.Evaluate(State(Now.AddMinutes(4), lastUser: Now.AddMinutes(-1))).Fire);
    }

    [Theory]
    [InlineData("daily", "Every")]
    [InlineData("weekdays", "Workdays")]
    [InlineData("Weekends", "Weekend")]
    [InlineData("mon,wed,fri", "Monday, Wednesday, Friday")]
    [InlineData("tu th", "Tuesday, Thursday")]
    public void ParseDays_ReadsNamesAndLists(string text, string expected)
    {
        Assert.Equal(expected, DailyRoutine.ParseDays(text)!.Value.ToString());
    }

    [Theory]
    [InlineData("someday")]
    [InlineData("m")]
    [InlineData("")]
    public void ParseDays_RejectsGarbage(string text)
    {
        Assert.Null(DailyRoutine.ParseDays(text));
    }

    [Fact]
    public async Task ScheduleCheckIn_InHours_CreatesOwnedOneTimeRoutine()
    {
        LifeTestContext test = new();

        string output = await test.Run<ScheduleCheckInCommand>(("About", "ask how the dentist appointment went"), ("InHours", 3), ("Name", "Dentist"));

        OneTimeRoutine routine = Assert.IsType<OneTimeRoutine>(Assert.Single(test.Routines.Items));
        Assert.Equal(test.Character.Id, routine.OwnerCharacterId);
        Assert.Equal(test.User.Id, routine.UserId);
        Assert.True(routine.CreatedBySelf);
        Assert.Equal("ask how the dentist appointment went", routine.Instruction);
        Assert.InRange((routine.DueUtc!.Value - DateTime.UtcNow).TotalMinutes, 179, 181);
        Assert.Contains(routine.ShortId, output);
        Assert.Empty(test.Character.RoutineIds);
    }

    [Fact]
    public async Task ScheduleCheckIn_TimeAlreadyPassedToday_MeansTomorrow()
    {
        LifeTestContext test = new();
        string earlier = DateTime.UtcNow.AddHours(-1).ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture);

        await test.Run<ScheduleCheckInCommand>(("About", "good morning call"), ("At", earlier));

        OneTimeRoutine routine = Assert.IsType<OneTimeRoutine>(Assert.Single(test.Routines.Items));
        Assert.InRange((routine.DueUtc!.Value - DateTime.UtcNow).TotalHours, 22.9, 24);
    }

    [Fact]
    public async Task ScheduleCheckIn_Repeat_CreatesDailyRoutineAtLocalTime()
    {
        LifeTestContext test = new();
        await test.Run<SetTimeZoneCommand>(("TimeZone", "Asia/Tokyo"));
        string tomorrow = DateTime.UtcNow.AddDays(1).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

        string output = await test.Run<ScheduleCheckInCommand>(("About", "evening Japanese shadowing together"), ("At", $"{tomorrow} 21:15"), ("Repeat", "weekdays"));

        DailyRoutine routine = Assert.IsType<DailyRoutine>(Assert.Single(test.Routines.Items));
        Assert.Equal("21:15", routine.TimeOfDay);
        Assert.Equal(Weekdays.Workdays, routine.Days);
        Assert.Contains("daily 21:15 (workdays)", output);
    }

    [Fact]
    public async Task ScheduleCheckIn_RejectsPastAndMissingTimes()
    {
        LifeTestContext test = new();

        Assert.Contains("in the past", await test.Run<ScheduleCheckInCommand>(("About", "x"), ("At", "2020-01-01 10:00")));
        Assert.Contains("Give a time", await test.Run<ScheduleCheckInCommand>(("About", "x")));
        Assert.Contains("Unknown Repeat", await test.Run<ScheduleCheckInCommand>(("About", "x"), ("InHours", 1), ("Repeat", "fortnightly")));
        Assert.Empty(test.Routines.Items);
    }

    [Fact]
    public async Task ListAdjustCancel_WorkOnGraphAndSelfMadeRoutines()
    {
        LifeTestContext test = new();
        InactivityRoutine graphRoutine = new() { Name = "Missed you", AfterHours = 30 };
        await test.Routines.SaveDocumentAsync(graphRoutine);
        test.Character.RoutineIds = [graphRoutine.Id!];
        await test.Run<ScheduleCheckInCommand>(("About", "wish luck for the interview"), ("InHours", 20), ("Name", "Interview luck"));

        string list = await test.Run<ListRoutinesCommand>();
        Assert.Contains("Missed you: after 30h of silence, built in", list);
        Assert.Contains("Interview luck", list);
        Assert.Contains("made by you", list);

        string adjusted = await test.Run<AdjustRoutineCommand>(("Routine", "Missed you"), ("Setting", "AfterHours"), ("Value", "48"), ("QuietHours", "23:00-09:00"));
        Assert.Contains("AfterHours = 48", adjusted);
        Assert.Equal(48, graphRoutine.AfterHours);
        Assert.Equal("23:00", graphRoutine.QuietHoursStart);

        string refused = await test.Run<AdjustRoutineCommand>(("Routine", graphRoutine.ShortId), ("Setting", "OwnerCharacterId"), ("Value", "x"));
        Assert.Contains("no adjustable setting", refused);
        Assert.Contains("is not a valid AfterHours", await test.Run<AdjustRoutineCommand>(("Routine", "Missed you"), ("Setting", "AfterHours"), ("Value", "soon")));

        Assert.Contains("Switched off", await test.Run<CancelRoutineCommand>(("Routine", "Missed you")));
        Assert.False(graphRoutine.Enabled);
        Assert.Contains("Deleted", await test.Run<CancelRoutineCommand>(("Routine", "interview")));
        Assert.Single(test.Routines.Items);
    }

    [Fact]
    public async Task Adjust_MovesOneTimeCheckIn()
    {
        LifeTestContext test = new();
        await test.Run<ScheduleCheckInCommand>(("About", "exam result"), ("InHours", 2), ("Name", "Exam"));
        string later = DateTime.UtcNow.AddDays(2).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) + " 18:00";

        string output = await test.Run<AdjustRoutineCommand>(("Routine", "Exam"), ("At", later));

        OneTimeRoutine routine = (OneTimeRoutine)test.Routines.Items.Single();
        Assert.Contains("moved to", output);
        Assert.Equal(18, routine.DueUtc!.Value.Hour);
    }

    [Fact]
    public void Find_ReportsAmbiguousNames()
    {
        List<Routine> routines =
        [
            new DailyRoutine { Id = ObjectId.GenerateNewId().ToString(), Name = "Morning greeting" },
            new DailyRoutine { Id = ObjectId.GenerateNewId().ToString(), Name = "Morning stretch" }
        ];

        (Routine? routine, string? error) = RoutineCommand.Find(routines, "morning");

        Assert.Null(routine);
        Assert.Contains("matches several", error);
        Assert.Same(routines[1], RoutineCommand.Find(routines, "Morning stretch").Routine);
    }

    [Fact]
    public async Task Runner_FiresSelfMadeRoutinesOfCharactersWithoutGraphRoutines()
    {
        InMemoryCharacterDatabase characters = new();
        InMemoryUserDatabase users = new();
        InMemoryRoutineDatabase routines = new();
        User user = new() { Id = ObjectId.GenerateNewId().ToString() };
        await users.SaveDocumentAsync(user);
        Character character = new() { Name = "Akagi", UserId = user.Id! };
        await characters.SaveDocumentAsync(character);
        OneTimeRoutine checkIn = new() { DueUtc = Now.AddMinutes(-1), OwnerCharacterId = character.Id!, CreatedBySelf = true, Instruction = "ask about the dentist" };
        await routines.SaveDocumentAsync(checkIn);

        List<string> delivered = [];
        RoutineRunner runner = new(characters, users, routines, new InMemoryLifeProfileDatabase(),
            (_, _, m) => { delivered.Add(((TextMessage)m).Text); return Task.CompletedTask; }, NullLogger.Instance);

        Assert.Equal(1, await runner.TickAsync(Now));
        Assert.Contains("ask about the dentist", Assert.Single(delivered));
        Assert.False(checkIn.Enabled);
        Assert.Equal(0, await runner.TickAsync(Now.AddMinutes(1)));
    }

    [Fact]
    public async Task Runner_PrunesFinishedCheckInsAfterAWeek()
    {
        InMemoryCharacterDatabase characters = new();
        InMemoryUserDatabase users = new();
        InMemoryRoutineDatabase routines = new();
        User user = new() { Id = ObjectId.GenerateNewId().ToString() };
        await users.SaveDocumentAsync(user);
        DailyRoutine graph = new() { TimeOfDay = "03:00" };
        await routines.SaveDocumentAsync(graph);
        Character character = new() { Name = "Akagi", UserId = user.Id!, RoutineIds = [graph.Id!] };
        await characters.SaveDocumentAsync(character);
        OneTimeRoutine old = new() { DueUtc = Now.AddDays(-9), LastFiredUtc = Now.AddDays(-9), Enabled = false, OwnerCharacterId = character.Id!, CreatedBySelf = true };
        await routines.SaveDocumentAsync(old);

        RoutineRunner runner = new(characters, users, routines, new InMemoryLifeProfileDatabase(), (_, _, _) => Task.CompletedTask, NullLogger.Instance);
        await runner.TickAsync(Now);

        Assert.DoesNotContain(old, routines.Items);
        Assert.Contains(graph, routines.Items);
    }
}

public class UpgradeTests
{
    [Fact]
    public async Task SuggestUpgrade_FilesAndMergesRepeatedIdeas()
    {
        LifeTestContext test = new();

        string first = await test.Run<SuggestUpgradeCommand>(("Title", "Movie night sync module"),
            ("Description", "Let me follow the timestamp of what we watch so I can react to scenes."), ("Priority", "low"), ("Motivation", "he watches anime alone"));
        string again = await test.Run<SuggestUpgradeCommand>(("Title", "Movie sync for movie night"),
            ("Description", "same"), ("Priority", "high"), ("Motivation", "he asked me to watch with him"));

        UpgradeProposal proposal = Assert.Single(test.Upgrades.Items);
        Assert.Contains("Filed upgrade", first);
        Assert.Contains("asked for it 2 times", again);
        Assert.Equal(2, proposal.Mentions);
        Assert.Equal(UpgradePriority.High, proposal.Priority);
        Assert.Contains("he asked me to watch with him", proposal.Motivation);
        Assert.Equal(test.Character.Id, proposal.CharacterId);
    }

    [Fact]
    public async Task SuggestUpgrade_DoesNotMergeIntoFinishedIdeas()
    {
        LifeTestContext test = new();
        await test.Run<SuggestUpgradeCommand>(("Title", "Wardrobe module"), ("Description", "outfits"));
        test.Upgrades.Items.Single().Status = UpgradeStatus.Done;

        await test.Run<SuggestUpgradeCommand>(("Title", "Wardrobe module"), ("Description", "more outfits"));

        Assert.Equal(2, test.Upgrades.Items.Count());
    }

    [Fact]
    public async Task ListUpgrades_FiltersByStatus()
    {
        LifeTestContext test = new();
        await test.Run<SuggestUpgradeCommand>(("Title", "Voice diary"), ("Description", "a"));
        await test.Run<SuggestUpgradeCommand>(("Title", "Calendar access"), ("Description", "b"));
        test.Upgrades.Items.First(p => p.Title == "Voice diary").Status = UpgradeStatus.Rejected;

        string open = await test.Run<ListUpgradesCommand>();
        string rejected = await test.Run<ListUpgradesCommand>(("Status", "rejected"));

        Assert.Contains("Calendar access", open);
        Assert.DoesNotContain("Voice diary", open);
        Assert.Contains("Voice diary", rejected);
    }

    [Theory]
    [InlineData("Movie night sync module", "Movie sync for movie night", true)]
    [InlineData("Wardrobe module", "Calendar access", false)]
    [InlineData("", "anything", false)]
    public void Similarity_MatchesRephrasedTitles(string a, string b, bool similar)
    {
        Assert.Equal(similar, UpgradeProposal.Similarity(a, b) >= SuggestUpgradeCommand.DuplicateSimilarity);
    }

    [Fact]
    public void Notice_AsksCompanionToReactInHerVoice()
    {
        UpgradeProposal proposal = new() { Title = "Wardrobe module", Status = UpgradeStatus.Done, DeveloperNote = "use ChangeOutfit" };

        string notice = UpgradeCommand.BuildNotice(proposal);

        Assert.StartsWith(TextMessage.SystemMessagePrefix, notice);
        Assert.Contains("finished building", notice);
        Assert.Contains("use ChangeOutfit", notice);
        Assert.Equal(UpgradeStatus.Done, UpgradeCommand.ParseStatus("shipped"));
        Assert.Null(UpgradeCommand.ParseStatus("maybe"));
    }
}
