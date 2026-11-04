using Akagi.Characters.Companions;
using Akagi.Characters.Memories;
using Akagi.Life.Japanese;
using Akagi.Tests.Characters;
using Microsoft.Extensions.Logging;
using Akagi.Life.Japanese.Calls;
using Akagi.LLMs.OpenRouter;
using Akagi.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using System.Net;
using System.Text;

namespace Akagi.Tests.Life;

internal class InMemoryCallJobDatabase : InMemoryDatabase<CallJob>, ICallJobDatabase
{
    public InMemoryCallJobDatabase() : base("call_jobs")
    {
    }

    public Task<List<CallJob>> GetForUser(string userId) => Task.FromResult(Items.Where(j => j.UserId == userId).ToList());

    public Task<CallJob?> GetNextPending() => Task.FromResult(Items.Where(j => !j.IsFinished).OrderBy(j => j.CreatedAt).FirstOrDefault());
}

public class TranscriptTests
{
    [Fact]
    public void Parse_ReadsSrtWithSpeakers()
    {
        string srt = "1\n00:00:01,000 --> 00:00:03,500\nYuki: 最近どう？\n\n2\n00:00:04,000 --> 00:00:07,250\nMe: えーと、仕事が忙しいです。\n";

        List<TranscriptSegment> segments = TranscriptParser.Parse(srt, "call.srt");

        Assert.Equal(2, segments.Count);
        Assert.Equal("Yuki", segments[0].Speaker);
        Assert.Equal("最近どう？", segments[0].Text);
        Assert.Equal(4.0, segments[1].Start);
        Assert.Equal(7.25, segments[1].End);
        Assert.Equal("Me", segments[1].Speaker);
    }

    [Fact]
    public void Parse_ReadsVttAndStripsTags()
    {
        string vtt = "WEBVTT\n\n00:01.000 --> 00:02.000\n<v Yuki>そうなんだ</v>\n";

        TranscriptSegment segment = Assert.Single(TranscriptParser.Parse(vtt, "call.vtt"));

        Assert.Equal("そうなんだ", segment.Text);
        Assert.Equal(1.0, segment.Start);
    }

    [Fact]
    public void Parse_ReadsPlainTextWithTimestamps()
    {
        string text = "[00:00:05] A: 明日12:30に会おう\n[00:01:10] B: いいよ\n12:30に駅で";

        List<TranscriptSegment> segments = TranscriptParser.Parse(text, "notes.txt");

        Assert.Equal(3, segments.Count);
        Assert.Equal(("A", "明日12:30に会おう"), (segments[0].Speaker, segments[0].Text));
        Assert.Equal(70, segments[0].End);
        Assert.Equal(70, segments[1].Start);
        Assert.Equal(string.Empty, segments[2].Speaker);
    }

    [Fact]
    public void Chunk_KeepsSegmentsWholeAndTimestamped()
    {
        List<TranscriptSegment> segments = [.. Enumerable.Range(0, 10).Select(i => new TranscriptSegment
        {
            Start = i * 10,
            End = i * 10 + 9,
            Speaker = i % 2 == 0 ? "Me" : "Yuki",
            Text = new string('あ', 40)
        })];

        List<CallChunk> chunks = TranscriptChunker.Chunk(segments, 200);

        Assert.True(chunks.Count > 1);
        Assert.All(chunks, c => Assert.True(c.Text.Length <= 200 + 60));
        Assert.Equal(segments.Count, chunks.Sum(c => c.Text.Split('\n').Length));
        Assert.StartsWith("[00:00:00] Me: ", chunks[0].Text);
        Assert.Equal(99, chunks[^1].End);
        Assert.Equal([.. Enumerable.Range(0, chunks.Count)], chunks.Select(c => c.Index));
    }

    [Fact]
    public void Transcriber_ParsesVerboseAndPlainResponses()
    {
        List<TranscriptSegment> verbose = OpenRouterTranscriber.ParseResponse(
            """{"text":"ignored","segments":[{"start":0.5,"end":2.0,"text":" こんにちは "},{"start":2.0,"end":3.0,"text":""}]}""");
        List<TranscriptSegment> plain = OpenRouterTranscriber.ParseResponse("""{"text":"元気？","duration":4.5}""");
        List<TranscriptSegment> billed = OpenRouterTranscriber.ParseResponse("""{"text":"元気？","usage":{"seconds":9.2,"cost":0.0003}}""");

        TranscriptSegment segment = Assert.Single(verbose);
        Assert.Equal("こんにちは", segment.Text);
        Assert.Equal(0.5, segment.Start);
        Assert.Equal(4.5, Assert.Single(plain).End);
        Assert.Equal(9.2, Assert.Single(billed).End);
    }

    [Fact]
    public async Task Transcriber_PostsMultipartToOpenRouterWithItsCredentials()
    {
        using TempDirectory temp = new();
        string audio = Path.Combine(temp.Path, "part_0000.mp3");
        await File.WriteAllBytesAsync(audio, [1, 2, 3]);
        RecordingHandler handler = new("""{"segments":[{"start":0,"end":1,"text":"はい"}]}""");
        OpenRouterTranscriber transcriber = new(new HttpClient(handler), Options(new CallAnalysisOptions()), OpenRouter("key", "https://openrouter.ai/api/v1/"));

        List<TranscriptSegment> segments = await transcriber.TranscribeAsync(audio, CancellationToken.None);

        Assert.Equal("はい", Assert.Single(segments).Text);
        Assert.True(transcriber.IsConfigured);
        Assert.Equal("https://openrouter.ai/api/v1/audio/transcriptions", handler.Request!.RequestUri!.ToString());
        Assert.Equal("Bearer key", handler.Request.Headers.Authorization!.ToString());
        Assert.Contains("microsoft/mai-transcribe-2", handler.Body);
        Assert.Contains("name=\"language\"", handler.Body);
        Assert.Contains("name=\"file\"; filename=\"part_0000.mp3\"", handler.Body);
        Assert.DoesNotContain("filename*", handler.Body);
        Assert.DoesNotContain("\"", handler.Request.Content!.Headers.ContentType!.ToString());
        Assert.Contains("verbose_json", handler.Body);
    }

    [Theory]
    [InlineData("", "https://openrouter.ai/api/v1/")]
    [InlineData("key", "")]
    public void Transcriber_NeedsBothOpenRouterCredentials(string apiKey, string baseUrl)
    {
        OpenRouterTranscriber transcriber = new(new HttpClient(), Options(new CallAnalysisOptions()), OpenRouter(apiKey, baseUrl));

        Assert.False(transcriber.IsConfigured);
    }

