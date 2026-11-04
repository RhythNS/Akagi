using Akagi.Characters;
using Akagi.Characters.Conversations;
using Akagi.Users;
using MongoDB.Bson;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Akagi.Life.Japanese.Calls;

/// <summary>
/// What the user said about a recording: who it was with, which politeness fits, and how to find their own voice.
/// </summary>
internal record CallRequest(string Partner, ExpectedRegister Register, LearnerChannel Channel, string LearnerSpeaker)
{
    /// <summary>
    /// Reads words such as "Yuki polite left" or "speaker=Me". Unknown words form the partner's name.
    /// </summary>
    public static CallRequest Parse(IEnumerable<string> tokens)
    {
        List<string> partner = [];
        ExpectedRegister register = ExpectedRegister.Casual;
        LearnerChannel channel = LearnerChannel.Mixed;
        string speaker = string.Empty;

        foreach (string raw in tokens)
        {
            string token = raw.Trim().Trim('[', ']', '(', ')', ',');
            if (token.Length == 0)
            {
                continue;
            }
            string lower = token.ToLowerInvariant();
            if (lower.StartsWith("speaker=", StringComparison.Ordinal) || lower.StartsWith("me=", StringComparison.Ordinal))
            {
                speaker = token[(token.IndexOf('=') + 1)..];
            }
            else if (lower is "left" or "right")
            {
                channel = lower == "left" ? LearnerChannel.Left : LearnerChannel.Right;
            }
            else if (lower is "casual" or "polite" or "formal" or "keigo")
            {
                register = SpeechAnalysis.ParseRegister(lower)!.Value;
            }
            else
            {
                partner.Add(token);
            }
        }
        return new CallRequest(string.Join(' ', partner), register, channel, speaker);
    }
}

internal static partial class CallJobs
{
    public static readonly string[] AudioExtensions = [".m4a", ".mp3", ".wav", ".ogg", ".oga", ".opus", ".webm", ".mp4", ".aac", ".flac", ".amr", ".3gp"];

    public static bool IsSupported(string fileName) =>
        AudioExtensions.Contains(Path.GetExtension(fileName), StringComparer.OrdinalIgnoreCase) || TranscriptParser.IsTranscriptFile(fileName);

    /// <summary>
    /// Inbox file names carry the request: "2026-10-27 Yuki [polite] [left].m4a". A leading date becomes the recording date.
    /// </summary>
    public static (CallRequest Request, DateTime? RecordedAt) ParseFileName(string fileName)
    {
        string name = Path.GetFileNameWithoutExtension(fileName);
        DateTime? recordedAt = null;
        Match date = LeadingDate().Match(name);
        if (date.Success && DateTime.TryParseExact(date.Groups["date"].Value.Replace('_', '-'), "yyyy-MM-dd", CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTime parsed))
        {
            recordedAt = parsed;
            name = name[date.Length..];
        }
        return (CallRequest.Parse(name.Split([' ', '_'], StringSplitOptions.RemoveEmptyEntries)), recordedAt);
    }

    /// <summary>
    /// Copies the recording into its storage folder and creates the job. The job is saved last: once saved, the analysis
    /// service may pick it up at any moment, and it needs the finished file.
    /// </summary>
    public static async Task<CallJob> CreateAsync(ICallJobDatabase jobs, CallAnalysisOptions options, string userId, Stream content,
        string fileName, CallRequest request, DateTime? recordedAt, (string Analysis, string Delivery) characters, CancellationToken cancellationToken = default)
    {
        CallJob job = new()
        {
            Id = ObjectId.GenerateNewId().ToString(),
            UserId = userId,
            Title = Path.GetFileNameWithoutExtension(fileName),
            Partner = request.Partner,
            Register = request.Register,
            LearnerChannel = request.Channel,
            LearnerSpeaker = request.LearnerSpeaker,
            IsTranscript = TranscriptParser.IsTranscriptFile(fileName),
            RecordedAt = recordedAt,
            AnalysisCharacterId = characters.Analysis,
            DeliveryCharacterId = characters.Delivery,
            CreatedAt = DateTime.UtcNow
        };

        string folder = Path.Combine(options.StorageDirectory, job.Id!);
        Directory.CreateDirectory(folder);
        job.SourcePath = Path.Combine(folder, Path.GetFileName(fileName));
        await using (FileStream file = File.Create(job.SourcePath))
        {
            await content.CopyToAsync(file, cancellationToken);
        }
        await jobs.SaveDocumentAsync(job);
        return job;
    }

