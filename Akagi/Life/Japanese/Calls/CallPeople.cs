using Akagi.Characters.Companions;
using Akagi.Characters.Memories;
using Akagi.Data;
using Akagi.Embeddings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;

namespace Akagi.Life.Japanese.Calls;

/// <summary>
/// One thing learned about someone in a call, with where it came from.
/// </summary>
internal class PersonFact
{
    /// <summary>
    /// The call finding it came from; taking the finding back removes the fact.
    /// </summary>
    public string Id { get; set; } = string.Empty;
    public string CallId { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public string Topic { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public string Quote { get; set; } = string.Empty;
    public double? Time { get; set; }
    public bool FollowUp { get; set; }
}

internal class PersonCall
{
    public string CallId { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public double DurationSeconds { get; set; }
}

/// <summary>
/// Everything the user's calls taught about one person. Nothing is ever trimmed: it is the user's own record to read
/// before talking to them again.
/// </summary>
internal class PersonProfile : Savable
{
    private string _userId = string.Empty;
    private string _name = string.Empty;

    [BsonRepresentation(MongoDB.Bson.BsonType.ObjectId)]
    public string UserId
    {
        get => _userId;
        set => SetProperty(ref _userId, value);
    }
    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }
    public List<PersonFact> Facts { get; set; } = [];
    public List<PersonCall> Calls { get; set; } = [];

    public DateTime? LastTalked => Calls.Count == 0 ? null : Calls.Max(c => c.Date);

    public void AddCall(CallJob job)
    {
        if (Calls.Any(c => c.CallId == job.Id))
        {
            return;
        }
        Calls.Add(new PersonCall { CallId = job.Id!, Date = job.RecordedAt ?? job.CreatedAt, DurationSeconds = job.DurationSeconds });
        Dirty = true;
    }
}

internal interface IPersonProfileDatabase : IDatabase<PersonProfile>
{
    public Task<List<PersonProfile>> GetForUser(string userId);
    public Task<PersonProfile?> Get(string userId, string name);
}

internal class PersonProfileDatabase : Database<PersonProfile>, IPersonProfileDatabase
{
    public override string CollectionName => "person_profiles";

    public PersonProfileDatabase(IOptionsMonitor<DatabaseOptions> options) : base(options)
    {
    }

    public override bool CanSave(Savable savable) => savable is PersonProfile;

    public override Task SaveAsync(Savable savable) => SaveDocumentAsync((PersonProfile)savable);

    public Task<List<PersonProfile>> GetForUser(string userId) =>
        GetDocumentsByPredicateAsync(Builders<PersonProfile>.Filter.Eq(p => p.UserId, userId));

    public async Task<PersonProfile?> Get(string userId, string name) =>
        (await GetForUser(userId)).FirstOrDefault(p => string.Equals(p.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// Where a call's facts go: about the partner into their profile and the companion's people, about the learner into the
/// companion's memories. Everything written can be taken back exactly.
/// </summary>
internal interface ICallMemory
{
    /// <summary>
    /// What is already known, so the facts pass adds rather than repeats.
    /// </summary>
    public Task<(List<string> Partner, List<string> Learner)> KnownAsync(CallJob job);

    public Task ApplyAsync(CallJob job, CallFinding finding, DateTime nowUtc);

    public Task UndoAsync(CallJob job, CallFinding finding);

    /// <summary>
    /// Records that the call happened, so the person's page lists it even when nothing new was learned.
    /// </summary>
    public Task RecordCallAsync(CallJob job);
}

internal class CallMemory : ICallMemory
{
    public const string MemoryCategory = "Call";
    public const float FactImportance = 0.8f;
    private const int MaxKnown = 150;

    private readonly IPersonProfileDatabase _people;
    private readonly IMemoryDatabase _memories;
    private readonly ICompanionStateDatabase _companions;
    private readonly IEmbeddingService? _embeddings;
    private readonly ILogger _logger;

    public CallMemory(IPersonProfileDatabase people, IMemoryDatabase memories, ICompanionStateDatabase companions,
        IEmbeddingService? embeddings, ILogger<CallMemory> logger)
    {
        _people = people;
        _memories = memories;
        _companions = companions;
        _embeddings = embeddings;
        _logger = logger;
    }

    public async Task<(List<string> Partner, List<string> Learner)> KnownAsync(CallJob job)
    {
        List<string> partner = [];
        if (!string.IsNullOrWhiteSpace(job.Partner) && await _people.Get(job.UserId, job.Partner) is PersonProfile profile)
        {
            partner = [.. profile.Facts.OrderByDescending(f => f.Date).Take(MaxKnown).Select(f => f.Text)];
        }
        List<string> learner = [];
        if (!string.IsNullOrEmpty(job.DeliveryCharacterId))
        {
            learner = [.. (await _memories.GetActiveMemoriesAsync(job.DeliveryCharacterId, job.UserId, includeGlobalLore: false))
                .Where(m => m.Category == MemoryCategory)
                .OrderByDescending(m => m.Timestamp)
                .Take(MaxKnown)
                .Select(m => m.Fact)];
        }
        return (partner, learner);
    }

    public async Task RecordCallAsync(CallJob job)
    {
        if (string.IsNullOrWhiteSpace(job.Partner))
        {
            return;
        }
        PersonProfile profile = await ProfileAsync(job);
        profile.AddCall(job);
        await _people.SaveDocumentAsync(profile);
    }

    public async Task ApplyAsync(CallJob job, CallFinding finding, DateTime nowUtc)
    {
        if (finding.Kind != FindingKind.Fact || finding.Applied || !finding.Counts)
        {
            return;
        }

        if (finding.Subject == FactSubject.Partner)
        {
            if (string.IsNullOrWhiteSpace(job.Partner))
            {
                // Nobody to file it under; it stays on the call
                return;
            }
            PersonProfile profile = await ProfileAsync(job);
            profile.AddCall(job);
            if (profile.Facts.All(f => f.Id != finding.Id))
            {
                profile.Facts.Add(new PersonFact
                {
                    Id = finding.Id,
                    CallId = job.Id!,
                    Date = job.RecordedAt ?? job.CreatedAt,
                    Topic = finding.Aspect,
                    Text = finding.Pattern,
                    Quote = finding.Said,
                    Time = finding.Time,
                    FollowUp = finding.FollowUp
                });
            }
            await _people.SaveDocumentAsync(profile);

            if (!string.IsNullOrEmpty(job.DeliveryCharacterId))
            {
                CompanionState state = await _companions.GetOrCreate(job.UserId, job.DeliveryCharacterId);
                state.RememberPerson(job.Partner, "call partner", null, finding.Pattern, nowUtc);
                await _companions.SaveDocumentAsync(state);
            }
        }
        else if (!string.IsNullOrEmpty(job.DeliveryCharacterId))
        {
            MemoryRecord memory = new()
            {
                CharacterId = job.DeliveryCharacterId,
                UserId = job.UserId,
                Fact = $"From the user's call with {job.PartnerLabel}: {finding.Pattern}",
                Embedding = await EmbedAsync(finding.Pattern),
                Importance = FactImportance,
                Category = MemoryCategory,
                Timestamp = nowUtc,
                LastAccessed = nowUtc
            };
            await _memories.SaveDocumentAsync(memory);
            finding.MemoryId = memory.Id ?? string.Empty;
        }
        finding.Applied = true;
    }

    public async Task UndoAsync(CallJob job, CallFinding finding)
    {
        if (finding.Kind != FindingKind.Fact || !finding.Applied)
        {
            return;
        }
        finding.Applied = false;

        if (finding.Subject == FactSubject.Partner)
        {
            if (await _people.Get(job.UserId, job.Partner) is PersonProfile profile && profile.Facts.RemoveAll(f => f.Id == finding.Id) > 0)
            {
                profile.Dirty = true;
                await _people.SaveDocumentAsync(profile);
            }
            if (!string.IsNullOrEmpty(job.DeliveryCharacterId)
                && await _companions.Get(job.UserId, job.DeliveryCharacterId) is CompanionState state
                && state.FindPerson(job.Partner) is Person person)
            {
                person.RemoveNote(finding.Pattern);
                state.Dirty = true;
                await _companions.SaveDocumentAsync(state);
            }
        }
        else if (finding.MemoryId.Length > 0)
        {
            await _memories.DeleteDocumentByIdAsync(finding.MemoryId);
            finding.MemoryId = string.Empty;
        }
    }

    private async Task<PersonProfile> ProfileAsync(CallJob job) =>
        await _people.Get(job.UserId, job.Partner) ?? new PersonProfile { UserId = job.UserId, Name = job.Partner.Trim() };

    private async Task<float[]> EmbedAsync(string text)
    {
        if (_embeddings == null)
        {
            return [];
        }
        try
        {
            return await _embeddings.GenerateEmbeddingAsync(text);
        }
        catch (Exception ex)
        {
            // The memory is still found by recency and importance without one
            _logger.LogWarning(ex, "Could not embed a call fact");
            return [];
        }
    }
}
