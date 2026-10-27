using Akagi.Connectors.TimeTracker;
using Akagi.Life;
using Akagi.Life.Mood;
using Akagi.Life.Work;
using Akagi.Receivers.Commands.Life;
using Akagi.Tests.Support;

namespace Akagi.Tests.Life;

public class LifeCommandTests
{
    [Fact]
    public async Task RecordMood_WorryWithFollowUp_CreatesConcern()
    {
        LifeTestContext test = new();

        string output = await test.Run<RecordMoodCommand>(
            ("Kind", "worry"), ("Stress", 8), ("Valence", -2), ("Note", "N2 mock exam on Friday"), ("FollowUpInHours", 48.5));

        Assert.Contains("Recorded worry", output);
        MoodEntry entry = Assert.Single(test.Profile.Mood);
        Assert.Equal(8, entry.Stress);
        Assert.Equal(test.Character.Id, entry.CharacterId);
        Concern concern = Assert.Single(test.Profile.Concerns);
        Assert.Equal(MoodKind.Worry, concern.Kind);
        Assert.InRange((concern.FollowUpAt!.Value - DateTime.UtcNow).TotalHours, 48, 49);
        Assert.Contains(concern.Id, output);
    }

    [Fact]
    public async Task RecordMood_ClampsOutOfRangeValues()
    {
        LifeTestContext test = new();

        await test.Run<RecordMoodCommand>(("Valence", 12), ("Energy", -4), ("Note", "wired"));

        MoodEntry entry = test.Profile.Mood.Single();
        Assert.Equal(5, entry.Valence);
        Assert.Equal(0, entry.Energy);
        Assert.Equal(MoodKind.CheckIn, entry.Kind);
    }

    [Fact]
    public async Task ResolveConcern_ByText_ClosesAndRemembersOutcome()
    {
        LifeTestContext test = new();
        await test.Run<RecordMoodCommand>(("Kind", "event"), ("Note", "job interview at Sony"));

        string output = await test.Run<ResolveConcernCommand>(("Concern", "interview"), ("Outcome", "got the offer"));

        Assert.Contains("Closed concern", output);
        Concern concern = test.Profile.Concerns.Single();
        Assert.True(concern.Resolved);
        Assert.Equal("got the offer", concern.Outcome);
        Assert.Contains(test.Profile.Mood, m => m.Note.Contains("got the offer"));
    }

    [Fact]
    public async Task SetTimeZone_RejectsUnknownZones()
    {
        LifeTestContext test = new();

        Assert.Contains("Unknown time zone", await test.Run<SetTimeZoneCommand>(("TimeZone", "Mars/Olympus")));
        Assert.Contains("Europe/Berlin", await test.Run<SetTimeZoneCommand>(("TimeZone", "Europe/Berlin")));
        Assert.Equal("Europe/Berlin", test.Profile.TimeZoneId);
    }

    [Fact]
    public async Task LogSleep_AcceptsInvariantDecimals()
    {
        LifeTestContext test = new();

        string output = await test.Run<LogSleepCommand>(("Hours", "6.5"));

        Assert.Contains("6h30m", output);
        Assert.Equal(390, test.Profile.HealthDays.Single().SleepMinutes);
    }

    [Fact]
    public async Task StartAndStopWork_TrackSessionsWithoutTimeTracker()
    {
        LifeTestContext test = new();

        await test.Run<StartWorkCommand>(("Project", "Akagi"), ("Task", "routines"));
        Assert.NotNull(test.Profile.Work.Active);

        string switched = await test.Run<StartWorkCommand>(("Project", "N2"));
        Assert.Contains("Stopped Akagi", switched);

        string stopped = await test.Run<StopWorkCommand>(("Notes", "read two articles"));
        Assert.Contains("Stopped N2", stopped);
        Assert.Null(test.Profile.Work.Active);
        Assert.Equal(2, test.Profile.Work.Sessions.Count);
        Assert.Equal("read two articles", test.Profile.Work.Sessions[^1].Notes);

        Assert.Contains("No work session is running", await test.Run<StopWorkCommand>());
    }

    [Fact]
    public async Task StudyTools_TrackVocabularyMistakesAndStreak()
    {
        LifeTestContext test = new();

        Assert.Contains("Added 影響", await test.Run<AddVocabularyCommand>(("Term", "影響"), ("Reading", "えいきょう"), ("Meaning", "influence")));
        Assert.Contains("already in the deck", await test.Run<AddVocabularyCommand>(("Term", "影響")));
        Assert.Contains("box 2", await test.Run<ReviewVocabularyCommand>(("Term", "えいきょう"), ("Correct", true)));
        Assert.Contains("not in the deck", await test.Run<ReviewVocabularyCommand>(("Term", "未知"), ("Correct", false)));

        await test.Run<RecordGrammarMistakeCommand>(("Pattern", "〜ようにする"), ("Explanation", "confused with ために"));
        Assert.Contains("mistake #2", await test.Run<RecordGrammarMistakeCommand>(("Pattern", "〜ようにする")));

        string logged = await test.Run<LogStudyCommand>(("Minutes", 25), ("Activity", "shadowing"));
        Assert.Contains("streak 1 day", logged);

        string status = await test.Run<StudyStatusCommand>();
        Assert.Contains("〜ようにする (2x)", status);
        Assert.Contains("1 words tracked", status);
    }

