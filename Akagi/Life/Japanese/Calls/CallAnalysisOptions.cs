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
    /// Length of the audio parts sent to transcription. 32 kbit/s mono stays far below the 25 MB upload limit; the
    /// tighter bound is the 60 seconds the providers behind OpenRouter get per request.
    /// </summary>
    public int PartMinutes { get; set; } = 5;
    /// <summary>
    /// Transcript characters per analysis call.
    /// </summary>
    public int ChunkCharacters { get; set; } = 2500;
    /// <summary>
    /// OpenRouter speech-to-text model; requests use the credentials of the "OpenRouter" section.
    /// </summary>
    public string TranscriptionModel { get; set; } = "microsoft/mai-transcribe-2";
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
