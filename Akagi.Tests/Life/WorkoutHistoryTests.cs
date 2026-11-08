using Akagi.Connectors.GoogleHealth;
using Akagi.Connectors.TimeTracker;
using Akagi.Life;
using Akagi.Life.Health;
using Akagi.Receivers.Commands;
using Akagi.Receivers.Commands.Life;
using Akagi.Tests.Support;
using System.Text.Json;

namespace Akagi.Tests.Life;

/// <summary>
/// His workouts from both sources: the watch's exercise sessions from Google Health and the interval timer's
/// "Workouts" tracker, merged into one report that health lookups show.
/// </summary>
public class WorkoutHistoryTests
{
    private class FakeConnector : ITimeTrackerConnector
    {
        public List<Definition> Definitions { get; } = [];
        public List<Entry> Entries { get; } = [];
        public bool IsConfigured => true;
        public Task<List<Definition>> GetDefinitionsAsync(TimeTrackerUserConfig userConfig) => Task.FromResult(Definitions.ToList());
        public Task<Definition?> GetDefinitionAsync(TimeTrackerUserConfig userConfig, string definitionId) => Task.FromResult(Definitions.FirstOrDefault(d => d.Id == definitionId));
        public Task<List<Entry>> GetEntriesAsync(TimeTrackerUserConfig userConfig, string? definitionId = null) =>
            Task.FromResult(Entries.Where(e => definitionId == null || e.DefinitionId == definitionId).ToList());
        public Task<Entry?> GetEntryAsync(TimeTrackerUserConfig userConfig, string entryId) => Task.FromResult(Entries.FirstOrDefault(e => e.Id == entryId));
        public Task<Definition> CreateDefinitionAsync(TimeTrackerUserConfig userConfig, string name, List<FieldDefinition> fields) => throw new NotSupportedException();
        public Task<Entry> CreateEntryAsync(TimeTrackerUserConfig userConfig, string definitionId, Dictionary<string, object> values, DateTime? createdAt = null) => throw new NotSupportedException();
    }

    private static readonly TimeTrackerUserConfig Config = new() { GoogleId = "g" };
    // Sunday 2026-11-08 18:00 in Berlin
    private static readonly DateTime Now = new(2026, 11, 8, 17, 0, 0, DateTimeKind.Utc);

    private static LifeProfile Berlin() => new() { TimeZoneId = "Europe/Berlin" };

    /// <summary>
    /// An entry the way the TimeTracker API hands it over: values as JSON, Date and the times as local strings.
    /// </summary>
    private static Entry Logged(string json, DateTime createdUtc, string definitionId = "workouts") => new()
    {
        Id = Guid.NewGuid().ToString(),
        DefinitionId = definitionId,
        CreatedAt = createdUtc,
        Values = JsonSerializer.Deserialize<Dictionary<string, object>>(json)!
    };

    private const string Jumprope = """
        {"Workout":"Jumprope","Date":"2026-11-08","Start":"16:33:03.8795041","End":"17:04:13.1383271","Rounds":6,
         "Planned rounds":6,"Work seconds":1650,"Rest seconds":150,"Duration seconds":1820,"Completed":true}
        """;

    private const string Core = """
        {"Workout":"Core","Date":"2026-11-06","Start":"21:40:00","End":"21:52:00","Rounds":2,"Planned rounds":5,
         "Work seconds":90,"Rest seconds":20,"Duration seconds":125,"Completed":false}
        """;

    /// <summary>
    /// Two sessions as the Charge 6 reports them: today's rope skipping, started by hand, and an older walk it
    /// detected on its own. Int64 values come as strings, durations as "1820s", distance in millimetres.
    /// </summary>
    internal const string ExerciseJson = """
        {"dataPoints":[
          {"name":"users/me/dataTypes/exercise/dataPoints/rope","dataSource":{"recordingMethod":"ACTIVELY_MEASURED","platform":"FITBIT"},
           "exercise":{"interval":{"startTime":"2026-11-08T15:35:09Z","startUtcOffset":"3600s","endTime":"2026-11-08T16:05:29Z","endUtcOffset":"3600s"},
             "exerciseType":"JUMPING_ROPE","displayName":"Skipping rope","activeDuration":"1820s",
             "metricsSummary":{"caloriesKcal":277,"averageHeartRateBeatsPerMinute":"152","activeZoneMinutes":"49",
               "heartRateZoneDurations":{"lightTime":"0s","moderateTime":"600s","vigorousTime":"660s","peakTime":"540s"}}}},
          {"name":"users/me/dataTypes/exercise/dataPoints/walk","dataSource":{"recordingMethod":"PASSIVELY_MEASURED","platform":"FITBIT"},
           "exercise":{"interval":{"startTime":"2026-11-05T10:37:20Z","startUtcOffset":"3600s","endTime":"2026-11-05T11:22:26Z","endUtcOffset":"3600s"},
             "exerciseType":"WALKING","displayName":"Walk","activeDuration":"2706s",
             "metricsSummary":{"caloriesKcal":190,"distanceMillimeters":1483609,"steps":"1984","averageHeartRateBeatsPerMinute":"102","activeZoneMinutes":"14",
               "heartRateZoneDurations":{"lightTime":"1860s","moderateTime":"840s","vigorousTime":"0s","peakTime":"0s"}}}}
        ]}
        """;

