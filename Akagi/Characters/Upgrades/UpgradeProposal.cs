using Akagi.Data;
using Microsoft.Extensions.Options;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;

namespace Akagi.Characters.Upgrades;

internal enum UpgradePriority
{
    Low,
    Medium,
    High
}

internal enum UpgradeStatus
{
    Proposed,
    Accepted,
    Rejected,
    Done
}

/// <summary>
/// A feature the companion asked its developer to build into her: the dev backlog she keeps herself.
/// </summary>
internal class UpgradeProposal : Savable
{
    private string _userId = string.Empty;
    private string _characterId = string.Empty;
    private string _title = string.Empty;
    private string _description = string.Empty;
    private string _motivation = string.Empty;
    private string _area = string.Empty;
    private UpgradePriority _priority = UpgradePriority.Medium;
    private UpgradeStatus _status = UpgradeStatus.Proposed;
    private int _mentions = 1;
    private string _developerNote = string.Empty;
    private DateTime _createdAt = DateTime.UtcNow;
    private DateTime _updatedAt = DateTime.UtcNow;
    private DateTime? _acknowledgedAt;

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
    public string Title
    {
        get => _title;
        set => SetProperty(ref _title, value);
    }
    public string Description
    {
        get => _description;
        set => SetProperty(ref _description, value);
    }
    public string Motivation
    {
        get => _motivation;
        set => SetProperty(ref _motivation, value);
    }
    public string Area
    {
        get => _area;
        set => SetProperty(ref _area, value);
    }
    public UpgradePriority Priority
    {
        get => _priority;
        set => SetProperty(ref _priority, value);
    }
    public UpgradeStatus Status
    {
        get => _status;
        set => SetProperty(ref _status, value);
    }
    /// <summary>
    /// How often the companion brought this idea up again.
    /// </summary>
    public int Mentions
    {
        get => _mentions;
        set => SetProperty(ref _mentions, value);
    }
    public string DeveloperNote
    {
        get => _developerNote;
        set => SetProperty(ref _developerNote, value);
    }
    public DateTime CreatedAt
    {
        get => _createdAt;
        set => SetProperty(ref _createdAt, value);
    }
    public DateTime UpdatedAt
    {
        get => _updatedAt;
        set => SetProperty(ref _updatedAt, value);
    }
    /// <summary>
    /// When the companion was told about the latest status change, so she reacts to a finished upgrade once.
    /// </summary>
    public DateTime? AcknowledgedAt
    {
        get => _acknowledgedAt;
        set => SetProperty(ref _acknowledgedAt, value);
    }

    public bool IsOpen => Status is UpgradeStatus.Proposed or UpgradeStatus.Accepted;

    public string ShortId => Id is { Length: > 6 } id ? id[^6..] : Id ?? string.Empty;

    public override string ToString()
    {
        string note = string.IsNullOrWhiteSpace(DeveloperNote) ? string.Empty : $" Note: {DeveloperNote}";
        string mentions = Mentions > 1 ? $", asked {Mentions}x" : string.Empty;
        return $"[{ShortId}] {Title} ({Status}, {Priority}{mentions}): {Description}{note}";
    }

    /// <summary>
    /// Word overlap between two titles, used to merge an idea that comes up again instead of filing it twice.
    /// </summary>
    public static double Similarity(string a, string b)
    {
        HashSet<string> left = Words(a);
        HashSet<string> right = Words(b);
        if (left.Count == 0 || right.Count == 0)
        {
            return 0;
        }
        int shared = left.Count(right.Contains);
        return (double)shared / Math.Min(left.Count, right.Count);
    }

    private static readonly HashSet<string> StopWords = ["a", "an", "the", "to", "for", "of", "and", "with", "me", "my", "module", "feature", "add", "so", "can", "i"];

    private static HashSet<string> Words(string text) =>
        [.. text.ToLowerInvariant()
            .Split([' ', ',', '.', '-', '_', ':', ';', '!', '?', '(', ')', '/', '\''], StringSplitOptions.RemoveEmptyEntries)
            .Where(w => !StopWords.Contains(w))];
}

internal interface IUpgradeProposalDatabase : IDatabase<UpgradeProposal>
{
    public Task<List<UpgradeProposal>> GetForUser(string userId);
}

internal class UpgradeProposalDatabase : Database<UpgradeProposal>, IUpgradeProposalDatabase
{
    public override string CollectionName => "upgrade_proposals";

    public UpgradeProposalDatabase(IOptionsMonitor<DatabaseOptions> options) : base(options)
    {
    }

    public override bool CanSave(Savable savable) => savable is UpgradeProposal;

    public override Task SaveAsync(Savable savable) => SaveDocumentAsync((UpgradeProposal)savable);

    public Task<List<UpgradeProposal>> GetForUser(string userId)
    {
        return GetDocumentsByPredicateAsync(Builders<UpgradeProposal>.Filter.Eq(p => p.UserId, userId));
    }
}
