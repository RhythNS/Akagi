using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace Akagi.Life.Japanese.Calls;

/// <summary>
/// A piece of a long recording and where it starts in the original.
/// </summary>
internal record AudioPart(string Path, double OffsetSeconds);

internal interface IAudioSplitter
{
    /// <summary>
    /// Cuts a recording into parts of about <paramref name="partLength"/> inside <paramref name="workDirectory"/>.
    /// Calling it again for the same directory reuses the parts already there.
    /// </summary>
    public Task<IReadOnlyList<AudioPart>> SplitAsync(string audioPath, TimeSpan partLength, LearnerChannel channel,
        string workDirectory, CancellationToken cancellationToken);

    /// <summary>
    /// Cuts a stereo recording like <see cref="SplitAsync"/> into parts holding both voices at the same loudness, so the
    /// quieter one is not drowned out.
    /// </summary>
    public Task<IReadOnlyList<AudioPart>> SplitBalancedAsync(string audioPath, TimeSpan partLength, string workDirectory, CancellationToken cancellationToken);

    /// <summary>
    /// The loudness of a part per <see cref="ConversationMerger.LoudnessWindowSeconds"/>, or null without ffmpeg.
    /// </summary>
    public Task<double[]?> MeasureLoudnessAsync(string audioPath, CancellationToken cancellationToken);
}

/// <summary>
/// Uses ffmpeg when it is installed (any format, channel selection, small mono MP3 parts) and falls back to
/// splitting PCM WAV files natively.
/// </summary>
internal class AudioSplitter : IAudioSplitter
{
    private readonly string _ffmpegPath;

    public AudioSplitter(string ffmpegPath)
    {
        _ffmpegPath = ffmpegPath;
    }

    public Task<IReadOnlyList<AudioPart>> SplitAsync(string audioPath, TimeSpan partLength, LearnerChannel channel,
        string workDirectory, CancellationToken cancellationToken)
    {
        List<string> filter = channel == LearnerChannel.Mixed
            ? []
            : ["-af", channel == LearnerChannel.Left ? "pan=mono|c0=c0" : "pan=mono|c0=c1"];
        return SplitAsync(audioPath, partLength, channel, filter, workDirectory, cancellationToken);
    }

    public Task<IReadOnlyList<AudioPart>> SplitBalancedAsync(string audioPath, TimeSpan partLength, string workDirectory, CancellationToken cancellationToken) =>
        SplitAsync(audioPath, partLength, LearnerChannel.Mixed, BalancedMix, workDirectory, cancellationToken);

    private static readonly string[] BalancedMix =
    [
        "-filter_complex",
        "[0:a]channelsplit=channel_layout=stereo[l][r];[l]loudnorm=I=-20:TP=-2:LRA=11[ln];[r]loudnorm=I=-20:TP=-2:LRA=11[rn];[ln][rn]amix=inputs=2:normalize=0[m]",
        "-map",
        "[m]"
    ];

    private async Task<IReadOnlyList<AudioPart>> SplitAsync(string audioPath, TimeSpan partLength, LearnerChannel wavChannel, IReadOnlyList<string> filter,
        string workDirectory, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(workDirectory);
        string marker = Path.Combine(workDirectory, CompleteMarker);
        if (File.Exists(marker))
        {
            return Existing(workDirectory, partLength);
        }
        // Parts of an interrupted split are incomplete
        foreach (string stale in Directory.GetFiles(workDirectory, "part_*.*"))
        {
            File.Delete(stale);
        }

        IReadOnlyList<AudioPart> parts;
        if (await IsFfmpegAvailable(cancellationToken))
        {
            await RunFfmpeg(audioPath, partLength, filter, workDirectory, cancellationToken);
            parts = Existing(workDirectory, partLength);
        }
        else if (WavSplitter.IsWav(audioPath))
        {
            parts = WavSplitter.Split(audioPath, partLength, wavChannel, workDirectory);
        }
        else
        {
            throw new InvalidOperationException($"ffmpeg was not found at '{_ffmpegPath}'. Install it or set CallAnalysis:FfmpegPath; only WAV files work without it.");
        }

        await File.WriteAllTextAsync(marker, parts.Count.ToString(CultureInfo.InvariantCulture), cancellationToken);
        return parts;
    }

