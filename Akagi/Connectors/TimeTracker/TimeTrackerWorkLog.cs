using Akagi.Life;
using Akagi.Life.Work;

namespace Akagi.Connectors.TimeTracker;

/// <summary>
/// Mirrors finished work sessions into a TimeTracker definition so they show up in Akagi.Web next to the user's other trackers.
/// </summary>
internal static class TimeTrackerWorkLog
{
    public const string DefinitionName = "Akagi Work Log";

    public static readonly List<FieldDefinition> Fields =
    [
        new FieldDefinition { Name = "Project", Type = FieldType.Text },
        new FieldDefinition { Name = "Task", Type = FieldType.Text },
        new FieldDefinition { Name = "Start", Type = FieldType.DateTime },
        new FieldDefinition { Name = "End", Type = FieldType.DateTime },
        new FieldDefinition { Name = "Hours", Type = FieldType.Float },
        new FieldDefinition { Name = "Notes", Type = FieldType.Text },
    ];

    public static async Task<Definition> EnsureDefinitionAsync(ITimeTrackerConnector connector, TimeTrackerUserConfig config)
    {
        List<Definition> definitions = await connector.GetDefinitionsAsync(config);
        Definition? existing = definitions.FirstOrDefault(d => string.Equals(d.Name, DefinitionName, StringComparison.OrdinalIgnoreCase));
        return existing ?? await connector.CreateDefinitionAsync(config, DefinitionName, Fields);
    }

    public static Dictionary<string, object> BuildValues(WorkSession session, DateTime nowUtc)
    {
        DateTime end = session.EndUtc ?? nowUtc;
        return new Dictionary<string, object>
        {
            ["Project"] = session.Project,
            ["Task"] = session.Task,
            ["Start"] = DateTime.SpecifyKind(session.StartUtc, DateTimeKind.Utc),
            ["End"] = DateTime.SpecifyKind(end, DateTimeKind.Utc),
            ["Hours"] = Math.Round((end - session.StartUtc).TotalHours, 2),
            ["Notes"] = session.Notes
        };
    }

    /// <summary>
    /// Pushes a finished session. Returns the created entry id.
    /// </summary>
    public static async Task<string?> SyncAsync(ITimeTrackerConnector connector, TimeTrackerUserConfig config, WorkSession session, DateTime nowUtc)
    {
        Definition definition = await EnsureDefinitionAsync(connector, config);
        Entry entry = await connector.CreateEntryAsync(config, definition.Id!, BuildValues(session, nowUtc), session.EndUtc ?? nowUtc);
        session.TimeTrackerEntryId = entry.Id;
        return entry.Id;
    }

    /// <summary>
    /// Describes the user's other TimeTracker definitions and their latest entries, so the companion knows what else they track.
    /// </summary>
    public static async Task<string> DescribeAsync(ITimeTrackerConnector connector, TimeTrackerUserConfig config, LifeProfile profile, int maxEntries)
    {
        List<Definition> definitions = await connector.GetDefinitionsAsync(config);
        if (definitions.Count == 0)
        {
            return "No TimeTracker definitions yet.";
        }

        List<Entry> entries = await connector.GetEntriesAsync(config);
        List<string> lines = [];
        foreach (Definition definition in definitions)
        {
            List<Entry> latest = [.. entries.Where(e => e.DefinitionId == definition.Id).OrderByDescending(e => e.CreatedAt).Take(maxEntries)];
            lines.Add($"{definition.Name} ({string.Join(", ", definition.Fields.Select(f => $"{f.Name}:{f.Type}"))}) - {entries.Count(e => e.DefinitionId == definition.Id)} entries");
            foreach (Entry entry in latest)
            {
                string values = string.Join(", ", entry.Values.Select(kv => $"{kv.Key}={kv.Value}"));
                lines.Add($"  {profile.ToLocal(entry.CreatedAt):yyyy-MM-dd HH:mm}: {values}");
            }
        }
        return string.Join(Environment.NewLine, lines);
    }
}