    [Fact]
    public async Task Transcriber_AsksForWordTimestampsOnlyWhenMerging()
    {
        using TempDirectory temp = new();
        string audio = Path.Combine(temp.Path, "part_0000.mp3");
        await File.WriteAllBytesAsync(audio, [1]);
        RecordingHandler handler = new("""{"text":"は","words":[{"word":"は","start":0.5,"end":0.6}]}""");
        OpenRouterTranscriber transcriber = new(new HttpClient(handler), Options(new CallAnalysisOptions()), OpenRouter("key", "https://openrouter.ai/api/v1/"));

        TranscriptWord word = Assert.Single(await transcriber.TranscribeWordsAsync(audio, CancellationToken.None));

        Assert.Equal(new TranscriptWord("は", 0.5, 0.6), word);
        Assert.Contains("name=\"timestamp_granularities[]\"", handler.Body);
    }

    [Fact]
    public void Transcriber_UsesSegmentsAsWordsWhenAModelHasNoWordTimestamps()
    {
        TranscriptWord word = Assert.Single(OpenRouterTranscriber.ParseWords("""{"segments":[{"start":1,"end":2,"text":"はい"}]}"""));

        Assert.Equal(new TranscriptWord("はい", 1, 2), word);
    }

    [Fact]
    public async Task Transcriber_AcceptsABaseUrlWithoutTrailingSlash()
    {
        using TempDirectory temp = new();
        string audio = Path.Combine(temp.Path, "part_0000.mp3");
        await File.WriteAllBytesAsync(audio, [1]);
        RecordingHandler handler = new("""{"text":"はい"}""");
        OpenRouterTranscriber transcriber = new(new HttpClient(handler), Options(new CallAnalysisOptions()), OpenRouter("key", "https://openrouter.ai/api/v1"));

        await transcriber.TranscribeAsync(audio, CancellationToken.None);

        Assert.Equal("https://openrouter.ai/api/v1/audio/transcriptions", handler.Request!.RequestUri!.ToString());
    }

