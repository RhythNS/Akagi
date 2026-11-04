using Akagi.Data;
using Microsoft.Extensions.Options;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;

namespace Akagi.Life.Japanese.Calls;

internal enum CallJobStatus
{
    Queued,
    Transcribing,
    Analyzing,
    Reporting,
    Completed,
    Failed,
    /// <summary>
    /// All chunks are reviewed; one more request merges, checks and ranks their findings. Numbered after the others
    /// because statuses are stored as numbers.
    /// </summary>
    Consolidating,
    /// <summary>One request over the whole call on how the conversation went.</summary>
    Coaching,
    /// <summary>One request over the whole call for what was learned about the people in it.</summary>
    Remembering
}

/// <summary>
/// Which voice in a recording is the learner, so the local measurements only look at the user's own speech.
/// </summary>
internal enum LearnerChannel
{
    /// <summary>Both voices are mixed; measurements cover everyone and are only rough.</summary>
    Mixed,
    /// <summary>The learner is on the left channel and the partner on the right.</summary>
    Left,
    /// <summary>The learner is on the right channel and the partner on the left.</summary>
    Right
}

internal class TranscriptSegment
{
    public double Start { get; set; }
    public double End { get; set; }
    public string Speaker { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    /// <summary>
    /// Where a second transcription of this moment heard something else, as "this -> that; ...".
    /// </summary>
    public string Disputed { get; set; } = string.Empty;
}

internal class CallChunk
{
    public int Index { get; set; }
    public double Start { get; set; }
    public double End { get; set; }
    public string Text { get; set; } = string.Empty;
    public bool Analyzed { get; set; }
    /// <summary>
    /// Findings the review noted in this chunk (before any were merged or filtered out).
    /// </summary>
    public int IssuesRecorded { get; set; }
    /// <summary>
    /// What this stretch of the conversation was about, so later chunks and the consolidation know the story so far.
    /// </summary>
    public string Summary { get; set; } = string.Empty;
}

/// <summary>
/// One recorded call (or transcript) on its way through transcription, chunked analysis and the final report.
/// Progress is saved after every part and chunk, so a restart resumes where it stopped instead of paying twice.
/// </summary>
internal class CallJob : Savable
{
    public const int MaxAttempts = 3;
    public const string DefaultLearnerLabel = "Me";
    public const string DefaultPartnerLabel = "Partner";

    private string _userId = string.Empty;
    private string _analysisCharacterId = string.Empty;
    private string _deliveryCharacterId = string.Empty;
    private string _title = string.Empty;
    private string _partner = string.Empty;
    private ExpectedRegister _register = ExpectedRegister.Casual;
    private LearnerChannel _learnerChannel = LearnerChannel.Mixed;
    private string _learnerSpeaker = string.Empty;
    private string _sourcePath = string.Empty;
    private bool _isTranscript;
    private CallJobStatus _status = CallJobStatus.Queued;
    private int _transcribedParts;
    private int _attempts;
    private string _report = string.Empty;
    private string _error = string.Empty;
    private DateTime _createdAt = DateTime.UtcNow;
    private DateTime _updatedAt = DateTime.UtcNow;
    private DateTime? _recordedAt;
    private bool _delivered;

