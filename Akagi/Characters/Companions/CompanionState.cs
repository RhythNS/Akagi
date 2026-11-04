using Akagi.Data;
using Microsoft.Extensions.Options;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;

namespace Akagi.Characters.Companions;

internal enum ActivityMode
{
    None,
    /// <summary>Watching a movie or series together.</summary>
    Movie,
    Anime,
    /// <summary>Working side by side: quiet company, short replies.</summary>
    CoWork,
    Gaming,
    Study,
    Walk,
    Cooking,
    /// <summary>Winding down for sleep.</summary>
    Bedtime,
    Other
}

internal class Outfit
{
    public string Description { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public DateTime ChangedAt { get; set; } = DateTime.UtcNow;
}

internal class SharedActivity
{
    public ActivityMode Mode { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Note { get; set; } = string.Empty;
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? EndedAt { get; set; }
    public string Summary { get; set; } = string.Empty;

    /// <summary>
    /// Activities nobody ended stop counting after a while, so a forgotten movie night does not mute her for days.
    /// </summary>
    public TimeSpan MaxDuration => Mode switch
    {
        ActivityMode.CoWork or ActivityMode.Study => TimeSpan.FromHours(8),
        ActivityMode.Bedtime => TimeSpan.FromHours(10),
        _ => TimeSpan.FromHours(4)
    };

    public bool IsRunning(DateTime nowUtc) => EndedAt == null && nowUtc - StartedAt < MaxDuration;

    /// <summary>
    /// Activities during which only explicitly scheduled check-ins may interrupt.
    /// </summary>
    public bool IsImmersive => Mode is ActivityMode.Movie or ActivityMode.Anime or ActivityMode.Gaming or ActivityMode.Bedtime;
}

internal enum PersonGender
{
    Unknown,
    Female,
    Male,
    Other
}

/// <summary>
/// Someone in the user's life the companion has heard about.
/// </summary>
internal class Person
{
    public string Name { get; set; } = string.Empty;
    public string Relation { get; set; } = string.Empty;
    public PersonGender Gender { get; set; }
    public List<string> Notes { get; set; } = [];
    public int Mentions { get; set; }
    public DateTime FirstMentioned { get; set; } = DateTime.UtcNow;
    public DateTime LastMentioned { get; set; } = DateTime.UtcNow;

    public void AddNote(string note)
    {
        if (string.IsNullOrWhiteSpace(note) || Notes.Contains(note.Trim(), StringComparer.OrdinalIgnoreCase))
        {
            return;
        }
        // Every note is kept: the companion's context only shows the latest ones, and the rest is the user's record
        Notes.Add(note.Trim());
    }

    public void RemoveNote(string note)
    {
        Notes.RemoveAll(n => string.Equals(n, note.Trim(), StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>
/// The companion's own side of the relationship with one user: what she is wearing, what they are doing together,
/// and the people she knows from his stories.
/// </summary>
internal class CompanionState : Savable
{
    public const int MaxOutfitHistory = 8;
    public const int MaxActivityHistory = 30;
    public const int MaxPeople = 80;

    private string _userId = string.Empty;
    private string _characterId = string.Empty;

    [BsonRepresentation(MongoDB.Bson.BsonType.ObjectId)]
    public string UserId
    {
        get => _userId;
        set => SetProperty(ref _userId, value);
    }
    public string CharacterId
    {
        get => _characterId;
        set => SetProperty(ref _characterId, value);
    }

    public Outfit? Outfit { get; set; }
    public List<Outfit> OutfitHistory { get; set; } = [];
    public SharedActivity? Activity { get; set; }
    public List<SharedActivity> ActivityHistory { get; set; } = [];
    public List<Person> People { get; set; } = [];

    public SharedActivity? RunningActivity(DateTime nowUtc) => Activity?.IsRunning(nowUtc) == true ? Activity : null;

    public void ChangeOutfit(string description, string reason, DateTime nowUtc)
    {
        if (Outfit != null)
        {
            OutfitHistory.Add(Outfit);
            if (OutfitHistory.Count > MaxOutfitHistory)
            {
                OutfitHistory.RemoveAt(0);
            }
        }
        Outfit = new Outfit { Description = description.Trim(), Reason = reason.Trim(), ChangedAt = nowUtc };
        Dirty = true;
    }

    /// <summary>
    /// Starts an activity, ending a running one first. Returns the activity that was ended, if any.
    /// </summary>
    public SharedActivity? StartActivity(ActivityMode mode, string title, string note, DateTime nowUtc)
    {
        SharedActivity? ended = EndActivity(string.Empty, nowUtc);
        Activity = new SharedActivity { Mode = mode, Title = title.Trim(), Note = note.Trim(), StartedAt = nowUtc };
        Dirty = true;
        return ended;
    }

    public SharedActivity? EndActivity(string summary, DateTime nowUtc)
    {
        if (Activity == null)
        {
            return null;
        }

        SharedActivity ended = Activity;
        // A forgotten activity ends when it stopped counting, not when somebody finally noticed
        ended.EndedAt = ended.IsRunning(nowUtc) ? nowUtc : ended.StartedAt + ended.MaxDuration;
        ended.Summary = summary.Trim();
        ActivityHistory.Add(ended);
        if (ActivityHistory.Count > MaxActivityHistory)
        {
            ActivityHistory.RemoveAt(0);
        }
        Activity = null;
        Dirty = true;
        return ended;
    }

    public Person RememberPerson(string name, string relation, PersonGender? gender, string note, DateTime nowUtc)
    {
        Person? person = FindPerson(name);
        if (person == null)
        {
            person = new Person { Name = name.Trim(), FirstMentioned = nowUtc };
            People.Add(person);
            if (People.Count > MaxPeople)
            {
                People.Remove(People.OrderBy(p => p.LastMentioned).First());
            }
        }
        if (!string.IsNullOrWhiteSpace(relation))
        {
            person.Relation = relation.Trim();
        }
        if (gender is PersonGender known and not PersonGender.Unknown)
        {
            person.Gender = known;
        }
        person.AddNote(note);
        person.Mentions++;
        person.LastMentioned = nowUtc;
        Dirty = true;
        return person;
    }

    public Person? FindPerson(string name) =>
        People.FirstOrDefault(p => string.Equals(p.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));
}

internal interface ICompanionStateDatabase : IDatabase<CompanionState>
{
    public Task<CompanionState?> Get(string userId, string characterId);
    public Task<CompanionState> GetOrCreate(string userId, string characterId);
}

internal class CompanionStateDatabase : Database<CompanionState>, ICompanionStateDatabase
{
    public override string CollectionName => "companion_states";

    public CompanionStateDatabase(IOptionsMonitor<DatabaseOptions> options) : base(options)
    {
    }

    public override bool CanSave(Savable savable) => savable is CompanionState;

    public override Task SaveAsync(Savable savable) => SaveDocumentAsync((CompanionState)savable);

    public async Task<CompanionState?> Get(string userId, string characterId)
    {
        FilterDefinition<CompanionState> filter = Builders<CompanionState>.Filter.Eq(s => s.UserId, userId)
            & Builders<CompanionState>.Filter.Eq(s => s.CharacterId, characterId);
        return (await GetDocumentsByPredicateAsync(filter)).FirstOrDefault();
    }

    public async Task<CompanionState> GetOrCreate(string userId, string characterId)
    {
        CompanionState? state = await Get(userId, characterId);
        if (state != null)
        {
            return state;
        }
        state = new CompanionState { UserId = userId, CharacterId = characterId };
        await SaveDocumentAsync(state);
        return state;
    }
}
