using Akagi.Life.Japanese.Calls;
using System.Diagnostics;
using System.Globalization;

namespace Akagi.Life.Japanese.Mining;

/// <summary>
/// Cuts audio for sentence mining with ffmpeg: a short clip of one line, or a whole call's track.
/// </summary>
internal interface IAudioClipper
{
    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken);

    public Task ClipAsync(string source, double start, double end, LearnerChannel learner, MiningVoices voices, string output, CancellationToken cancellationToken);

    public Task ExtractAsync(string source, LearnerChannel learner, MiningVoices voices, string output, CancellationToken cancellationToken);
}

internal class AudioClipper : IAudioClipper
{
    private readonly string _ffmpegPath;

    public AudioClipper(string ffmpegPath)
    {
        _ffmpegPath = ffmpegPath;
    }

    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken)
    {
        try
        {
            (int exitCode, _) = await RunAsync(["-version"], cancellationToken);
            return exitCode == 0;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    public Task ClipAsync(string source, double start, double end, LearnerChannel learner, MiningVoices voices, string output, CancellationToken cancellationToken) =>
        RunCheckedAsync(ClipArguments(source, start, end, learner, voices, output), cancellationToken);

    public Task ExtractAsync(string source, LearnerChannel learner, MiningVoices voices, string output, CancellationToken cancellationToken) =>
        RunCheckedAsync(TrackArguments(source, learner, voices, output), cancellationToken);

    /// <summary>
    /// Seeks before reading the input, which is fast and, since the clip is encoded again, still exact.
    /// </summary>
    internal static List<string> ClipArguments(string source, double start, double end, LearnerChannel learner, MiningVoices voices, string output) =>
    [
        "-hide_banner", "-loglevel", "error", "-y",
        "-ss", Seconds(start), "-t", Seconds(Math.Max(0.1, end - start)),
        "-i", source,
        .. ChannelFilter(learner, voices),
        "-ac", "1", "-c:a", "libmp3lame", "-b:a", "96k",
        output
    ];

    internal static List<string> TrackArguments(string source, LearnerChannel learner, MiningVoices voices, string output)
    {
        bool partnerOnly = voices != MiningVoices.Both && learner != LearnerChannel.Mixed;
        return
        [
            "-hide_banner", "-loglevel", "error", "-y",
            "-i", source,
            .. ChannelFilter(learner, voices),
            // Both voices stay on their own side, as recorded
            "-ac", partnerOnly ? "1" : "2", "-c:a", "libmp3lame", "-b:a", partnerOnly ? "48k" : "64k",
            output
        ];
    }

    /// <summary>
    /// The partner is on the other channel than the learner. Mixed recordings have nothing to pick.
    /// </summary>
    internal static string[] ChannelFilter(LearnerChannel learner, MiningVoices voices)
    {
        if (learner == LearnerChannel.Mixed || voices == MiningVoices.Both)
        {
            return [];
        }
        bool left = (learner == LearnerChannel.Left) == (voices == MiningVoices.Learner);
        return ["-af", left ? "pan=mono|c0=c0" : "pan=mono|c0=c1"];
    }

    private static string Seconds(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    private async Task RunCheckedAsync(IEnumerable<string> arguments, CancellationToken cancellationToken)
    {
        (int exitCode, string errors) = await RunAsync(arguments, cancellationToken);
        if (exitCode != 0)
        {
            throw new InvalidOperationException($"ffmpeg failed ({exitCode}): {errors.Trim()}");
        }
    }

    private async Task<(int ExitCode, string Errors)> RunAsync(IEnumerable<string> arguments, CancellationToken cancellationToken)
    {
        ProcessStartInfo info = new(_ffmpegPath)
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (string argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }
        using Process process = Process.Start(info) ?? throw new InvalidOperationException("Could not start ffmpeg.");
        Task<string> errors = process.StandardError.ReadToEndAsync(cancellationToken);
        Task<string> output = process.StandardOutput.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        await output;
        return (process.ExitCode, await errors);
    }
}
