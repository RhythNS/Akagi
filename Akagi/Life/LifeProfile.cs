using Akagi.Data;
using Akagi.Life.Health;
using Akagi.Life.Mood;
using Akagi.Life.Study;
using Akagi.Life.Work;
using MongoDB.Bson.Serialization.Attributes;

namespace Akagi.Life;

/// <summary>
/// Everything the companion knows about the user's life outside the conversation: mood, worries, sleep,
/// work sessions and study progress. One document per user, shared by every character and subagent,
/// so the user experiences a single companion that remembers across all of them.
/// </summary>
internal class LifeProfile : Savable
{
    public const int MaxMoodEntries = 200;
    public const int MaxConcerns = 100;
    public const int MaxHealthDays = 120;
    public const int MaxWorkSessions = 500;

    private string _userId = string.Empty;
    private string _timeZoneId = string.Empty;

    [BsonRepresentation(MongoDB.Bson.BsonType.ObjectId)]
    public string UserId
    {
        get => _userId;
        set => SetProperty(ref _userId, value);
    }

    /// <summary>
    /// IANA or Windows time zone id. Empty means UTC.
    /// </summary>
    public string TimeZoneId
    {
        get => _timeZoneId;
        set => SetProperty(ref _timeZoneId, value);
    }

    public List<MoodEntry> Mood { get; set; } = [];
    public List<Concern> Concerns { get; set; } = [];
    public List<HealthDay> HealthDays { get; set; } = [];
    public WorkLog Work { get; set; } = new();
    public StudyProfile Study { get; set; } = new();

    public TimeZoneInfo TimeZone => LifeClock.FindTimeZone(TimeZoneId);

    public DateTime ToLocal(DateTime utc) => LifeClock.ToLocal(utc, TimeZone);

    public string LocalDate(DateTime utc) => LifeClock.DateKey(ToLocal(utc));

    public void AddMood(MoodEntry entry)
    {
        Mood.Add(entry);
        Trim(Mood, MaxMoodEntries);
        Dirty = true;
    }

    public Concern AddConcern(Concern concern)
    {
        Concerns.Add(concern);
        // Drop the oldest resolved concerns first
        while (Concerns.Count > MaxConcerns)
        {
            Concern? oldestResolved = Concerns.Where(c => c.Resolved).OrderBy(c => c.CreatedAt).FirstOrDefault();
            Concerns.Remove(oldestResolved ?? Concerns.OrderBy(c => c.CreatedAt).First());
        }
        Dirty = true;
        return concern;
    }

    public Concern? FindConcern(string idOrText)
    {
        return Concerns.FirstOrDefault(c => string.Equals(c.Id, idOrText, StringComparison.OrdinalIgnoreCase))
            ?? Concerns.Where(c => !c.Resolved)
                .FirstOrDefault(c => c.Text.Contains(idOrText, StringComparison.OrdinalIgnoreCase));
    }

    public HealthDay GetOrAddHealthDay(string date)
    {
        HealthDay? day = HealthDays.FirstOrDefault(d => d.Date == date);
        if (day == null)
        {
            day = new HealthDay { Date = date };
            HealthDays.Add(day);
            HealthDays.Sort((a, b) => string.CompareOrdinal(a.Date, b.Date));
            while (HealthDays.Count > MaxHealthDays)
            {
                HealthDays.RemoveAt(0);
            }
        }
        Dirty = true;
        return day;
    }

    public void MarkChanged()
    {
        Dirty = true;
    }

    private static void Trim<T>(List<T> list, int max)
    {
        if (list.Count > max)
        {
            list.RemoveRange(0, list.Count - max);
        }
    }
}