    private const string CompleteMarker = "parts.complete";

    private static List<AudioPart> Existing(string workDirectory, TimeSpan partLength) =>
        [.. Directory.GetFiles(workDirectory, "part_*.*")
            .Order(StringComparer.Ordinal)
            .Select((path, index) => new AudioPart(path, index * partLength.TotalSeconds))];

    private async Task<bool> IsFfmpegAvailable(CancellationToken cancellationToken)
    {
        try
        {
            (int exitCode, _) = await Run(["-version"], cancellationToken);
            return exitCode == 0;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    private async Task RunFfmpeg(string audioPath, TimeSpan partLength, IReadOnlyList<string> filter, string workDirectory, CancellationToken cancellationToken)
    {
        List<string> arguments = ["-hide_banner", "-loglevel", "error", "-y", "-i", audioPath, .. filter];
        arguments.AddRange(["-ac", "1", "-ar", "16000", "-c:a", "libmp3lame", "-b:a", "32k",
            "-f", "segment", "-segment_time", ((int)partLength.TotalSeconds).ToString(CultureInfo.InvariantCulture),
            Path.Combine(workDirectory, "part_%04d.mp3")]);

        (int exitCode, string errors) = await Run(arguments, cancellationToken);
        if (exitCode != 0)
        {
            throw new InvalidOperationException($"ffmpeg failed ({exitCode}): {errors.Trim()}");
        }
    }

    public async Task<double[]?> MeasureLoudnessAsync(string audioPath, CancellationToken cancellationToken)
    {
        if (!await IsFfmpegAvailable(cancellationToken))
        {
            return null;
        }

        using Process process = Start(["-hide_banner", "-loglevel", "error", "-i", audioPath, "-ac", "1", "-ar", LoudnessSampleRate.ToString(CultureInfo.InvariantCulture),
            "-f", "s16le", "-"]);
        Task<string> errors = process.StandardError.ReadToEndAsync(cancellationToken);
        using MemoryStream samples = new();
        await process.StandardOutput.BaseStream.CopyToAsync(samples, cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"ffmpeg failed ({process.ExitCode}): {(await errors).Trim()}");
        }
        return Envelope(samples.GetBuffer().AsSpan(0, (int)samples.Length), (int)(LoudnessSampleRate * ConversationMerger.LoudnessWindowSeconds));
    }

    private const int LoudnessSampleRate = 8000;

    /// <summary>
    /// Root mean square of 16 bit little endian samples, per window.
    /// </summary>
    internal static double[] Envelope(ReadOnlySpan<byte> samples, int windowSamples)
    {
        int count = samples.Length / 2;
        double[] envelope = new double[count / windowSamples];
        for (int window = 0; window < envelope.Length; window++)
        {
            double sum = 0;
            for (int i = 0; i < windowSamples; i++)
            {
                int offset = (window * windowSamples + i) * 2;
                short sample = (short)(samples[offset] | (samples[offset + 1] << 8));
                sum += (double)sample * sample;
            }
            envelope[window] = Math.Sqrt(sum / windowSamples);
        }
        return envelope;
    }

    private async Task<(int ExitCode, string Errors)> Run(IEnumerable<string> arguments, CancellationToken cancellationToken)
    {
        using Process process = Start(arguments);
        Task<string> errors = process.StandardError.ReadToEndAsync(cancellationToken);
        Task<string> output = process.StandardOutput.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        await output;
        return (process.ExitCode, await errors);
    }

    private Process Start(IEnumerable<string> arguments)
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

        return Process.Start(info) ?? throw new InvalidOperationException("Could not start ffmpeg.");
    }
}

/// <summary>
/// Splits uncompressed PCM WAV files without external tools. Picking a channel keeps only the learner's side of a
/// stereo call recording.
/// </summary>
internal static class WavSplitter
{
    private record Format(short Channels, int SampleRate, short BitsPerSample, long DataOffset, long DataLength)
    {
        public int BlockAlign => Channels * BitsPerSample / 8;
    }

