using Akagi.Bridge.Chat.Models;
using Akagi.Data;
using Microsoft.Extensions.Options;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;

namespace Akagi.Life.Japanese.Mining;

/// <summary>
/// A sentence from a call the user wants to learn from: a line they picked in a transcript, a search result, or an
/// example of a word they hear but do not say. Its clip is cut once, kept in the call's folder, and downloaded as
/// audio, subtitles or an Anki card.
/// </summary>
[BsonIgnoreExtraElements]
internal class MinedSentence : Savable
{
    private string _userId = string.Empty;
    private MinedClipState _state;

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
    public DateTime AddedAt { get; set; } = DateTime.UtcNow;
    public MinedClipState State
    {
        get => _state;
        set => SetProperty(ref _state, value);
    }
    /// <summary>Why the clip could not be cut.</summary>
    public string Error { get; set; } = string.Empty;
    /// <summary>Seconds of the cut clip.</summary>
    public double ClipSeconds { get; set; }
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
