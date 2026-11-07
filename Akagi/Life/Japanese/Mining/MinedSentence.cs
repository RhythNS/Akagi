using Akagi.Data;
using Microsoft.Extensions.Options;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;

namespace Akagi.Life.Japanese.Mining;

/// <summary>
/// A sentence from a call the user wants to learn from: a line they picked in a transcript, a search result, or an
/// example of a word they hear but do not say. Exported as audio clips with an Anki import file.
/// </summary>
internal class MinedSentence : Savable
{
    private string _userId = string.Empty;
    private string _note = string.Empty;

    [BsonRepresentation(MongoDB.Bson.BsonType.ObjectId)]
    public string UserId
    {
        get => _userId;
        set => SetProperty(ref _userId, value);
    }
    public string CallId { get; set; } = string.Empty;
    public string CallName { get; set; } = string.Empty;
    /// <summary>When the call took place.</summary>
    public DateTime Date { get; set; }
    /// <summary>Seconds into the call where the line starts and ends; the clip is cut from these.</summary>
    public double Start { get; set; }
    public double End { get; set; }
    public string Speaker { get; set; } = string.Empty;
    public bool IsLearner { get; set; }
    public string Text { get; set; } = string.Empty;
    /// <summary>The word or expression it was mined for, if any.</summary>
    public string Target { get; set; } = string.Empty;
    public string Note
    {
        get => _note;
        set => SetProperty(ref _note, value);
    }
    public DateTime AddedAt { get; set; } = DateTime.UtcNow;
}

internal interface IMinedSentenceDatabase : IDatabase<MinedSentence>
{
    public Task<List<MinedSentence>> GetForUser(string userId);

    /// <summary>
    /// Removes what was mined from a deleted call; returns how many.
    /// </summary>
    public Task<int> DeleteForCallAsync(string userId, string callId);
}

internal class MinedSentenceDatabase : Database<MinedSentence>, IMinedSentenceDatabase
{
    public override string CollectionName => "mined_sentences";

    public MinedSentenceDatabase(IOptionsMonitor<DatabaseOptions> options) : base(options)
    {
    }

    public override bool CanSave(Savable savable) => savable is MinedSentence;

    public override Task SaveAsync(Savable savable) => SaveDocumentAsync((MinedSentence)savable);

    public Task<List<MinedSentence>> GetForUser(string userId) =>
        GetDocumentsByPredicateAsync(Builders<MinedSentence>.Filter.Eq(m => m.UserId, userId));

    public async Task<int> DeleteForCallAsync(string userId, string callId)
    {
        List<MinedSentence> mined = await GetDocumentsByPredicateAsync(Builders<MinedSentence>.Filter.And(
            Builders<MinedSentence>.Filter.Eq(m => m.UserId, userId),
            Builders<MinedSentence>.Filter.Eq(m => m.CallId, callId)));
        foreach (MinedSentence sentence in mined.Where(m => m.Id != null))
        {
            await DeleteDocumentByIdAsync(sentence.Id!);
        }
        return mined.Count;
    }
}