    private static LifeProfile WithWatch()
    {
        LifeProfile profile = Berlin();
        GoogleHealthMapper.ApplyExercise(profile, ExerciseJson, new DateTime(2026, 10, 26), new DateTime(2026, 11, 8));
        return profile;
    }

    private static async Task<List<TimeTrackerWorkouts.Session>> Timer(LifeProfile profile, params Entry[] entries)
    {
        FakeConnector connector = new();
        connector.Definitions.Add(new Definition { Id = "workouts", Name = "Workouts", UserId = "u" });
        connector.Entries.AddRange(entries);
        (List<TimeTrackerWorkouts.Session>? sessions, _, string? problem) = await TimeTrackerWorkouts.LoadAsync(connector, Config, profile, null);
        Assert.Null(problem);
        return sessions!;
    }

    [Fact]
    public void The_watch_sessions_are_stored_on_their_local_days()
    {
        LifeProfile profile = WithWatch();

        HealthExercise rope = Assert.Single(profile.HealthDays.Single(d => d.Date == "2026-11-08").Exercises);
        Assert.Equal("JUMPING_ROPE", rope.Type);
        Assert.Equal(1820, rope.ActiveSeconds);
        Assert.Equal(152, rope.AverageHeartRate);
        Assert.Equal(49, rope.ActiveZoneMinutes);
        Assert.Equal(540, rope.PeakSeconds);
        Assert.False(rope.AutoDetected);
        HealthExercise walk = Assert.Single(profile.HealthDays.Single(d => d.Date == "2026-11-05").Exercises);
        Assert.Equal(1483.6, walk.DistanceMeters);
        Assert.Equal(1984, walk.Steps);
        Assert.True(walk.AutoDetected);

        // Synced again: the same sessions, not twice; a session gone from Google goes from the profile too
        GoogleHealthMapper.ApplyExercise(profile, ExerciseJson, new DateTime(2026, 10, 26), new DateTime(2026, 11, 8));
        Assert.Single(profile.HealthDays.Single(d => d.Date == "2026-11-08").Exercises);
        GoogleHealthMapper.ApplyExercise(profile, """{"dataPoints":[]}""", new DateTime(2026, 11, 8), new DateTime(2026, 11, 8));
        Assert.Empty(profile.HealthDays.Single(d => d.Date == "2026-11-08").Exercises);
        Assert.Single(profile.HealthDays.Single(d => d.Date == "2026-11-05").Exercises);
    }

    [Fact]
    public async Task A_timer_workout_and_the_watch_session_of_it_are_one_workout()
    {
        LifeProfile profile = WithWatch();
        List<TimeTrackerWorkouts.Session> timer = await Timer(profile,
            Logged(Jumprope, new DateTime(2026, 11, 8, 16, 4, 13, DateTimeKind.Utc)),
            Logged(Core, new DateTime(2026, 11, 6, 20, 52, 0, DateTimeKind.Utc)));

        string text = ActivityReport.Describe(profile, timer, null, Now, 7);

        Assert.StartsWith("Workouts in the last 7 days (2026-11-02 to 2026-11-08): 3 workouts on 3 days, 1h17m active, 63 Active Zone Minutes, 1 stopped early. Last one today.", text);
        Assert.Contains("- Thu 2026-11-05 11:37-12:22 Walk (watch, detected automatically): 45m06s active, 1.5 km, 1,984 steps, avg heart rate 102 bpm, heart rate zones light 31m / moderate 14m, 14 Active Zone Minutes, 190 kcal", text);
        Assert.Contains("- Fri 2026-11-06 21:40-21:52 Core (interval timer, no watch session): 2/5 rounds, 1m30s work, 20s rest, 2m05s total, stopped early", text);
        Assert.Contains("- Sun 2026-11-08 16:35-17:05 Skipping rope (watch and interval timer \"Jumprope\"): 6/6 rounds, 27m30s work, 2m30s rest, finished, 30m20s active, avg heart rate 152 bpm, heart rate zones moderate 10m / vigorous 11m / peak 9m, 49 Active Zone Minutes, 277 kcal", text);
        Assert.DoesNotContain("Jumprope (interval timer", text);
    }