    [Fact]
    public async Task CheckReading_ReportsSlips()
    {
        LifeTestContext test = new();

        string output = await test.Run<CheckReadingCommand>(("Expected", "けっこん"), ("Heard", "けこん"));

        Assert.Contains("small tsu", output);
    }

    [Fact]
    public async Task MissingRequiredArgument_Throws()
    {
        LifeTestContext test = new();

        await Assert.ThrowsAsync<ArgumentException>(() => test.Run<RecordMoodCommand>(("Valence", 1)));
    }
}

public class LifeContextRendererTests
{
    private static readonly DateTime Now = new(2026, 10, 27, 7, 15, 0, DateTimeKind.Utc);

    [Fact]
    public void Render_CombinesAllSectionsInLocalTime()
    {
        LifeProfile profile = new() { TimeZoneId = "Europe/Berlin" };
        profile.AddMood(new MoodEntry { Timestamp = Now.AddHours(-1), Valence = -1, Energy = 2, Note = "tired" });
        profile.AddConcern(new Concern { Text = "presentation", Kind = MoodKind.Event, FollowUpAt = Now.AddDays(2) });
        Akagi.Life.Health.HealthImport.LogSleep(profile, 5, Now.AddMinutes(-30), "test");
        profile.Work.Start("Akagi", "", Now.AddMinutes(-20));
        profile.Study.GetDay("2026-10-26").Minutes = 15;

        string text = LifeContextRenderer.Render(profile, Now, LifeSections.All);

        Assert.Contains("Tuesday 2026-10-27 08:15", text);
        Assert.Contains("morning", text);
        Assert.Contains("energy 2/10", text);
        Assert.Contains("presentation", text);
        Assert.Contains("Sleep last night: 5h00m", text);
        Assert.Contains("Currently working on Akagi", text);
        Assert.Contains("study streak 1 day", text);
        Assert.Contains("Tone right now: gentle", text);
    }

    [Fact]
    public void Render_RespectsSectionFlagsAndEmptyProfile()
    {
        LifeProfile profile = new();

        Assert.Equal(string.Empty, LifeContextRenderer.Render(profile, Now, LifeSections.Mood | LifeSections.Health | LifeSections.Tone));
        Assert.StartsWith("Local time:", LifeContextRenderer.Render(profile, Now, LifeSections.Time));
    }
}

public class TimeTrackerWorkLogTests
{
    private class FakeConnector : ITimeTrackerConnector
    {
        public List<Definition> Definitions { get; } = [];
        public List<Entry> Entries { get; } = [];

        public bool IsConfigured => true;

        public Task<List<Definition>> GetDefinitionsAsync(TimeTrackerUserConfig userConfig) => Task.FromResult(Definitions.ToList());

        public Task<Definition?> GetDefinitionAsync(TimeTrackerUserConfig userConfig, string definitionId) =>
            Task.FromResult(Definitions.FirstOrDefault(d => d.Id == definitionId));

        public Task<List<Entry>> GetEntriesAsync(TimeTrackerUserConfig userConfig, string? definitionId = null) => Task.FromResult(Entries.ToList());

        public Task<Entry?> GetEntryAsync(TimeTrackerUserConfig userConfig, string entryId) =>
            Task.FromResult(Entries.FirstOrDefault(e => e.Id == entryId));

        public Task<Definition> CreateDefinitionAsync(TimeTrackerUserConfig userConfig, string name, List<FieldDefinition> fields)
        {
            Definition definition = new() { Id = $"def{Definitions.Count}", Name = name, UserId = userConfig.GoogleId, Fields = fields };
            Definitions.Add(definition);
            return Task.FromResult(definition);
        }

        public Task<Entry> CreateEntryAsync(TimeTrackerUserConfig userConfig, string definitionId, Dictionary<string, object> values, DateTime? createdAt = null)
        {
            Entry entry = new() { Id = $"entry{Entries.Count}", DefinitionId = definitionId, Values = values, CreatedAt = createdAt ?? DateTime.UtcNow };
            Entries.Add(entry);
            return Task.FromResult(entry);
        }
    }

    [Fact]
    public async Task Sync_CreatesDefinitionOnceAndLogsSessions()
    {
        FakeConnector connector = new();
        TimeTrackerUserConfig config = new() { GoogleId = "g" };
        DateTime now = new(2026, 10, 27, 12, 0, 0, DateTimeKind.Utc);
        WorkSession first = new() { Project = "Akagi", Task = "routines", StartUtc = now.AddHours(-2), EndUtc = now.AddMinutes(-30) };
        WorkSession second = new() { Project = "N2", StartUtc = now.AddMinutes(-30), EndUtc = now };

        await TimeTrackerWorkLog.SyncAsync(connector, config, first, now);
        await TimeTrackerWorkLog.SyncAsync(connector, config, second, now);

        Definition definition = Assert.Single(connector.Definitions);
        Assert.Equal(TimeTrackerWorkLog.DefinitionName, definition.Name);
        Assert.Equal(2, connector.Entries.Count);
        Assert.Equal(1.5, connector.Entries[0].Values["Hours"]);
        Assert.Equal("entry0", first.TimeTrackerEntryId);

        string overview = await TimeTrackerWorkLog.DescribeAsync(connector, config, new LifeProfile(), 1);
        Assert.Contains("Akagi Work Log", overview);
        Assert.Contains("2 entries", overview);
    }
}