    /// <summary>
    /// The tutor analyzes and the companion delivers, found by name among the user's characters.
    /// Falls back to the given character, e.g. the one the user is talking to.
    /// </summary>
    public static async Task<(string Analysis, string Delivery)> ResolveCharactersAsync(ICharacterDatabase characters, User user,
        CallAnalysisOptions options, Character? fallback)
    {
        List<Character> owned = await characters.GetCharactersForUser(user);
        Character? analysis = Newest(owned, options.AnalysisCharacterName);
        Character? delivery = Newest(owned, options.DeliveryCharacterName);
        analysis ??= fallback ?? delivery;
        delivery ??= fallback ?? analysis;
        return (analysis?.Id ?? string.Empty, delivery?.Id ?? string.Empty);
    }

    /// <summary>
    /// Uploading a graph again adds characters of the same name, and the latest one is the one in use. Object ids sort by
    /// creation time.
    /// </summary>
    private static Character? Newest(IEnumerable<Character> characters, string name) =>
        characters
            .Where(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(c => c.Id, StringComparer.Ordinal)
            .FirstOrDefault();

    /// <summary>
    /// Gives a failed job its attempts back. It resumes at the step it stopped in; finished parts, passes and chunks are
    /// not paid for again.
    /// </summary>
    public static bool Requeue(CallJob job)
    {
        if (job.Status != CallJobStatus.Failed)
        {
            return false;
        }
        job.Attempts = 0;
        job.Error = string.Empty;
        job.Status = job.Chunks.Count == 0 ? CallJobStatus.Transcribing
            : !job.Chunks.All(c => c.Analyzed) ? CallJobStatus.Analyzing
            : job.Review == null ? CallJobStatus.Consolidating
            : CallJobStatus.Reporting;
        return true;
    }

    /// <summary>
    /// Reviews a finished call again from its stored transcript: takes everything the earlier review put into the
    /// diagnosis back out, then queues the review, consolidation and report. Transcription is not repeated, but the
    /// review requests are paid again.
    /// </summary>
    public static bool Reanalyze(CallJob job, JapaneseDiagnosis? diagnosis)
    {
        if (job.Status is not (CallJobStatus.Completed or CallJobStatus.Failed) || job.Chunks.Count == 0)
        {
            return false;
        }
        if (diagnosis != null)
        {
            CallDiagnosis.ImportLegacy(job, diagnosis);
            foreach (CallFinding finding in job.Findings)
            {
                CallDiagnosis.Undo(diagnosis, job, finding);
            }
        }
        job.Findings = [];
        job.Review = null;
        job.Stats = null;
        job.Report = string.Empty;
        job.Delivered = false;
        job.LegacyImported = false;
        foreach (CallChunk chunk in job.Chunks)
        {
            chunk.Analyzed = false;
            chunk.Summary = string.Empty;
            chunk.IssuesRecorded = 0;
        }
        job.Attempts = 0;
        job.Error = string.Empty;
        job.Status = CallJobStatus.Analyzing;
        return true;
    }

    /// <summary>
    /// The system message that asks the companion to share the result in her own voice.
    /// </summary>
    public static string BuildDeliveryInstruction(CallJob job)
    {
        StringBuilder text = new();
        text.Append(TextMessage.SystemMessagePrefix).Append("[Call analysis] ");
        text.AppendLine($"The analysis of the user's Japanese {job.SourceLabel} just finished.");
        text.AppendLine(job.Report);
        text.Append("Tell the user about it in your own voice and keep it short: one honest compliment, the two or three most useful things " +
                    "to work on with a natural example each, and offer to practice them together. Do not paste the report.");
        return text.ToString();
    }

    [GeneratedRegex(@"^(?<date>\d{4}[-_]\d{2}[-_]\d{2})[\s_-]*")]
    private static partial Regex LeadingDate();
}
