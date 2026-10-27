namespace Akagi.Life.Japanese.Calls;

/// <summary>
/// Configuration section "CallAnalysis".
/// </summary>
internal class CallAnalysisOptions
{
    /// <summary>
    /// Folder watched for recordings and transcripts. Files go into a subfolder named after the user's username,
    /// e.g. Inbox/noah/2026-10-27 Yuki.m4a. Empty disables the inbox.
    /// </summary>
    public string InboxDirectory { get; set; } = string.Empty;
    /// <summary>
    /// Where recordings and intermediate audio parts are kept while a job runs.
    /// </summary>
    public string StorageDirectory { get; set; } = Path.Combine(AppContext.BaseDirectory, "calls");
    public string FfmpegPath { get; set; } = "ffmpeg";
    /// <summary>
    /// Length of the audio parts sent to transcription; 10 minutes of 32 kbit/s mono stays far below upload limits.
    /// </summary>
    public int PartMinutes { get; set; } = 10;
    /// <summary>
    /// Transcript characters per analysis call.
    /// </summary>
    public int ChunkCharacters { get; set; } = 2500;
    /// <summary>
    /// OpenAI compatible speech-to-text endpoint; a local faster-whisper server works the same way.
    /// </summary>
    public string TranscriptionBaseUrl { get; set; } = "https://api.openai.com/v1/";
    public string TranscriptionApiKey { get; set; } = string.Empty;
    public string TranscriptionModel { get; set; } = "whisper-1";
    public string Language { get; set; } = "ja";
    /// <summary>
    /// Names of the user's characters that analyze calls (the tutor) and tell the user about them (the companion).
    /// </summary>
    public string AnalysisCharacterName { get; set; } = "Sensei";
    public string DeliveryCharacterName { get; set; } = "Akagi";
    /// <summary>
    /// Seconds between checks for new inbox files and pending jobs.
    /// </summary>
    public int PollSeconds { get; set; } = 30;
}