    [Fact]
    public async Task Without_workouts_in_the_window_the_last_one_is_named()
    {
        LifeProfile profile = WithWatch();
        List<TimeTrackerWorkouts.Session> timer = await Timer(profile);

        string text = ActivityReport.Describe(profile, timer, null, Now.AddDays(5), 3);

        Assert.StartsWith("Workouts: none in the last 3 days (2026-11-11 to 2026-11-13), neither on the watch nor with the interval timer. The last one was 5 days ago: Sun 2026-11-08 16:35-17:05 Skipping rope (watch)", text);
    }

    [Fact]
    public void When_the_timer_cannot_be_read_the_watch_still_counts()
    {
        string text = ActivityReport.Describe(WithWatch(), null, "TimeTracker is not available.", Now, 7);

        Assert.Contains("2 workouts on 2 days", text);
        Assert.EndsWith("Interval timer: TimeTracker is not available.", text);
    }

    [Fact]
    public async Task Without_the_tracker_it_says_so_and_names_the_others()
    {
        FakeConnector connector = new();
        connector.Definitions.Add(new Definition { Id = "work", Name = "Akagi Work Log", UserId = "u" });

        (List<TimeTrackerWorkouts.Session>? sessions, _, string? problem) = await TimeTrackerWorkouts.LoadAsync(connector, Config, Berlin(), null);
        Assert.Null(sessions);
        Assert.Equal("No \"Workouts\" tracker yet: he has not logged a workout with the interval timer. Trackers: Akagi Work Log.", problem);

        (_, _, string? unknown) = await TimeTrackerWorkouts.LoadAsync(connector, Config, Berlin(), "Swimming");
        Assert.Equal("No tracker named \"Swimming\". Trackers: Akagi Work Log.", unknown);
    }

    [Fact]
    public async Task A_tracker_of_his_own_is_read_by_field_names()
    {
        FakeConnector connector = new();
        connector.Definitions.Add(new Definition { Id = "runs", Name = "Running", UserId = "u" });
        connector.Entries.Add(Logged("""{"Activity":"Park loop","Start":"2026-11-07T07:30:00Z","Minutes":42.5}""", new DateTime(2026, 11, 7, 9, 0, 0, DateTimeKind.Utc), "runs"));
        connector.Entries.Add(Logged("""{"Felt":"good","Km":5}""", new DateTime(2026, 11, 8, 9, 0, 0, DateTimeKind.Utc), "runs"));

        (List<TimeTrackerWorkouts.Session>? sessions, string name, _) = await TimeTrackerWorkouts.LoadAsync(connector, Config, Berlin(), "run");
        string text = ActivityReport.Describe(Berlin(), sessions, null, Now, 14);

        Assert.Equal("Running", name);
        // The UTC start comes out in Berlin time, the minutes as a duration
        Assert.Contains("- Sat 2026-11-07 08:30-09:12 Park loop (interval timer, no watch session): 42m30s total", text);
        // Nothing it knows: the entry's own values, at the time it was made
        Assert.Contains("- Sun 2026-11-08 10:00 workout (interval timer, no watch session): Felt=good, Km=5", text);
    }

    [Fact]
    public void The_life_context_line_names_the_latest_watch_workout()
    {
        HealthSummary summary = HealthSummary.Create(WithWatch(), Now);

        Assert.True(summary.HasData);
        Assert.Equal("Workouts on the watch (7 days): 2, 1h15m active, 63 Active Zone Minutes; latest today 16:35 Skipping rope, 30m20s, avg heart rate 152 bpm", summary.DescribeExercise());
        Assert.Contains(summary.DescribeExercise(), summary.Describe());
        Assert.DoesNotContain("Workouts on the watch", summary.Describe(withExercise: false));
    }

    [Fact]
    public async Task HealthStatus_shows_the_workouts_with_the_rest()
    {
        LifeTestContext test = new();
        LifeProfile profile = await test.Profiles.GetOrCreate(test.User.Id!);
        profile.TimeZoneId = "Europe/Berlin";
        GoogleHealthMapper.ApplyExercise(profile, ExerciseJson, DateTime.UtcNow.Date.AddDays(-13), DateTime.UtcNow.Date);
        await test.Profiles.SaveDocumentAsync(profile);

        string output = await test.Run<HealthStatusCommand>();

        Assert.Contains("Workouts", output);
        Assert.Contains("Interval timer: ", output);
    }

    [Fact]
    public void The_lookups_are_offered_during_a_consultation()
    {
        Assert.True(ToolGuard.IsOffered(new WorkoutHistoryCommand(), TurnSource.Consultation));
        Assert.True(ToolGuard.IsOffered(new HealthStatusCommand(), TurnSource.Consultation));
    }
}