    public static bool IsWav(string path)
    {
        if (!File.Exists(path))
        {
            return false;
        }
        using FileStream stream = File.OpenRead(path);
        Span<byte> header = stackalloc byte[12];
        return stream.Read(header) == 12 && Encoding.ASCII.GetString(header[..4]) == "RIFF" && Encoding.ASCII.GetString(header[8..12]) == "WAVE";
    }

    public static IReadOnlyList<AudioPart> Split(string path, TimeSpan partLength, LearnerChannel channel, string workDirectory)
    {
        using FileStream input = File.OpenRead(path);
        Format format = ReadFormat(input);
        int sampleBytes = format.BitsPerSample / 8;
        bool pickChannel = channel != LearnerChannel.Mixed && format.Channels == 2;
        short outChannels = pickChannel ? (short)1 : format.Channels;
        int channelIndex = channel == LearnerChannel.Right ? 1 : 0;

        long framesPerPart = (long)(format.SampleRate * partLength.TotalSeconds);
        long totalFrames = format.DataLength / format.BlockAlign;
        List<AudioPart> parts = [];
        byte[] frame = new byte[format.BlockAlign];
        input.Position = format.DataOffset;

        for (long first = 0; first < totalFrames; first += framesPerPart)
        {
            long frames = Math.Min(framesPerPart, totalFrames - first);
            string partPath = Path.Combine(workDirectory, $"part_{parts.Count:D4}.wav");
            using (FileStream output = File.Create(partPath))
            {
                WriteHeader(output, outChannels, format.SampleRate, format.BitsPerSample, frames * outChannels * sampleBytes);
                for (long i = 0; i < frames; i++)
                {
                    input.ReadExactly(frame);
                    if (pickChannel)
                    {
                        output.Write(frame, channelIndex * sampleBytes, sampleBytes);
                    }
                    else
                    {
                        output.Write(frame);
                    }
                }
            }
            parts.Add(new AudioPart(partPath, (double)first / format.SampleRate));
        }
        return parts;
    }

    private static Format ReadFormat(Stream stream)
    {
        using BinaryReader reader = new(stream, Encoding.ASCII, leaveOpen: true);
        stream.Position = 12;
        short channels = 0;
        int sampleRate = 0;
        short bits = 0;
        while (stream.Position + 8 <= stream.Length)
        {
            string id = Encoding.ASCII.GetString(reader.ReadBytes(4));
            uint size = reader.ReadUInt32();
            long body = stream.Position;
            if (id == "fmt ")
            {
                short audioFormat = reader.ReadInt16();
                if (audioFormat != 1)
                {
                    throw new NotSupportedException("Only uncompressed PCM WAV can be split without ffmpeg.");
                }
                channels = reader.ReadInt16();
                sampleRate = reader.ReadInt32();
                reader.ReadInt32();
                reader.ReadInt16();
                bits = reader.ReadInt16();
            }
            else if (id == "data")
            {
                if (channels == 0)
                {
                    throw new InvalidDataException("WAV data chunk before format chunk.");
                }
                long length = Math.Min(size, stream.Length - body);
                return new Format(channels, sampleRate, bits, body, length);
            }
            // Chunks are padded to an even size
            stream.Position = body + size + (size % 2);
        }
        throw new InvalidDataException("WAV file has no data chunk.");
    }

    internal static void WriteHeader(Stream output, short channels, int sampleRate, short bitsPerSample, long dataLength)
    {
        using BinaryWriter writer = new(output, Encoding.ASCII, leaveOpen: true);
        short blockAlign = (short)(channels * bitsPerSample / 8);
        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        writer.Write((uint)(36 + dataLength));
        writer.Write(Encoding.ASCII.GetBytes("WAVE"));
        writer.Write(Encoding.ASCII.GetBytes("fmt "));
        writer.Write(16u);
        writer.Write((short)1);
        writer.Write(channels);
        writer.Write(sampleRate);
        writer.Write(sampleRate * blockAlign);
        writer.Write(blockAlign);
        writer.Write(bitsPerSample);
        writer.Write(Encoding.ASCII.GetBytes("data"));
        writer.Write((uint)dataLength);
    }
}
