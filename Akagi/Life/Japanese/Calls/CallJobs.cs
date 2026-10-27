using Akagi.Characters;
using Akagi.Characters.Conversations;
using Akagi.Users;
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
    /// Creates a job and copies the recording into its storage folder.
    /// </summary>
    public static async Task<CallJob> CreateAsync(ICallJobDatabase jobs, CallAnalysisOptions options, string userId, Stream content,
        string fileName, CallRequest request, DateTime? recordedAt, (string Analysis, string Delivery) characters, CancellationToken cancellationToken = default)
    {
        CallJob job = new()
        {
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
        await jobs.SaveDocumentAsync(job);

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
        Character? analysis = owned.FirstOrDefault(c => string.Equals(c.Name, options.AnalysisCharacterName, StringComparison.OrdinalIgnoreCase));
        Character? delivery = owned.FirstOrDefault(c => string.Equals(c.Name, options.DeliveryCharacterName, StringComparison.OrdinalIgnoreCase));
        analysis ??= fallback ?? delivery;
        delivery ??= fallback ?? analysis;
        return (analysis?.Id ?? string.Empty, delivery?.Id ?? string.Empty);
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
