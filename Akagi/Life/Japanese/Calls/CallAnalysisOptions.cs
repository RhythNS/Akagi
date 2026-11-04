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
    /// Length of the audio parts sent to transcription. 32 kbit/s mono stays far below the 25 MB upload limit, and
    /// MAI-Transcribe needs about 5 of the 60 seconds providers get per request for 10 minutes of audio.
    /// </summary>
    public int PartMinutes { get; set; } = 10;
    /// <summary>
    /// Transcript characters per review request, in the compact layout of turns (about ten minutes of conversation).
    /// Fewer, larger parts repeat the instructions less often.
    /// </summary>
    public int ChunkCharacters { get; set; } = 6000;
    /// <summary>
    /// How much the model may think in each pass: none, minimal, low, medium or high; empty leaves it to the model.
    /// Thinking is billed as output, the most expensive part of a review.
    /// </summary>
    public string ReviewReasoning { get; set; } = "low";
    public string CoachingReasoning { get; set; } = "low";
    public string FactsReasoning { get; set; } = "low";
    public string ConsolidationReasoning { get; set; } = "medium";
    /// <summary>
    /// US dollars per hour of audio, to estimate a transcription's cost when the provider does not report it.
    /// </summary>
    public double TranscriptionPricePerHour { get; set; } = 0.10;
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