    private sealed class SequenceHandler(params Func<HttpResponseMessage>[] responses) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(responses[Math.Min(Calls++, responses.Length - 1)]());
    }

    private static HttpResponseMessage Throttled(int? retryAfterSeconds = null)
    {
        HttpResponseMessage response = new(HttpStatusCode.TooManyRequests)
        {
            Content = new StringContent("""{"error":{"message":"Provider returned 429","code":429}}""")
        };
        if (retryAfterSeconds is int seconds)
        {
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(seconds));
        }
        return response;
    }

    private static async Task<(OpenRouterTranscriber Transcriber, List<TimeSpan> Waits, string Audio, TempDirectory Temp)> RetryingTranscriber(SequenceHandler handler)
    {
        TempDirectory temp = new();
        string audio = Path.Combine(temp.Path, "part_0000.mp3");
        await File.WriteAllBytesAsync(audio, [1]);
        List<TimeSpan> waits = [];
        OpenRouterTranscriber transcriber = new(new HttpClient(handler), Options(new CallAnalysisOptions()), OpenRouter("key", "https://openrouter.ai/api/v1"),
            (wait, _) => { waits.Add(wait); return Task.CompletedTask; });
        return (transcriber, waits, audio, temp);
    }

    [Fact]
    public async Task Transcriber_WaitsAndTriesAgainWhenThrottled()
    {
        SequenceHandler handler = new(() => Throttled(1), () => Throttled(10),
            () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"words":[{"word":"は","start":0,"end":0.1}]}""") });
        (OpenRouterTranscriber transcriber, List<TimeSpan> waits, string audio, TempDirectory temp) = await RetryingTranscriber(handler);
        using TempDirectory cleanup = temp;

        List<TranscriptWord> words = await transcriber.TranscribeWordsAsync(audio, CancellationToken.None);

        Assert.Single(words);
        Assert.Equal(3, handler.Calls);
        // Never less than the backoff; longer when the provider asks for more
        Assert.Equal([TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(10)], waits);
    }

    [Fact]
    public async Task Transcriber_GivesUpAfterMaxTriesAndDoesNotRetryClientErrors()
    {
        SequenceHandler throttled = new(() => Throttled());
        (OpenRouterTranscriber transcriber, List<TimeSpan> waits, string audio, TempDirectory temp) = await RetryingTranscriber(throttled);
        using TempDirectory cleanup = temp;

        HttpRequestException error = await Assert.ThrowsAsync<HttpRequestException>(() => transcriber.TranscribeWordsAsync(audio, CancellationToken.None));
        Assert.Equal(HttpStatusCode.TooManyRequests, error.StatusCode);
        Assert.Equal(OpenRouterTranscriber.MaxTries, throttled.Calls);
        Assert.All(waits, w => Assert.True(w <= OpenRouterTranscriber.MaxWait));

        SequenceHandler refused = new(() => new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent("bad") });
        (OpenRouterTranscriber once, _, string audio2, TempDirectory temp2) = await RetryingTranscriber(refused);
        using TempDirectory cleanup2 = temp2;
        await Assert.ThrowsAsync<HttpRequestException>(() => once.TranscribeWordsAsync(audio2, CancellationToken.None));
        Assert.Equal(1, refused.Calls);
    }

    internal static IOptionsMonitor<CallAnalysisOptions> Options(CallAnalysisOptions value) => new StaticOptions<CallAnalysisOptions>(value);

    private static IOptionsMonitor<OpenRouterClient.Options> OpenRouter(string apiKey, string baseUrl) =>
        new StaticOptions<OpenRouterClient.Options>(new OpenRouterClient.Options { ApiKey = apiKey, BaseUrl = baseUrl, ResponsePath = "chat/completions" });

    private sealed class StaticOptions<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue => value;
        public T Get(string? name) => value;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}

internal sealed class RecordingHandler(string response, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
{
    public HttpRequestMessage? Request { get; private set; }
    public string Body { get; private set; } = string.Empty;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Request = request;
        Body = request.Content == null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        return new HttpResponseMessage(status) { Content = new StringContent(response, Encoding.UTF8, "application/json") };
    }
}

public class WavSplitterTests
{
    [Fact]
    public void Split_CutsPartsAndKeepsTheLearnerChannel()
    {
        using TempDirectory temp = new();
        string path = Path.Combine(temp.Path, "call.wav");
        const int sampleRate = 8000;
        WriteStereoWav(path, sampleRate, frames: (int)(sampleRate * 2.5), left: 1, right: 2);

        Assert.True(WavSplitter.IsWav(path));
        IReadOnlyList<AudioPart> parts = WavSplitter.Split(path, TimeSpan.FromSeconds(1), LearnerChannel.Right, temp.Path);

        Assert.Equal([0.0, 1.0, 2.0], parts.Select(p => p.OffsetSeconds));
        byte[] first = File.ReadAllBytes(parts[0].Path);
        Assert.Equal(44 + sampleRate * 2, first.Length);
        Assert.Equal(1, BitConverter.ToInt16(first, 22));
        Assert.Equal(2, BitConverter.ToInt16(first, 44));
        Assert.Equal(44 + sampleRate, File.ReadAllBytes(parts[2].Path).Length);
    }

    [Fact]
    public void Split_MixedKeepsBothChannels()
    {
        using TempDirectory temp = new();
        string path = Path.Combine(temp.Path, "call.wav");
        WriteStereoWav(path, 8000, frames: 8000, left: 5, right: 6);

        AudioPart part = Assert.Single(WavSplitter.Split(path, TimeSpan.FromSeconds(1), LearnerChannel.Mixed, temp.Path));

        byte[] bytes = File.ReadAllBytes(part.Path);
        Assert.Equal(2, BitConverter.ToInt16(bytes, 22));
        Assert.Equal(5, BitConverter.ToInt16(bytes, 44));
        Assert.Equal(6, BitConverter.ToInt16(bytes, 46));
    }

    [Fact]
    public async Task AudioSplitter_FallsBackToWavWithoutFfmpegAndReusesParts()
    {
        using TempDirectory temp = new();
        string path = Path.Combine(temp.Path, "call.wav");
        WriteStereoWav(path, 8000, frames: 16000, left: 1, right: 2);
        AudioSplitter splitter = new(Path.Combine(temp.Path, "no-ffmpeg-here.exe"));
        string work = Path.Combine(temp.Path, "parts");

        IReadOnlyList<AudioPart> parts = await splitter.SplitAsync(path, TimeSpan.FromSeconds(1), LearnerChannel.Left, work, CancellationToken.None);
        File.Delete(path);
        IReadOnlyList<AudioPart> again = await splitter.SplitAsync(path, TimeSpan.FromSeconds(1), LearnerChannel.Left, work, CancellationToken.None);

        Assert.Equal(2, parts.Count);
        Assert.Equal(parts.Select(p => p.Path), again.Select(p => p.Path));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            splitter.SplitAsync(Path.Combine(temp.Path, "x.m4a"), TimeSpan.FromSeconds(1), LearnerChannel.Mixed, Path.Combine(temp.Path, "other"), CancellationToken.None));
    }

    private static void WriteStereoWav(string path, int sampleRate, int frames, short left, short right)
    {
        using FileStream stream = File.Create(path);
        WavSplitter.WriteHeader(stream, 2, sampleRate, 16, frames * 4L);
        using BinaryWriter writer = new(stream);
        for (int i = 0; i < frames; i++)
        {
            writer.Write(left);
            writer.Write(right);
        }
    }
}

public class CallJobTests
{
    [Theory]
    [InlineData("2026-10-27 Yuki [polite] [left].m4a", "Yuki", "Polite", "Left")]
    [InlineData("Takeshi and Mai.mp3", "Takeshi and Mai", "Casual", "Mixed")]
    [InlineData("2026_10_01_right_formal.wav", "", "Formal", "Right")]
    public void ParseFileName_ReadsPartnerRegisterAndChannel(string file, string partner, string register, string channel)
    {
        (CallRequest request, DateTime? recordedAt) = CallJobs.ParseFileName(file);

        Assert.Equal(partner, request.Partner);
        Assert.Equal(register, request.Register.ToString());
        Assert.Equal(channel, request.Channel.ToString());
        Assert.Equal(file.StartsWith("2026", StringComparison.Ordinal), recordedAt.HasValue);
    }

    [Fact]
    public void Parse_ReadsSpeakerLabel()
    {
        Assert.Equal("Me", CallRequest.Parse(["Yuki", "speaker=Me"]).LearnerSpeaker);
    }

    [Theory]
    [InlineData("20261021output.mp3", "output")]
    [InlineData("Rii 2026_10_21 [left].m4a", "Rii")]
    [InlineData("Rii left 2026.10.21.m4a", "Rii")]
    [InlineData("call-20261021-Rii.mp3", "call--Rii")]
    public void ParseFileName_FindsADateAnywhere(string file, string partner)
    {
        (CallRequest request, DateTime? recordedAt) = CallJobs.ParseFileName(file);

        Assert.Equal(new DateTime(2026, 10, 21, 0, 0, 0, DateTimeKind.Utc), recordedAt);
        Assert.Equal(partner, request.Partner);
    }

    [Theory]
    [InlineData("date=20261021")]
    [InlineData("2026-10-21")]
    public void Parse_ReadsADateFromTheCommand(string token)
    {
        CallRequest request = CallRequest.Parse(["Rii", "left", token]);

        Assert.Equal(("Rii", new DateTime(2026, 10, 21, 0, 0, 0, DateTimeKind.Utc)), (request.Partner, request.RecordedAt));
    }

    [Theory]
    [InlineData("20261321")]
    [InlineData("19991021")]
    [InlineData("2026")]
    [InlineData("12345678")]
    public void TryParseDate_RefusesWhatIsNoDate(string text)
    {
        Assert.Null(CallJobs.TryParseDate(text));
    }

    [Fact]
    public void RecordingDate_IsReadFromFfmpegsDescription()
    {
        string phone = "Input #0, mov,mp4,m4a,3gp,3g2,mj2, from 'call.m4a':\n  Metadata:\n    major_brand     : M4A\n    creation_time   : 2026-10-21T19:03:12.000000Z\n  Duration: 01:01:51.32, start: 0.000000";
        string converted = "Input #0, mp3, from '20261021output.mp3':\n  Metadata:\n    encoder         : Lavf63.11.101\n  Duration: 01:01:51.32";

        Assert.Equal(new DateTime(2026, 10, 21), AudioSplitter.ParseRecordingDate(phone)!.Value.Date);
        Assert.Null(AudioSplitter.ParseRecordingDate(converted));
        Assert.Equal(new DateTime(2026, 10, 20), AudioSplitter.ParseRecordingDate("    date            : 2026-10-20\n")!.Value.Date);
    }

    [Fact]
    public async Task SetDate_RelabelsTheCallEverywhere()
    {
        CallJob job = new()
        {
            Id = ObjectId.GenerateNewId().ToString(),
            UserId = ObjectId.GenerateNewId().ToString(),
            Partner = "Rii",
            Status = CallJobStatus.Completed,
            CreatedAt = new DateTime(2026, 11, 3, 10, 0, 0, DateTimeKind.Utc),
            Segments = [new TranscriptSegment { Start = 1, Text = "家に勉強します" }]
        };
        string before = job.SourceLabel;
        JapaneseDiagnosis diagnosis = new() { UserId = job.UserId };
        // An old call: its examples are found by the label only
        diagnosis.RecordIssue(IssueCategory.Particle, "に vs で", "", new IssueExample { Said = "家に勉強します", Source = before }, 2, DateTime.UtcNow);
        diagnosis.RecordIssue(IssueCategory.Particle, "other call", "", new IssueExample { Said = "x", Source = "chat" }, 2, DateTime.UtcNow);
        diagnosis.AddSnapshot(new SpeechSnapshot { Source = before });
        InMemoryPersonProfileDatabase people = new();
        await people.SaveDocumentAsync(new PersonProfile
        {
            UserId = job.UserId,
            Name = "Rii",
            Calls = [new PersonCall { CallId = job.Id!, Date = job.CreatedAt }],
            Facts = [new PersonFact { Id = "f", CallId = job.Id!, Date = job.CreatedAt, Text = "Rii plays games." }]
        });

        Assert.True(await CallJobs.SetDateAsync(job, new DateTime(2026, 10, 21), diagnosis, people));

        Assert.Equal("call with Rii 2026-10-21", job.SourceLabel);
        Assert.Single(job.Findings);
        Assert.Equal(job.SourceLabel, diagnosis.Issues[0].Examples.Single().Source);
        Assert.Equal("chat", diagnosis.Issues[1].Examples.Single().Source);
        Assert.Equal(job.SourceLabel, diagnosis.Snapshots.Single().Source);
        PersonProfile profile = people.Items.Single();
        Assert.Equal(new DateTime(2026, 10, 21), profile.Calls.Single().Date);
        Assert.Equal(new DateTime(2026, 10, 21), profile.Facts.Single().Date);

        // Taking the copied finding back still finds its example under the new label
        CallDiagnosis.SetFeedback(diagnosis, job, job.Findings.Single(), FindingFeedback.NotAMistake, DateTime.UtcNow);
        Assert.DoesNotContain(diagnosis.Issues, i => i.Pattern == "に vs で");
        Assert.False(await CallJobs.SetDateAsync(job, new DateTime(2026, 10, 21), diagnosis, people));
    }

    [Fact]
    public async Task Create_StoresTheRecordingWithTheJob()
    {
        using TempDirectory temp = new();
        InMemoryCallJobDatabase jobs = new();
        CallAnalysisOptions options = new() { StorageDirectory = temp.Path };

        CallJob job = await CallJobs.CreateAsync(jobs, options, ObjectId.GenerateNewId().ToString(), new MemoryStream([1, 2, 3]),
            "Yuki.srt", new CallRequest("Yuki", ExpectedRegister.Casual, LearnerChannel.Mixed, "Me"), null, ("a", "d"));

        Assert.True(job.IsTranscript);
        Assert.Equal(Path.Combine(temp.Path, job.Id!, "Yuki.srt"), job.SourcePath);
        Assert.Equal([1, 2, 3], await File.ReadAllBytesAsync(job.SourcePath));
        Assert.Equal(("a", "d"), (job.AnalysisCharacterId, job.DeliveryCharacterId));
    }

    [Fact]
    public void LearnerText_UsesTheLabeledLines()
    {
        CallJob job = new()
        {
            LearnerSpeaker = "me",
            Segments = [new() { Speaker = "Me", Text = "私です" }, new() { Speaker = "Yuki", Text = "ユキです" }]
        };

        Assert.Equal("私です", job.LearnerText());
        Assert.True(job.HasLearnerSeparation);
    }

    [Fact]
    public void Instruction_NamesTheLearnerAndKnownFocusAreas()
    {
        JapaneseDiagnosis diagnosis = new();
        (DiagnosisIssue issue, _) = diagnosis.RecordIssue(IssueCategory.Particle, "に vs で", "", null, 2, DateTime.UtcNow);
        CallJob job = new() { Partner = "Yuki", LearnerSpeaker = "Me", Chunks = [new()] };

        string instruction = ToolCallReviewer.BuildInstruction(job, diagnosis, DateTime.UtcNow);

        Assert.Contains("Lines labeled 'Me' are {{user}}", instruction);
        Assert.Contains($"[{issue.Id}] Particle: に vs で", instruction);
        Assert.Contains("{{character}}", instruction);
        Assert.Contains("between {{user}} and Yuki", instruction);
        Assert.Contains("NoteCallCorrectUse", instruction);
        Assert.StartsWith("Transcript part 1 of 1", ToolCallReviewer.BuildChunkMessage(job, job.Chunks[0]));
    }

    [Fact]
    public void DeliveryInstruction_AsksForAShortPersonalSummary()
    {
        CallJob job = new() { Partner = "Yuki", Report = "Call analysis: ..." };

        string text = CallJobs.BuildDeliveryInstruction(job);

        Assert.StartsWith(Akagi.Characters.Conversations.TextMessage.SystemMessagePrefix, text);
        Assert.Contains("call with Yuki", text);
        Assert.Contains("Do not paste the report", text);
    }
}

public class CallJobCreationTests
{
    /// <summary>
    /// Stands for the moment the analysis service polls while a recording is still being copied.
    /// </summary>
    private sealed class ObservedStream(byte[] content, Action onRead) : MemoryStream(content)
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            onRead();
            return base.ReadAsync(buffer, cancellationToken);
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            onRead();
            return base.ReadAsync(buffer, offset, count, cancellationToken);
        }
    }

    private static readonly CallRequest Request = new("Lee", ExpectedRegister.Casual, LearnerChannel.Left, string.Empty);

    [Fact]
    public async Task Create_IsNotPendingUntilTheFileIsComplete()
    {
        using TempDirectory temp = new();
        InMemoryCallJobDatabase jobs = new();
        List<CallJob?> seenDuringCopy = [];
        using ObservedStream content = new(new byte[100_000], () => seenDuringCopy.Add(jobs.GetNextPending().Result));

        CallJob job = await CallJobs.CreateAsync(jobs, new CallAnalysisOptions { StorageDirectory = temp.Path }, ObjectId.GenerateNewId().ToString(), content,
            "call.mp3", Request, null, ("a", "b"));

        Assert.NotEmpty(seenDuringCopy);
        Assert.All(seenDuringCopy, Assert.Null);
        CallJob pending = (await jobs.GetNextPending())!;
        Assert.Same(job, pending);
        Assert.Equal(100_000, new FileInfo(pending.SourcePath).Length);
        Assert.Equal(Path.Combine(temp.Path, job.Id!, "call.mp3"), pending.SourcePath);
    }

    [Fact]
    public async Task Create_WhenTheCopyFails_LeavesNoJobBehind()
    {
        using TempDirectory temp = new();
        InMemoryCallJobDatabase jobs = new();
        using ObservedStream content = new([1, 2, 3], () => throw new IOException("disk full"));

        await Assert.ThrowsAsync<IOException>(() => CallJobs.CreateAsync(jobs, new CallAnalysisOptions { StorageDirectory = temp.Path }, "user", content, "call.mp3", Request, null, ("a", "b")));

        Assert.Empty(jobs.Items);
    }

    [Fact]
    public async Task Characters_TheNewestOfANameWins()
    {
        InMemoryCharacterDatabase characters = new();
        Akagi.Users.User user = new() { Id = "user" };
        await characters.SaveDocumentAsync(new Akagi.Characters.Character { Id = "6adcc28c4f351a3450e30ab5", Name = "Akagi", UserId = "user" });
        await characters.SaveDocumentAsync(new Akagi.Characters.Character { Id = "6ae1ebfa5a533276d716f835", Name = "Akagi", UserId = "user" });
        await characters.SaveDocumentAsync(new Akagi.Characters.Character { Id = "6ae1ebfb5a533276d716f855", Name = "Sensei", UserId = "user" });

        (string analysis, string delivery) = await CallJobs.ResolveCharactersAsync(characters, user, new CallAnalysisOptions(), null);

        Assert.Equal("6ae1ebfb5a533276d716f855", analysis);
        Assert.Equal("6ae1ebfa5a533276d716f835", delivery);
    }
}

public class CallPipelineTests
{
    private sealed class StubSplitter : IAudioSplitter
    {
        public int Calls { get; private set; }
        public List<LearnerChannel> Channels { get; } = [];

        public Task<IReadOnlyList<AudioPart>> SplitAsync(string audioPath, TimeSpan partLength, LearnerChannel channel, string workDirectory, CancellationToken cancellationToken)
        {
            Calls++;
            Channels.Add(channel);
            string name = channel == LearnerChannel.Mixed ? string.Empty : Path.GetFileName(workDirectory) + "/";
            return Task.FromResult<IReadOnlyList<AudioPart>>([new AudioPart(name + "p0", 0), new AudioPart(name + "p1", 600)]);
        }

        public Task<IReadOnlyList<AudioPart>> SplitBalancedAsync(string audioPath, TimeSpan partLength, string workDirectory, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult<IReadOnlyList<AudioPart>>([new AudioPart("mix/p0", 0), new AudioPart("mix/p1", 600)]);
        }

        public Task<double[]?> MeasureLoudnessAsync(string audioPath, CancellationToken cancellationToken) => Task.FromResult<double[]?>(null);

        public DateTime? RecordingDate { get; set; }

        public Task<DateTime?> ReadRecordingDateAsync(string audioPath, CancellationToken cancellationToken) => Task.FromResult(RecordingDate);
    }

    private sealed class StubTranscriber : ITranscriber
    {
        public List<string> Transcribed { get; } = [];
        public bool IsConfigured => true;

        public Task<List<TranscriptSegment>> TranscribeAsync(string audioPath, CancellationToken cancellationToken)
        {
            Transcribed.Add(audioPath);
            string text = string.Concat(Enumerable.Repeat("なんか、今日は楽しかったです。でも、疲れました。", 6));
            return Task.FromResult<List<TranscriptSegment>>([new() { Start = 1, End = 300, Text = text }, new() { Start = 300, End = 590, Text = text }]);
        }

        /// <summary>
        /// Fails this path once, as a throttled provider would.
        /// </summary>
        public string? FailOnce { get; set; }

        public Task<List<TranscriptWord>> TranscribeWordsAsync(string audioPath, CancellationToken cancellationToken)
        {
            if (audioPath == FailOnce)
            {
                FailOnce = null;
                throw new HttpRequestException("Transcription failed (429)");
            }
            Transcribed.Add(audioPath);
            string text = audioPath.StartsWith("partner", StringComparison.Ordinal)
                ? "最近どう？"
                : string.Concat(Enumerable.Repeat("なんか、今日は楽しかったです。でも、疲れました。", 6));
            double start = audioPath.StartsWith("partner", StringComparison.Ordinal) ? 1 : 3;
            return Task.FromResult<List<TranscriptWord>>([.. text.Select((c, i) => new TranscriptWord(c.ToString(), start + i * 0.1, start + i * 0.1 + 0.08))]);
        }
    }

    /// <summary>
    /// Stands in for the LLM: notes one mistake per chunk, plus one whose quote nobody said, and consolidates them into
    /// one group.
    /// </summary>
    private sealed class StubAnalyzer : ICallChunkAnalyzer, ICallConsolidator, ICallWholeReviewer
    {
        public int Coachings { get; private set; }
        public int FactChecks { get; private set; }
        public IReadOnlyList<string> KnownPartner { get; private set; } = [];

        public Task<CoachingResult> CoachAsync(CallJob job, CancellationToken cancellationToken)
        {
            Coachings++;
            return Task.FromResult(new CoachingResult(
            [
                new CallFinding { Kind = FindingKind.Conversation, Aspect = "follow-up", Context = "最近どう？", Said = "楽しかったです", Better = "楽しかった！ユキは？", Explanation = "Ask back." },
                new CallFinding { Kind = FindingKind.Conversation, Aspect = "reaction", WentWell = true, Japanese = true, Said = "疲れました", Explanation = "Honest." },
                new CallFinding { Kind = FindingKind.Conversation, Aspect = "question", Context = "誰も言っていない", Said = "楽しかったです", Explanation = "Invented context." }
            ], new CallCoaching { Summary = "You answer but rarely ask back.", Tips = ["Ask back: ユキは？"] }));
        }

        public Task<List<CallFinding>> FactsAsync(CallJob job, IReadOnlyList<string> knownPartner, IReadOnlyList<string> knownLearner, CancellationToken cancellationToken)
        {
            FactChecks++;
            KnownPartner = knownPartner;
            return Task.FromResult(new List<CallFinding>
            {
                new() { Kind = FindingKind.Fact, Subject = FactSubject.Partner, Aspect = "personality", Pattern = "Yuki likes to check in on friends.", Said = "最近どう？", FollowUp = true },
                new() { Kind = FindingKind.Fact, Subject = FactSubject.Learner, Aspect = "events", Pattern = "The learner had a fun but tiring day.", Said = "疲れました" },
                new() { Kind = FindingKind.Fact, Subject = FactSubject.Partner, Aspect = "work", Pattern = "Yuki is a pilot.", Said = "パイロットです" }
            });
        }

        public List<int> Analyzed { get; } = [];
        public int FailOnChunk { get; set; } = -1;
        public int Consolidations { get; private set; }
        public bool ConsolidationGivesNothing { get; set; }

        public Task<ChunkReview> AnalyzeAsync(CallJob job, CallChunk chunk, JapaneseDiagnosis? diagnosis, CancellationToken cancellationToken)
        {
            if (chunk.Index == FailOnChunk)
            {
                FailOnChunk = -1;
                throw new HttpRequestException("rate limited");
            }
            Analyzed.Add(chunk.Index);
            return Task.FromResult(new ChunkReview(
            [
                new CallFinding { Kind = FindingKind.Mistake, Category = IssueCategory.Register, Pattern = "polite with a friend", Said = "楽しかったです", Better = "楽しかった", Explanation = "です/ます with a close friend" },
                new CallFinding { Kind = FindingKind.Mistake, Category = IssueCategory.Particle, Pattern = "invented", Said = "誰も言っていない言葉です" }
            ], $"Part {chunk.Index + 1} small talk."));
        }

        public Task<Consolidation?> ConsolidateAsync(CallJob job, JapaneseDiagnosis? diagnosis, CancellationToken cancellationToken)
        {
            Consolidations++;
            if (ConsolidationGivesNothing)
            {
                return Task.FromResult<Consolidation?>(null);
            }
            Consolidation result = new() { Summary = "A relaxed call.", Priorities = ["Drop です with friends."] };
            result.Groups.Add(new FindingGroup([.. job.Findings.Where(f => f.State == FindingState.Candidate).Select(f => f.Id)], string.Empty,
                "Register", "too polite with friends", "です/ます with a close friend", 2));
            return Task.FromResult<Consolidation?>(result);
        }
    }

    private sealed class Fixture
    {
        public const string Akagi = "6ae1ebfa5a533276d716f834";

        public InMemoryCallJobDatabase Jobs { get; } = new();
        public InMemoryJapaneseDiagnosisDatabase Diagnoses { get; } = new();
        public InMemoryPersonProfileDatabase People { get; } = new();
        public InMemoryMemoryDatabase Memories { get; } = new();
        public InMemoryCompanionStateDatabase Companions { get; } = new();
        public CallMemory Memory { get; }
        public StubSplitter Splitter { get; } = new();
        public StubTranscriber Transcriber { get; } = new();
        public StubAnalyzer Analyzer { get; }
        public List<CallJob> Delivered { get; } = [];
        public CallAnalysisPipeline Pipeline { get; }

        public Fixture(string storage)
        {
            Analyzer = new StubAnalyzer();
            Memory = new CallMemory(People, Memories, Companions, null, Microsoft.Extensions.Logging.Abstractions.NullLogger<CallMemory>.Instance);
            Pipeline = new CallAnalysisPipeline(Jobs, Diagnoses, Splitter, Transcriber, Analyzer, Analyzer, Analyzer, Memory,
                job => { Delivered.Add(job); return Task.CompletedTask; },
                new CallAnalysisOptions { StorageDirectory = storage, ChunkCharacters = 600 }, NullLogger.Instance);
        }

        public async Task<CallJob> Queue(LearnerChannel channel = LearnerChannel.Left)
        {
            CallJob job = new() { UserId = ObjectId.GenerateNewId().ToString(), Title = "Yuki", Partner = "Yuki", SourcePath = "call.m4a", LearnerChannel = channel, DeliveryCharacterId = Akagi };
            await Jobs.SaveDocumentAsync(job);
            return job;
        }
    }

    [Fact]
    public async Task Run_TranscribesAnalyzesReportsAndDelivers()
    {
        using TempDirectory temp = new();
        Fixture fixture = new(temp.Path);
        CallJob job = await fixture.Queue();

        await fixture.Pipeline.RunAsync(job, CancellationToken.None);

        Assert.Equal(CallJobStatus.Completed, job.Status);
        Assert.Equal(["learner/p0", "partner/p0", "mix/p0", "learner/p1", "partner/p1", "mix/p1"], fixture.Transcriber.Transcribed);
        Assert.Equal([LearnerChannel.Left, LearnerChannel.Right], fixture.Splitter.Channels);
        Assert.True(job.HasBothVoices);
        Assert.Equal("Me", job.LearnerSpeaker);
        Assert.Equal(("Yuki", "最近どう？", 1), (job.Segments[0].Speaker, job.Segments[0].Text, job.Segments[0].Start));
        Assert.Equal("Me", job.Segments[1].Speaker);
        Assert.Equal(601, job.Segments.First(s => s.Start > 600).Start);
        Assert.DoesNotContain("最近どう", job.LearnerText());
        Assert.True(job.Chunks.Count > 1);
        Assert.All(job.Chunks, c => Assert.True(c.Analyzed));
        Assert.Contains("Noticed in this call", job.Report);
        Assert.Contains("too polite with friends", job.Report);
        Assert.Contains("Measured on the learner's speech", job.Report);
        Assert.Same(job, Assert.Single(fixture.Delivered));
        Assert.True(job.Delivered);

        JapaneseDiagnosis diagnosis = fixture.Diagnoses.Items.Single();
        Assert.Single(diagnosis.Snapshots);
        Assert.Contains(diagnosis.Issues, i => i.Category == IssueCategory.CrutchWord && i.Pattern == "なんか");
    }

    [Fact]
    public async Task Run_AFailedPassDoesNotPayForTheFinishedPassesAgainAndRetryResumes()
    {
        using TempDirectory temp = new();
        Fixture fixture = new(temp.Path);
        CallJob job = await fixture.Queue();
        fixture.Transcriber.FailOnce = "mix/p1";

        await Assert.ThrowsAsync<HttpRequestException>(() => fixture.Pipeline.RunAsync(job, CancellationToken.None));
        for (int i = 0; i < CallJob.MaxAttempts; i++)
        {
            await fixture.Pipeline.FailAttemptAsync(job, new HttpRequestException("429"));
        }
        Assert.Equal(CallJobStatus.Failed, job.Status);
        Assert.Equal(1, job.TranscribedParts);

        Assert.True(CallJobs.Requeue(job));
        Assert.Equal((CallJobStatus.Transcribing, 0, string.Empty), (job.Status, job.Attempts, job.Error));
        Assert.False(CallJobs.Requeue(job));

        await fixture.Pipeline.RunAsync(job, CancellationToken.None);

        Assert.Equal(CallJobStatus.Completed, job.Status);
        // Part 1's two channel passes were kept on disk; only its mix pass ran again
        Assert.Equal(["learner/p0", "partner/p0", "mix/p0", "learner/p1", "partner/p1", "mix/p1"], fixture.Transcriber.Transcribed);
    }

    [Fact]
    public async Task Run_MixedRecordingsDoNotSkewTheTrend()
    {
        using TempDirectory temp = new();
        Fixture fixture = new(temp.Path);
        CallJob job = await fixture.Queue(LearnerChannel.Mixed);

        await fixture.Pipeline.RunAsync(job, CancellationToken.None);

        JapaneseDiagnosis diagnosis = fixture.Diagnoses.Items.Single();
        Assert.Empty(diagnosis.Snapshots);
        Assert.DoesNotContain(diagnosis.Issues, i => i.Category == IssueCategory.CrutchWord);
        Assert.Contains("only rough", job.Report);
        Assert.Equal(["p0", "p1"], fixture.Transcriber.Transcribed);
        Assert.False(job.HasBothVoices);
    }

    [Fact]
    public async Task Run_ResumesAfterAFailureWithoutRepeatingPaidWork()
    {
        using TempDirectory temp = new();
        Fixture fixture = new(temp.Path);
        fixture.Analyzer.FailOnChunk = 1;
        CallJob job = await fixture.Queue();

        await Assert.ThrowsAsync<HttpRequestException>(() => fixture.Pipeline.RunAsync(job, CancellationToken.None));
        await fixture.Pipeline.FailAttemptAsync(job, new HttpRequestException("rate limited"));
        Assert.Equal(CallJobStatus.Analyzing, job.Status);
        Assert.Equal(1, job.Attempts);
        Assert.Equal("rate limited", job.Error);

        await fixture.Pipeline.RunAsync(job, CancellationToken.None);

        Assert.Equal(CallJobStatus.Completed, job.Status);
        Assert.Equal(6, fixture.Transcriber.Transcribed.Count);
        Assert.Equal(3, fixture.Splitter.Calls);
        Assert.Equal([.. Enumerable.Range(0, job.Chunks.Count)], fixture.Analyzer.Analyzed);
        Assert.Equal(string.Empty, job.Error);
    }

    [Fact]
    public async Task FailAttempt_GivesUpAfterMaxAttempts()
    {
        using TempDirectory temp = new();
        Fixture fixture = new(temp.Path);
        CallJob job = await fixture.Queue();

        for (int i = 0; i < CallJob.MaxAttempts; i++)
        {
            await fixture.Pipeline.FailAttemptAsync(job, new InvalidOperationException("ffmpeg missing"));
        }

        Assert.Equal(CallJobStatus.Failed, job.Status);
        Assert.Null(await fixture.Jobs.GetNextPending());
    }

    [Fact]
    public async Task Run_TranscriptJobsSkipAudioEntirely()
    {
        using TempDirectory temp = new();
        Fixture fixture = new(temp.Path);
        string path = Path.Combine(temp.Path, "call.txt");
        await File.WriteAllTextAsync(path, "[00:00:01] Me: えーと、明日は行かざるを得ないです。\n[00:00:05] Yuki: そっか");
        CallJob job = await fixture.Queue(LearnerChannel.Mixed);
        job.IsTranscript = true;
        job.LearnerSpeaker = "Me";
        job.SourcePath = path;

        await fixture.Pipeline.RunAsync(job, CancellationToken.None);

        Assert.Equal(CallJobStatus.Completed, job.Status);
        Assert.Empty(fixture.Transcriber.Transcribed);
        Assert.Equal(0, fixture.Splitter.Calls);
        Assert.Equal(1, fixture.Diagnoses.Items.Single().PatternUsage.Single(u => u.Pattern == "～ざるを得ない").Count);
    }

    [Fact]
    public async Task Run_FailsCleanlyWhenNothingWasSaid()
    {
        using TempDirectory temp = new();
        Fixture fixture = new(temp.Path);
        string path = Path.Combine(temp.Path, "empty.txt");
        await File.WriteAllTextAsync(path, "   \n");
        CallJob job = await fixture.Queue();
        job.IsTranscript = true;
        job.SourcePath = path;

        await fixture.Pipeline.RunAsync(job, CancellationToken.None);

        Assert.Equal(CallJobStatus.Failed, job.Status);
        Assert.Contains("No speech", job.Error);
        Assert.Empty(fixture.Delivered);
    }

    [Fact]
    public async Task Run_KeepsEveryFindingOnTheCall_AndOnlyCheckedOnesReachTheDiagnosis()
    {
        using TempDirectory temp = new();
        Fixture fixture = new(temp.Path);
        CallJob job = await fixture.Queue();

        await fixture.Pipeline.RunAsync(job, CancellationToken.None);

        int chunks = job.Chunks.Count;
        Assert.Equal(1, fixture.Analyzer.Consolidations);
        Assert.All(job.Chunks, c => Assert.StartsWith("Part ", c.Summary));

        List<CallFinding> reviewed = [.. job.Findings.Where(f => f.Origin == FindingOrigin.Review && f.Kind == FindingKind.Mistake)];
        Assert.Equal(2 * chunks, reviewed.Count);
        List<CallFinding> invented = [.. reviewed.Where(f => f.Pattern == "invented")];
        Assert.Equal(chunks, invented.Count);
        Assert.All(invented, f => Assert.Equal((FindingState.Rejected, QuoteCheck.NotFound, false), (f.State, f.Quote, f.Applied)));

        List<CallFinding> kept = [.. reviewed.Where(f => f.State == FindingState.Kept)];
        Assert.Equal(chunks, kept.Count);
        Assert.All(kept, f =>
        {
            Assert.Equal("too polite with friends", f.Pattern);
            Assert.Equal(QuoteCheck.Found, f.Quote);
            Assert.True(f.Applied);
            Assert.NotNull(f.Time);
        });
        Assert.Single(kept.Select(f => f.IssueId).Distinct());

        JapaneseDiagnosis diagnosis = fixture.Diagnoses.Items.Single();
        DiagnosisIssue issue = diagnosis.GetIssue(kept[0].IssueId)!;
        // The local register measurement notices the same politeness problem and counts once more
        Assert.Contains(job.Findings, f => f.Origin == FindingOrigin.Measurement && f.IssueId == issue.Id);
        Assert.Equal(job.Findings.Count(f => f.IssueId == issue.Id && f.Applied), issue.Count);
        Assert.DoesNotContain(diagnosis.Issues, i => i.Pattern == "invented");

        // The local measurements are findings too, so the call keeps all of its evidence
        Assert.Contains(job.Findings, f => f.Origin == FindingOrigin.Measurement && f.Pattern == "なんか");
        Assert.Equal("A relaxed call.", job.Review!.Summary);
        Assert.Contains("Tutor's review: A relaxed call.", job.Report);
        Assert.Contains("- Drop です with friends.", job.Report);
        Assert.Contains("filtered out", job.Report);

        Assert.NotNull(job.Stats);
        Assert.True(job.Stats!.LearnerShare > 0.5);
        Assert.Contains(job.Stats.Words, w => w.Lemma == "楽しい" && w.Learner > 0);
        Assert.Contains("Conversation: the learner spoke", job.Report);
    }

    [Fact]
    public async Task Run_KeepsCheckedFindingsAsNoted_WhenTheConsolidationGivesNothing()
    {
        using TempDirectory temp = new();
        Fixture fixture = new(temp.Path);
        fixture.Analyzer.ConsolidationGivesNothing = true;
        CallJob job = await fixture.Queue();

        await fixture.Pipeline.RunAsync(job, CancellationToken.None);

        Assert.True(job.Review!.Fallback);
        Assert.All(job.Findings.Where(f => f.Pattern == "polite with a friend"), f => Assert.Equal(FindingState.Kept, f.State));
        Assert.Equal(job.Chunks.Count, fixture.Diagnoses.Items.Single().Issues.Single(i => i.Pattern == "polite with a friend").Count);
    }

    [Fact]
    public async Task Reanalyze_TakesTheEarlierFindingsOutOfTheDiagnosis()
    {
        using TempDirectory temp = new();
        Fixture fixture = new(temp.Path);
        CallJob job = await fixture.Queue();
        await fixture.Pipeline.RunAsync(job, CancellationToken.None);
        JapaneseDiagnosis diagnosis = fixture.Diagnoses.Items.Single();
        int count = diagnosis.Issues.Single(i => i.Pattern == "too polite with friends").Count;

        Assert.True(await CallJobs.ReanalyzeAsync(job, diagnosis, fixture.Memory));

        Assert.DoesNotContain(diagnosis.Issues, i => i.Pattern == "too polite with friends");
        Assert.DoesNotContain(diagnosis.Issues, i => i.Pattern == "なんか");
        Assert.Equal(CallJobStatus.Analyzing, job.Status);
        Assert.Empty(job.Findings);
        Assert.All(job.Chunks, c => Assert.False(c.Analyzed));

        await fixture.Pipeline.RunAsync(job, CancellationToken.None);

        Assert.Equal(count, diagnosis.Issues.Single(i => i.Pattern == "too polite with friends").Count);
        Assert.Equal(6, fixture.Transcriber.Transcribed.Count);
    }

    [Fact]
    public async Task NotAMistake_TakesExactlyThatFindingBack_AndChangingYourMindRestoresIt()
    {
        using TempDirectory temp = new();
        Fixture fixture = new(temp.Path);
        CallJob job = await fixture.Queue();
        await fixture.Pipeline.RunAsync(job, CancellationToken.None);
        JapaneseDiagnosis diagnosis = fixture.Diagnoses.Items.Single();
        CallFinding finding = job.KeptMistakes.First(f => f.Origin == FindingOrigin.Review);
        DiagnosisIssue issue = diagnosis.GetIssue(finding.IssueId)!;
        int before = issue.Count;

        CallDiagnosis.SetFeedback(diagnosis, job, finding, FindingFeedback.NotAMistake, DateTime.UtcNow);

        Assert.Equal(before - 1, issue.Count);
        Assert.DoesNotContain(issue.Examples, e => e.FindingId == finding.Id);
        Assert.False(finding.Counts);

        CallDiagnosis.SetFeedback(diagnosis, job, finding, FindingFeedback.Useful, DateTime.UtcNow);

        Assert.Equal(before, issue.Count);
        Assert.True(finding.Applied);
    }

    [Fact]
    public async Task Run_TakesTheDateFromTheRecording_WhenTheNameHasNone()
    {
        using TempDirectory temp = new();
        Fixture fixture = new(temp.Path);
        fixture.Splitter.RecordingDate = new DateTime(2026, 10, 21, 0, 0, 0, DateTimeKind.Utc);
        CallJob named = await fixture.Queue();
        named.RecordedAt = new DateTime(2026, 10, 20, 0, 0, 0, DateTimeKind.Utc);
        CallJob unnamed = await fixture.Queue();

        await fixture.Pipeline.RunAsync(unnamed, CancellationToken.None);
        await fixture.Pipeline.RunAsync(named, CancellationToken.None);

        Assert.Equal("call with Yuki 2026-10-21", unnamed.SourceLabel);
        Assert.Equal(new DateTime(2026, 10, 20), named.RecordedAt!.Value);
    }

    [Fact]
    public async Task Run_CoachesTheConversationAndRemembersThePeople()
    {
        using TempDirectory temp = new();
        Fixture fixture = new(temp.Path);
        CallJob job = await fixture.Queue();

        await fixture.Pipeline.RunAsync(job, CancellationToken.None);

        Assert.Equal((1, 1), (fixture.Analyzer.Coachings, fixture.Analyzer.FactChecks));
        List<CallFinding> moments = [.. job.Findings.Where(f => f.Kind == FindingKind.Conversation)];
        Assert.Equal(3, moments.Count);
        Assert.Equal(2, moments.Count(m => m.State == FindingState.Kept));
        CallFinding invented = moments.Single(m => m.Aspect == "question");
        Assert.Equal(FindingState.Rejected, invented.State);
        Assert.Contains("reacts to", invented.Note);
        Assert.NotNull(moments.Single(m => m.Aspect == "follow-up").Time);
        Assert.Equal("You answer but rarely ask back.", job.Coaching!.Summary);
        Assert.Contains("Conversation: You answer but rarely ask back.", job.Report);
        Assert.Contains("- Ask back: ユキは？", job.Report);

        // The partner's facts land in their profile and the companion's people, the learner's in her memories
        PersonProfile profile = Assert.Single(fixture.People.Items);
        Assert.Equal("Yuki", profile.Name);
        PersonFact fact = Assert.Single(profile.Facts);
        Assert.Equal(("Yuki likes to check in on friends.", "personality", true), (fact.Text, fact.Topic, fact.FollowUp));
        Assert.Equal(job.Id, Assert.Single(profile.Calls).CallId);
        Assert.Contains("Yuki likes to check in on friends.", fixture.Companions.Items.Single().FindPerson("Yuki")!.Notes);
        MemoryRecord memory = Assert.Single(fixture.Memories.Items);
        Assert.Equal((Fixture.Akagi, CallMemory.MemoryCategory), (memory.CharacterId, memory.Category));
        Assert.Contains("fun but tiring day", memory.Fact);
        CallFinding pilot = job.Findings.Single(f => f.Pattern == "Yuki is a pilot.");
        Assert.Equal((FindingState.Rejected, false), (pilot.State, pilot.Applied));
        Assert.Contains("Worth asking about next time: Yuki likes to check in on friends.", job.Report);

        // Taking a fact back removes it everywhere it was written
        CallFinding written = job.Findings.Single(f => f.Pattern == "Yuki likes to check in on friends.");
        await fixture.Memory.UndoAsync(job, written);
        Assert.Empty(profile.Facts);
        Assert.Empty(fixture.Companions.Items.Single().FindPerson("Yuki")!.Notes);
        await fixture.Memory.UndoAsync(job, job.Findings.Single(f => f.Subject == FactSubject.Learner && f.Kind == FindingKind.Fact));
        Assert.Empty(fixture.Memories.Items);
    }

    [Fact]
    public async Task CatchUp_RunsOnlyTheMissingPasses_AndDoesNotDeliverAgain()
    {
        using TempDirectory temp = new();
        Fixture fixture = new(temp.Path);
        CallJob job = await fixture.Queue();
        await fixture.Pipeline.RunAsync(job, CancellationToken.None);
        Assert.False(CallJobs.CatchUp(job));

        // As if it had been analyzed before these passes existed
        job.Coaching = null;
        job.FactsChecked = false;
        job.Findings.RemoveAll(f => f.Kind is FindingKind.Conversation or FindingKind.Fact && !f.Applied);
        int issues = fixture.Diagnoses.Items.Single().Issues.Sum(i => i.Count);

        Assert.True(CallJobs.CatchUp(job));
        Assert.Equal(CallJobStatus.Coaching, job.Status);
        await fixture.Pipeline.RunAsync(job, CancellationToken.None);

        Assert.Equal(CallJobStatus.Completed, job.Status);
        Assert.Equal((2, 2, 1), (fixture.Analyzer.Coachings, fixture.Analyzer.FactChecks, fixture.Analyzer.Consolidations));
        Assert.Single(fixture.Delivered);
        Assert.Equal(issues, fixture.Diagnoses.Items.Single().Issues.Sum(i => i.Count));
        // What the first run learned is known to the second
        Assert.Contains("Yuki likes to check in on friends.", fixture.Analyzer.KnownPartner);
        Assert.Single(fixture.People.Items.Single().Calls);
    }
}
