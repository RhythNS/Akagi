using Akagi.Bridge.Japanese;
using Akagi.Data;
using Microsoft.Extensions.Options;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;

namespace Akagi.Life.Japanese.Conjugations;

/// <summary>
/// Progress on one practice question: a verb in a form, asked one way round. One document per question, so a long
/// practice history never grows a single document.
/// </summary>
[BsonIgnoreExtraElements]
internal class ConjugationItem : Savable
{
    [BsonRepresentation(MongoDB.Bson.BsonType.ObjectId)]
    public string UserId { get; set; } = string.Empty;
    /// <summary>Written form and reading, <c>書く[かく]</c>.</summary>
    public string Verb { get; set; } = string.Empty;
    public string Written { get; set; } = string.Empty;
    public string Reading { get; set; } = string.Empty;
    public VerbClass Class { get; set; }
    public ConjugationForm Form { get; set; }
    public bool Reverse { get; set; }
    public int Attempts { get; set; }
    public int Correct { get; set; }
    /// <summary>The latest results, oldest first: 1 right, 0 wrong.</summary>
    public string Recent { get; set; } = string.Empty;
    public DateTime LastSeen { get; set; }
    public DateTime? LastWrong { get; set; }
    /// <summary>The latest wrong answers, newest last.</summary>
    public List<string> WrongAnswers { get; set; } = [];
}

/// <summary>
/// One answer, kept for the activity per day and the list of mistakes.
/// </summary>
[BsonIgnoreExtraElements]
internal class ConjugationAttempt : Savable
{
    [BsonRepresentation(MongoDB.Bson.BsonType.ObjectId)]
    public string UserId { get; set; } = string.Empty;
    public string Verb { get; set; } = string.Empty;
    public string Written { get; set; } = string.Empty;
    public string Reading { get; set; } = string.Empty;
    public VerbClass Class { get; set; }
    public ConjugationForm Form { get; set; }
    public bool Reverse { get; set; }
    public string Answer { get; set; } = string.Empty;
    public bool Correct { get; set; }
    public int Millis { get; set; }
    public DateTime At { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// The practice settings of a user.
/// </summary>
[BsonIgnoreExtraElements]
internal class ConjugationPreferences : Savable
{
    [BsonRepresentation(MongoDB.Bson.BsonType.ObjectId)]
    public string UserId { get; set; } = string.Empty;
    public List<ConjugationForm> Forms { get; set; } = [];
    public bool Godan { get; set; } = true;
    public bool Ichidan { get; set; } = true;
    public bool Irregular { get; set; } = true;
    public int Pool { get; set; }
    public int CommonLimit { get; set; } = 5_000;
    public int Mode { get; set; }
    public bool Reverse { get; set; }
    public bool Randomize { get; set; }
    public bool ShowKanji { get; set; } = true;
    public bool ShowFurigana { get; set; }
    public bool HideType { get; set; }
    public bool ShowEnglish { get; set; }
}

internal interface IConjugationItemDatabase : IDatabase<ConjugationItem>
{
    public Task<List<ConjugationItem>> GetForUser(string userId);
    public Task<ConjugationItem?> FindAsync(string userId, string verb, ConjugationForm form, bool reverse);
    public Task DeleteForUserAsync(string userId);
}

internal interface IConjugationAttemptDatabase : IDatabase<ConjugationAttempt>
{
    public Task<List<ConjugationAttempt>> GetSinceAsync(string userId, DateTime since);
    /// <summary>The latest wrong answers, newest first.</summary>
    public Task<List<ConjugationAttempt>> GetMistakesAsync(string userId, int count);
    public Task DeleteForUserAsync(string userId);
}

internal interface IConjugationPreferencesDatabase : IDatabase<ConjugationPreferences>
{
    public Task<ConjugationPreferences?> GetForUser(string userId);
}

internal class ConjugationItemDatabase : Database<ConjugationItem>, IConjugationItemDatabase
{
    public override string CollectionName => "conjugation_items";

    public ConjugationItemDatabase(IOptionsMonitor<DatabaseOptions> options) : base(options)
    {
    }

    public override bool CanSave(Savable savable) => savable is ConjugationItem;

    public override Task SaveAsync(Savable savable) => SaveDocumentAsync((ConjugationItem)savable);

    public Task<List<ConjugationItem>> GetForUser(string userId) =>
        GetDocumentsByPredicateAsync(Builders<ConjugationItem>.Filter.Eq(i => i.UserId, userId));

    public async Task<ConjugationItem?> FindAsync(string userId, string verb, ConjugationForm form, bool reverse) =>
        (await GetDocumentsByPredicateAsync(Builders<ConjugationItem>.Filter.And(
            Builders<ConjugationItem>.Filter.Eq(i => i.UserId, userId),
            Builders<ConjugationItem>.Filter.Eq(i => i.Verb, verb),
            Builders<ConjugationItem>.Filter.Eq(i => i.Form, form),
            Builders<ConjugationItem>.Filter.Eq(i => i.Reverse, reverse)))).FirstOrDefault();

    public Task DeleteForUserAsync(string userId) =>
        GetCollection().DeleteManyAsync(Builders<ConjugationItem>.Filter.Eq(i => i.UserId, userId));
}

internal class ConjugationAttemptDatabase : Database<ConjugationAttempt>, IConjugationAttemptDatabase
{
    public override string CollectionName => "conjugation_attempts";

    public ConjugationAttemptDatabase(IOptionsMonitor<DatabaseOptions> options) : base(options)
    {
    }

    public override bool CanSave(Savable savable) => savable is ConjugationAttempt;

    public override Task SaveAsync(Savable savable) => SaveDocumentAsync((ConjugationAttempt)savable);

    public Task<List<ConjugationAttempt>> GetSinceAsync(string userId, DateTime since) =>
        GetDocumentsByPredicateAsync(Builders<ConjugationAttempt>.Filter.And(
            Builders<ConjugationAttempt>.Filter.Eq(a => a.UserId, userId),
            Builders<ConjugationAttempt>.Filter.Gte(a => a.At, since)));

    public Task<List<ConjugationAttempt>> GetMistakesAsync(string userId, int count) =>
        GetCollection()
            .Find(Builders<ConjugationAttempt>.Filter.And(
                Builders<ConjugationAttempt>.Filter.Eq(a => a.UserId, userId),
                Builders<ConjugationAttempt>.Filter.Eq(a => a.Correct, false)))
            .SortByDescending(a => a.At)
            .Limit(count)
            .ToListAsync();

    public Task DeleteForUserAsync(string userId) =>
        GetCollection().DeleteManyAsync(Builders<ConjugationAttempt>.Filter.Eq(a => a.UserId, userId));
}

internal class ConjugationPreferencesDatabase : Database<ConjugationPreferences>, IConjugationPreferencesDatabase
{
    public override string CollectionName => "conjugation_preferences";

    public ConjugationPreferencesDatabase(IOptionsMonitor<DatabaseOptions> options) : base(options)
    {
    }

    public override bool CanSave(Savable savable) => savable is ConjugationPreferences;

    public override Task SaveAsync(Savable savable) => SaveDocumentAsync((ConjugationPreferences)savable);

    public async Task<ConjugationPreferences?> GetForUser(string userId) =>
        (await GetDocumentsByPredicateAsync(Builders<ConjugationPreferences>.Filter.Eq(p => p.UserId, userId))).FirstOrDefault();
}
