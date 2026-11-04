using Akagi.Bridge.Chat.Models;
using Akagi.Bridge.Chat.Transmissions.Requests;
using Akagi.Bridge.Chat.Transmissions.Responses;
using Akagi.Communication.SocketComs.Transmissions;
using Akagi.Life.Japanese;
using Akagi.Life.Japanese.Calls;
using Akagi.Tests.Support;
using MessagePack;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Bson;

namespace Akagi.Tests.Life;

/// <summary>
/// What the website's call analysis page gets from the server.
/// </summary>
public class CallAnalysisSocketTests
{
    private const string UserId = "6adcc28c4f351a3450e30ab6";
    private const string OtherUserId = "6adcc28c4f351a3450e30ab7";

    private readonly InMemoryCallJobDatabase _jobs = new();
    private readonly InMemoryJapaneseDiagnosisDatabase _diagnoses = new();
    private readonly CallAnalysisRequestHandler _handler;

    public CallAnalysisSocketTests()
    {
        _handler = new CallAnalysisRequestHandler(_jobs, _diagnoses, NullLogger<CallAnalysisRequestHandler>.Instance);
    }

    private CallJob AddJob(string userId, string partner, DateTime recordedAt, CallJobStatus status = CallJobStatus.Completed)
    {
        CallJob job = new()
        {
            Id = ObjectId.GenerateNewId().ToString(),
            UserId = userId,
            Title = $"{recordedAt:yyyy-MM-dd} {partner} [left]",
            Partner = partner,
            LearnerChannel = LearnerChannel.Left,
            LearnerSpeaker = CallJob.DefaultLearnerLabel,
            RecordedAt = recordedAt,
            CreatedAt = recordedAt.AddHours(1),
            UpdatedAt = recordedAt.AddHours(2),
            Status = status,
            Report = "Call analysis: done.",
            Segments =
            [
                new TranscriptSegment { Start = 0, End = 4, Speaker = partner, Text = "最近どう？" },
                new TranscriptSegment { Start = 5, End = 9, Speaker = "Me", Text = "えーと、仕事がすごく忙しいです。えーと、毎日残業です。", Disputed = "残業 -> 残業中" },
                new TranscriptSegment { Start = 10, End = 14, Speaker = "Me", Text = "えーと、でも休むに越したことはないです。" }
            ],
            Chunks = [new CallChunk { Index = 0, Analyzed = true, IssuesRecorded = 2 }]
        };
        _jobs.SaveDocumentAsync(job).Wait();
        return job;
    }

    private Task<CallAnalysisResponseTransmission> Send(CallAnalysisAction action, string callId = "", string userId = UserId) =>
        _handler.ProcessAsync(userId, new CallAnalysisRequestTransmission { RequestId = "r1", Action = action, CallId = callId });

    [Fact]
    public async Task List_ShowsOnlyTheUsersCalls_NewestFirst()
    {
        CallJob older = AddJob(UserId, "Yuki", new DateTime(2026, 10, 20, 0, 0, 0, DateTimeKind.Utc));
        CallJob newer = AddJob(UserId, "Ken", new DateTime(2026, 10, 27, 0, 0, 0, DateTimeKind.Utc), CallJobStatus.Analyzing);
        AddJob(OtherUserId, "Someone", new DateTime(2026, 10, 28, 0, 0, 0, DateTimeKind.Utc));

        CallAnalysisResponseTransmission response = await Send(CallAnalysisAction.List);

        Assert.Equal("r1", response.RequestId);
        Assert.Equal([newer.Id, older.Id], response.Calls.Select(c => c.Id));
        Assert.Equal(CallAnalysisState.Analyzing, response.Calls[0].State);
        Assert.Equal(2, response.Calls[1].IssueCount);
        Assert.Equal(14, response.Calls[1].DurationSeconds);
    }

    [Fact]
    public async Task Detail_GathersStrugglesSpeechAndTranscriptOfThatCall()
    {
        CallJob job = AddJob(UserId, "Yuki", new DateTime(2026, 10, 20, 0, 0, 0, DateTimeKind.Utc));
        JapaneseDiagnosis diagnosis = new() { UserId = UserId };
        DateTime during = job.CreatedAt.AddMinutes(30);
        diagnosis.RecordIssue(IssueCategory.UnnaturalPhrasing, "すごく忙しい", "Sounds stiff here.",
            new IssueExample { Said = "すごく忙しいです", Better = "めっちゃ忙しい", Source = job.SourceLabel }, 3, during);
        diagnosis.RecordIssue(IssueCategory.Particle, "に vs で", "Place of action takes で.",
            new IssueExample { Said = "家に勉強する", Better = "家で勉強する", Source = "chat" }, 2, during);
        diagnosis.RecordStrength("Natural back-channeling", during);
        diagnosis.RecordStrength("Something from long ago", job.CreatedAt.AddDays(-5));
        await _diagnoses.SaveDocumentAsync(diagnosis);

        CallAnalysisDetail detail = (await Send(CallAnalysisAction.Detail, job.Id!)).Detail!;

        CallStruggle struggle = Assert.Single(detail.Struggles);
        Assert.Equal("Unnatural phrasing", struggle.Category);
        Assert.Equal("めっちゃ忙しい", Assert.Single(struggle.Examples).Better);
        Assert.Equal(1, struggle.TimesInCall);
        Assert.Equal(["Natural back-channeling"], detail.Strengths);
        Assert.Equal("Call analysis: done.", detail.Report);
        Assert.Equal("casual", detail.Register);

        Assert.Equal(3, detail.Transcript.Length);
        Assert.False(detail.Transcript[0].IsLearner);
        Assert.True(detail.Transcript[1].IsLearner);
        Assert.Equal("残業 -> 残業中", detail.Transcript[1].Disputed);

        CallSpeech speech = detail.Speech!;
        Assert.True(speech.LearnerOnly);
        Assert.Contains(speech.Fillers, f => f.Form == "えーと" && f.Count == 3);
        Assert.Contains(speech.Grammar, g => g.Count > 0);
    }

    [Fact]
    public async Task Detail_RefusesSomeoneElsesCall()
    {
        CallJob foreign = AddJob(OtherUserId, "Someone", new DateTime(2026, 10, 28, 0, 0, 0, DateTimeKind.Utc));

        CallAnalysisResponseTransmission response = await Send(CallAnalysisAction.Detail, foreign.Id!);

        Assert.Null(response.Detail);
        Assert.NotNull(response.Error);
    }

    [Fact]
    public async Task Detail_SurvivesTheWire()
    {
        CallJob job = AddJob(UserId, "Yuki", new DateTime(2026, 10, 20, 0, 0, 0, DateTimeKind.Utc));

        CallAnalysisResponseTransmission response = await Send(CallAnalysisAction.Detail, job.Id!);
        CallAnalysisResponseTransmission copy = MessagePackSerializer.Deserialize<CallAnalysisResponseTransmission>(MessagePackSerializer.Serialize(response));

        Assert.Equal(job.Id, copy.Detail!.Summary.Id);
        Assert.Equal(3, copy.Detail.Transcript.Length);
        Assert.Equal(response.Detail!.Speech!.Fillers.Length, copy.Detail.Speech!.Fillers.Length);
    }

    [Theory]
    [InlineData("UnnaturalPhrasing", "Unnatural phrasing")]
    [InlineData("Particle", "Particle")]
    [InlineData("GrammarPattern", "Grammar pattern")]
    public void Humanize_SplitsCategoryNames(string name, string expected)
    {
        Assert.Equal(expected, CallAnalysisRequestHandler.Humanize(name));
    }
}