    [BsonRepresentation(MongoDB.Bson.BsonType.ObjectId)]
    public string UserId
    {
        get => _userId;
        set => SetProperty(ref _userId, value);
    }
    /// <summary>
    /// The character whose tools and voice analyze the chunks (the Japanese tutor).
    /// </summary>
    public string AnalysisCharacterId
    {
        get => _analysisCharacterId;
        set => SetProperty(ref _analysisCharacterId, value);
    }
    /// <summary>
    /// The character that tells the user about the result (the companion persona).
    /// </summary>
    public string DeliveryCharacterId
    {
        get => _deliveryCharacterId;
        set => SetProperty(ref _deliveryCharacterId, value);
    }
    public string Title
    {
        get => _title;
        set => SetProperty(ref _title, value);
    }
    public string Partner
    {
        get => _partner;
        set => SetProperty(ref _partner, value);
    }
    public ExpectedRegister Register
    {
        get => _register;
        set => SetProperty(ref _register, value);
    }
    public LearnerChannel LearnerChannel
    {
        get => _learnerChannel;
        set => SetProperty(ref _learnerChannel, value);
    }
    /// <summary>
    /// Speaker label of the learner in a labeled transcript ("Me", "A", ...).
    /// </summary>
    public string LearnerSpeaker
    {
        get => _learnerSpeaker;
        set => SetProperty(ref _learnerSpeaker, value);
    }
    public string SourcePath
    {
        get => _sourcePath;
        set => SetProperty(ref _sourcePath, value);
    }
    public bool IsTranscript
    {
        get => _isTranscript;
        set => SetProperty(ref _isTranscript, value);
    }
    public CallJobStatus Status
    {
        get => _status;
        set => SetProperty(ref _status, value);
    }
    public int TranscribedParts
    {
        get => _transcribedParts;
        set => SetProperty(ref _transcribedParts, value);
    }
    public int Attempts
    {
        get => _attempts;
        set => SetProperty(ref _attempts, value);
    }
    public string Report
    {
        get => _report;
        set => SetProperty(ref _report, value);
    }
    public string Error
    {
        get => _error;
        set => SetProperty(ref _error, value);
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
    public DateTime? RecordedAt
    {
        get => _recordedAt;
        set => SetProperty(ref _recordedAt, value);
    }
    public bool Delivered
    {
        get => _delivered;
        set => SetProperty(ref _delivered, value);
    }

    public List<TranscriptSegment> Segments { get; set; } = [];
    public List<CallChunk> Chunks { get; set; } = [];
    /// <summary>
    /// Everything the analysis found in this call, kept here in full. The diagnosis only keeps a few recent examples per
    /// issue, so this is the call's own record.
    /// </summary>
    public List<CallFinding> Findings { get; set; } = [];
    public CallReview? Review { get; set; }
    public CallCoaching? Coaching { get; set; }
    /// <summary>
    /// The facts pass ran; it may have found nothing.
    /// </summary>
    public bool FactsChecked { get; set; }
    public CallStats? Stats { get; set; }
    /// <summary>
    /// Findings of a call analyzed before they were stored on the call were copied over from the diagnosis.
    /// </summary>
    public bool LegacyImported { get; set; }

    public bool IsFinished => Status is CallJobStatus.Completed or CallJobStatus.Failed;

    public bool HasLearnerSeparation => LearnerChannel != LearnerChannel.Mixed || !string.IsNullOrWhiteSpace(LearnerSpeaker);

    public string PartnerLabel => string.IsNullOrWhiteSpace(Partner) ? DefaultPartnerLabel : Partner;

    /// <summary>
    /// The transcript holds the learner's and the partner's channels, with every line labeled from its channel.
    /// </summary>
    public bool HasBothVoices => LearnerChannel != LearnerChannel.Mixed && Segments.Any(s => s.Speaker == PartnerLabel);

    public double DurationSeconds => Segments.Count == 0 ? 0 : Segments.Max(s => s.End);

    public string ShortId => Id is { Length: > 6 } id ? id[^6..] : Id ?? string.Empty;

    /// <summary>
    /// Label used as the source of diagnosis evidence, e.g. "call with Yuki 2026-10-27".
    /// </summary>
    public string SourceLabel
    {
        get
        {
            string date = (RecordedAt ?? CreatedAt).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            return string.IsNullOrWhiteSpace(Partner) ? $"call {date}" : $"call with {Partner} {date}";
        }
    }

    /// <summary>
    /// The learner's own words: the learner channel was transcribed alone, or the labeled learner lines.
    /// Without separation it is the whole transcript.
    /// </summary>
    public string LearnerText()
    {
        IEnumerable<TranscriptSegment> segments = string.IsNullOrWhiteSpace(LearnerSpeaker)
            ? Segments
            : Segments.Where(s => string.Equals(s.Speaker, LearnerSpeaker, StringComparison.OrdinalIgnoreCase));
        return string.Join(Environment.NewLine, segments.Select(s => s.Text));
    }

    /// <summary>
    /// Whether a line is the learner's: the labeled learner, or every line when only the learner's channel was transcribed
    /// or nothing tells the voices apart.
    /// </summary>
    public bool IsLearnerLine(TranscriptSegment segment) =>
        string.IsNullOrWhiteSpace(LearnerSpeaker)
        || string.Equals(segment.Speaker, LearnerSpeaker, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Mistakes that count: kept by the review and not taken back by the user.
    /// </summary>
    public IEnumerable<CallFinding> KeptMistakes => Findings.Where(f => f.Kind == FindingKind.Mistake && f.Counts);

    public int IssueCount => Findings.Count > 0 ? KeptMistakes.Count() : Chunks.Sum(c => c.IssuesRecorded);

    public void Touch(DateTime nowUtc)
    {
        UpdatedAt = nowUtc;
    }
}

internal interface ICallJobDatabase : IDatabase<CallJob>
{
    public Task<List<CallJob>> GetForUser(string userId);

    /// <summary>
    /// The oldest job that still has work to do.
    /// </summary>
    public Task<CallJob?> GetNextPending();
}

internal class CallJobDatabase : Database<CallJob>, ICallJobDatabase
{
    public override string CollectionName => "call_jobs";

    public CallJobDatabase(IOptionsMonitor<DatabaseOptions> options) : base(options)
    {
    }

    public override bool CanSave(Savable savable) => savable is CallJob;

    public override Task SaveAsync(Savable savable) => SaveDocumentAsync((CallJob)savable);

    public Task<List<CallJob>> GetForUser(string userId) =>
        GetDocumentsByPredicateAsync(Builders<CallJob>.Filter.Eq(j => j.UserId, userId));

    public async Task<CallJob?> GetNextPending()
    {
        FilterDefinition<CallJob> filter = Builders<CallJob>.Filter.Nin(j => j.Status, [CallJobStatus.Completed, CallJobStatus.Failed]);
        List<CallJob> pending = await GetDocumentsByPredicateAsync(filter);
        return pending.OrderBy(j => j.CreatedAt).FirstOrDefault();
    }
}
