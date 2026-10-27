using Akagi.Connectors.Fitbit;
using Akagi.Life;
using Akagi.Life.Health;
using Akagi.Receivers.Commands.Life;
using Akagi.Tests.Support;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Web;

namespace Akagi.Tests.Life;

internal class InMemoryFitbitAccountDatabase : InMemoryDatabase<FitbitAccount>, IFitbitAccountDatabase
{
    public InMemoryFitbitAccountDatabase() : base("fitbit_accounts")
    {
    }

    public Task<FitbitAccount?> GetForUser(string userId) => Task.FromResult(Items.FirstOrDefault(a => a.UserId == userId));

    public async Task<FitbitAccount> GetOrCreate(string userId) => await GetForUser(userId) ?? await Create(userId);

    public Task<List<FitbitAccount>> GetConnected() => Task.FromResult(Items.Where(a => a.IsConnected).ToList());

    private async Task<FitbitAccount> Create(string userId)
    {
        FitbitAccount account = new() { UserId = userId };
        await SaveDocumentAsync(account);
        return account;
    }
}

/// <summary>
/// Answers HTTP requests from a queue and records what was sent.
/// </summary>
internal sealed class QueueHandler : HttpMessageHandler
{
    private readonly Queue<(HttpStatusCode Status, string Body)> _responses = new();

    public List<(HttpRequestMessage Request, string Body)> Requests { get; } = [];

    public QueueHandler Then(string body, HttpStatusCode status = HttpStatusCode.OK)
    {
        _responses.Enqueue((status, body));
        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string body = request.Content == null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add((request, body));
        (HttpStatusCode status, string response) = _responses.Dequeue();
        return new HttpResponseMessage(status) { Content = new StringContent(response, Encoding.UTF8, "application/json") };
    }
}

public class FitbitClientTests
{
    private const string TokenJson = """{"access_token":"access-2","refresh_token":"refresh-2","expires_in":28800,"user_id":"ABC12","scope":"sleep activity"}""";

    private static FitbitClient Client(QueueHandler handler, string secret = "") => new(new HttpClient(handler), new StaticOptions(new FitbitOptions
    {
        ClientId = "client",
        ClientSecret = secret,
        RedirectUri = "http://localhost:8080/fitbit",
        ApiBaseUrl = "https://api.fitbit.test/"
    }));

    private static FitbitAccount Linked(DateTime expires) => new()
    {
        UserId = ObjectId.GenerateNewId().ToString(),
        AccessToken = "access-1",
        RefreshToken = "refresh-1",
        ExpiresAtUtc = expires
    };

    [Theory]
    [InlineData("abc123", "abc123", null)]
    [InlineData("http://localhost:8080/fitbit?code=abc123&state=xyz#_=_", "abc123", "xyz")]
    [InlineData("?code=abc123", "abc123", null)]
    [InlineData("code=abc123&state=s", "abc123", "s")]
    public void ExtractCode_AcceptsCodesAndRedirectAddresses(string input, string code, string? state)
    {
        Assert.Equal((code, state), FitbitClient.ExtractCode(input));
    }

    [Fact]
    public void StartAuthorization_UsesPkceAndState()
    {
        FitbitAccount account = new();

        string url = Client(new QueueHandler()).StartAuthorization(account);

        Uri uri = new(url);
        System.Collections.Specialized.NameValueCollection query = HttpUtility.ParseQueryString(uri.Query);
        string expectedChallenge = Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(account.PendingVerifier)))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        Assert.Equal("client", query["client_id"]);
        Assert.Equal("S256", query["code_challenge_method"]);
        Assert.Equal(expectedChallenge, query["code_challenge"]);
        Assert.Equal(account.PendingState, query["state"]);
        Assert.Equal("sleep activity heartrate", query["scope"]);
        Assert.True(account.PendingVerifier.Length >= 43);
    }

    [Fact]
    public async Task CompleteAuthorization_ExchangesCodeWithVerifier()
    {
        QueueHandler handler = new QueueHandler().Then(TokenJson);
        FitbitClient client = Client(handler, secret: "secret");
        FitbitAccount account = new();
        client.StartAuthorization(account);
        string verifier = account.PendingVerifier;

        await client.CompleteAuthorizationAsync(account, $"http://localhost:8080/fitbit?code=the-code&state={account.PendingState}", CancellationToken.None);

        (HttpRequestMessage request, string body) = Assert.Single(handler.Requests);
        Assert.Equal("https://api.fitbit.test/oauth2/token", request.RequestUri!.ToString());
        Assert.Equal("Basic", request.Headers.Authorization!.Scheme);
        Assert.Contains("grant_type=authorization_code", body);
        Assert.Contains("code=the-code", body);
        Assert.Contains($"code_verifier={verifier}", body);
        Assert.Equal(("access-2", "refresh-2", "ABC12"), (account.AccessToken, account.RefreshToken, account.FitbitUserId));
        Assert.True(account.IsConnected);
        Assert.Empty(account.PendingVerifier);
        Assert.NotNull(account.ConnectedAt);
    }

    [Fact]
    public async Task CompleteAuthorization_RejectsForeignState()
    {
        FitbitClient client = Client(new QueueHandler());
        FitbitAccount account = new();
        client.StartAuthorization(account);

        await Assert.ThrowsAsync<FitbitAuthException>(() =>
            client.CompleteAuthorizationAsync(account, "http://x/?code=c&state=other", CancellationToken.None));
        await Assert.ThrowsAsync<FitbitAuthException>(() =>
            client.CompleteAuthorizationAsync(new FitbitAccount(), "code", CancellationToken.None));
    }

    [Fact]
    public async Task Get_RefreshesAnExpiringTokenAndPersistsTheRotatedOne()
    {
        QueueHandler handler = new QueueHandler().Then(TokenJson).Then("""{"sleep":[]}""");
        FitbitAccount account = Linked(DateTime.UtcNow.AddMinutes(1));
        List<string> persisted = [];

        string body = await Client(handler).GetAsync(account, "1.2/user/-/sleep/date/2026-10-25/2026-10-27.json",
            a => { persisted.Add(a.RefreshToken); return Task.CompletedTask; }, CancellationToken.None);

        Assert.Equal("""{"sleep":[]}""", body);
        Assert.Equal(["refresh-2"], persisted);
        Assert.Contains("refresh_token=refresh-1", handler.Requests[0].Body);
        Assert.Equal("Bearer access-2", handler.Requests[1].Request.Headers.Authorization!.ToString());
        Assert.Equal("https://api.fitbit.test/1.2/user/-/sleep/date/2026-10-25/2026-10-27.json", handler.Requests[1].Request.RequestUri!.ToString());
    }

    [Fact]
    public async Task Get_RetriesOnceAfterUnauthorized()
    {
        QueueHandler handler = new QueueHandler()
            .Then("""{"errors":[{"errorType":"expired_token"}]}""", HttpStatusCode.Unauthorized)
            .Then(TokenJson)
            .Then("{}");
        FitbitAccount account = Linked(DateTime.UtcNow.AddHours(5));

        await Client(handler).GetAsync(account, "1/x.json", _ => Task.CompletedTask, CancellationToken.None);

        Assert.Equal(3, handler.Requests.Count);
        Assert.Equal("access-2", account.AccessToken);
    }

    [Fact]
    public async Task Get_DisconnectsWhenTheRefreshTokenIsRevoked()
    {
        QueueHandler handler = new QueueHandler().Then("""{"errors":[{"errorType":"invalid_grant"}]}""", HttpStatusCode.BadRequest);
        FitbitAccount account = Linked(DateTime.UtcNow.AddMinutes(-5));
        int persisted = 0;

        await Assert.ThrowsAsync<FitbitAuthException>(() =>
            Client(handler).GetAsync(account, "1/x.json", _ => { persisted++; return Task.CompletedTask; }, CancellationToken.None));

        Assert.False(account.IsConnected);
        Assert.Contains("Link again", account.LastError);
        Assert.Equal(1, persisted);
    }

    internal sealed class StaticOptions(FitbitOptions value) : IOptionsMonitor<FitbitOptions>
    {
        public FitbitOptions CurrentValue => value;
        public FitbitOptions Get(string? name) => value;
        public IDisposable? OnChange(Action<FitbitOptions, string?> listener) => null;
    }
}

public class FitbitMapperTests
{
    internal const string SleepJson = """
    {"sleep":[
      {"dateOfSleep":"2026-10-27","isMainSleep":true,"minutesAsleep":392,"minutesAwake":48,"efficiency":86,
       "startTime":"2026-10-26T23:40:00.000","endTime":"2026-10-27T07:00:00.000","type":"stages",
       "levels":{"summary":{"deep":{"minutes":35},"light":{"minutes":230},"rem":{"minutes":127},"wake":{"minutes":48}}}},
      {"dateOfSleep":"2026-10-27","isMainSleep":false,"minutesAsleep":25,"efficiency":95,
       "startTime":"2026-10-27T14:00:00.000","endTime":"2026-10-27T14:30:00.000","type":"classic"},
      {"dateOfSleep":"2026-10-26","isMainSleep":true,"minutesAsleep":450,"minutesAwake":20,"efficiency":95,
       "startTime":"2026-10-25T23:00:00.000","endTime":"2026-10-26T07:00:00.000","type":"classic",
       "levels":{"summary":{"asleep":{"minutes":430},"restless":{"minutes":20},"awake":{"minutes":5}}}}
    ]}
    """;
    internal const string StepsJson = """{"activities-steps":[{"dateTime":"2026-10-26","value":"8421"},{"dateTime":"2026-10-27","value":"1203"}]}""";
    internal const string HeartJson = """{"activities-heart":[{"dateTime":"2026-10-26","value":{"restingHeartRate":58}},{"dateTime":"2026-10-27","value":{"heartRateZones":[]}}]}""";

    [Fact]
    public void Sleep_MapsMainNightStagesAndNaps()
    {
        LifeProfile profile = new() { TimeZoneId = "Europe/Berlin" };

        Assert.Equal(2, FitbitHealthMapper.ApplySleep(profile, SleepJson));

        HealthDay night = profile.HealthDays.Single(d => d.Date == "2026-10-27");
        Assert.Equal(392, night.SleepMinutes);
        Assert.Equal((35, 127, 230, 48), (night.DeepSleepMinutes, night.RemSleepMinutes, night.LightSleepMinutes, night.AwakeMinutes));
        Assert.Equal(86, night.SleepEfficiency);
        Assert.Equal(25, night.NapMinutes);
        Assert.Equal(new DateTime(2026, 10, 26, 22, 40, 0, DateTimeKind.Utc), night.SleepStartUtc);
        Assert.Equal(new DateTime(2026, 10, 27, 6, 0, 0, DateTimeKind.Utc), night.SleepEndUtc);
        Assert.Equal(FitbitHealthMapper.Source, night.Source);

        HealthDay classic = profile.HealthDays.Single(d => d.Date == "2026-10-26");
        Assert.Null(classic.DeepSleepMinutes);
        Assert.Equal(5, classic.AwakeMinutes);
        Assert.Null(classic.NapMinutes);
    }

    [Fact]
    public void StepsAndHeart_MapPerDayAndSkipMissingRestingRate()
    {
        LifeProfile profile = new();

        Assert.Equal(2, FitbitHealthMapper.ApplySteps(profile, StepsJson));
        Assert.Equal(1, FitbitHealthMapper.ApplyRestingHeartRate(profile, HeartJson));

        Assert.Equal(8421, profile.HealthDays.Single(d => d.Date == "2026-10-26").Steps);
        Assert.Equal(58, profile.HealthDays.Single(d => d.Date == "2026-10-26").RestingHeartRate);
        Assert.Null(profile.HealthDays.Single(d => d.Date == "2026-10-27").RestingHeartRate);
    }

    [Fact]
    public void Mapping_IsIdempotent()
    {
        LifeProfile profile = new();
        FitbitHealthMapper.ApplySleep(profile, SleepJson);
        FitbitHealthMapper.ApplySleep(profile, SleepJson);

        Assert.Equal(2, profile.HealthDays.Count);
    }

    [Fact]
    public void Summary_DescribesQualityAndRestlessNights()
    {
        LifeProfile profile = new();
        FitbitHealthMapper.ApplySleep(profile, SleepJson);
        DateTime morning = new(2026, 10, 27, 9, 0, 0, DateTimeKind.Utc);

        HealthSummary summary = HealthSummary.Create(profile, morning);

        Assert.True(summary.LastNightWasRestless);
        Assert.Contains("Sleep quality: deep 35m, REM 2h07m, awake 48m, efficiency 86%, napped 25m", summary.Describe());
    }
}

public class FitbitSyncTests
{
    private sealed class StubClient : IFitbitClient
    {
        public List<string> Paths { get; } = [];
        public bool IsConfigured => true;
        public string StartAuthorization(FitbitAccount account) => "url";
        public Task CompleteAuthorizationAsync(FitbitAccount account, string codeOrRedirectUrl, CancellationToken cancellationToken) => Task.CompletedTask;

        public async Task<string> GetAsync(FitbitAccount account, string path, Func<FitbitAccount, Task> persist, CancellationToken cancellationToken)
        {
            Paths.Add(path);
            account.RefreshToken = "rotated";
            await persist(account);
            return path.Contains("sleep") ? FitbitMapperTests.SleepJson : path.Contains("steps") ? FitbitMapperTests.StepsJson : FitbitMapperTests.HeartJson;
        }
    }

    private sealed class Fixture
    {
        public StubClient Client { get; } = new();
        public InMemoryFitbitAccountDatabase Accounts { get; } = new();
        public InMemoryLifeProfileDatabase Profiles { get; } = new();
        public FitbitSync Sync { get; }
        public string UserId { get; } = ObjectId.GenerateNewId().ToString();

        public Fixture()
        {
            Sync = new FitbitSync(Client, Accounts, Profiles, new FitbitClientTests.StaticOptions(new FitbitOptions { DaysToSync = 3 }));
        }
    }

    [Fact]
    public async Task SyncUser_FillsTheLifeProfileAndSavesTheAccount()
    {
        Fixture fixture = new();
        await fixture.Accounts.SaveDocumentAsync(new FitbitAccount { UserId = fixture.UserId, RefreshToken = "r", ExpiresAtUtc = DateTime.UtcNow.AddHours(1) });

        FitbitSyncResult? result = await fixture.Sync.SyncUserAsync(fixture.UserId);

        Assert.Equal(new FitbitSyncResult(2, 2, 1), result);
        Assert.Equal(3, fixture.Client.Paths.Count);
        Assert.Matches(@"^1\.2/user/-/sleep/date/\d{4}-\d{2}-\d{2}/\d{4}-\d{2}-\d{2}\.json$", fixture.Client.Paths[0]);
        Assert.Equal(392, fixture.Profiles.Items.Single().HealthDays.Single(d => d.Date == "2026-10-27").SleepMinutes);
        FitbitAccount account = fixture.Accounts.Items.Single();
        Assert.Equal("rotated", account.RefreshToken);
        Assert.NotNull(account.LastSyncUtc);
    }

    [Fact]
    public async Task SyncUser_ReturnsNullWhenNotLinked()
    {
        Fixture fixture = new();

        Assert.Null(await fixture.Sync.SyncUserAsync(fixture.UserId));
        Assert.Empty(fixture.Client.Paths);
    }

    [Fact]
    public void IsDue_SyncsMoreOftenInTheMorning()
    {
        Fixture fixture = new();
        LifeProfile profile = new() { TimeZoneId = "UTC" };
        FitbitAccount account = new() { RefreshToken = "r", LastSyncUtc = new DateTime(2026, 10, 27, 6, 0, 0, DateTimeKind.Utc) };

        Assert.True(fixture.Sync.IsDue(account, profile, new DateTime(2026, 10, 27, 6, 25, 0, DateTimeKind.Utc)));
        account.LastSyncUtc = new DateTime(2026, 10, 27, 14, 0, 0, DateTimeKind.Utc);
        Assert.False(fixture.Sync.IsDue(account, profile, new DateTime(2026, 10, 27, 14, 25, 0, DateTimeKind.Utc)));
        Assert.True(fixture.Sync.IsDue(account, profile, new DateTime(2026, 10, 27, 16, 5, 0, DateTimeKind.Utc)));
        Assert.False(fixture.Sync.IsDue(new FitbitAccount(), profile, DateTime.UtcNow));
    }

    [Fact]
    public async Task SyncHealthTool_ReportsSyncAndSummary()
    {
        LifeTestContext test = new();
        Fixture fixture = new();
        await fixture.Accounts.SaveDocumentAsync(new FitbitAccount { UserId = test.User.Id!, RefreshToken = "r" });
        FitbitSync sync = new(fixture.Client, fixture.Accounts, test.Profiles, new FitbitClientTests.StaticOptions(new FitbitOptions()));
        SyncHealthCommand command = new(sync) { Arguments = [] };

        await command.Execute(test.Context);

        string output = ((Akagi.Characters.Conversations.CommandMessage)test.Conversation.Messages[^1]).Output;
        Assert.Contains("Synced sleep for 2 day(s)", output);
    }
}

public class RestlessNightTests
{
    private static readonly DateTime Morning = new(2026, 10, 27, 9, 0, 0, DateTimeKind.Utc);

    private static LifeProfile RestlessProfile()
    {
        LifeProfile profile = new();
        FitbitHealthMapper.ApplySleep(profile, FitbitMapperTests.SleepJson);
        // A long night that was still restless
        profile.HealthDays.Single(d => d.Date == "2026-10-27").SleepMinutes = 430;
        return profile;
    }

    [Fact]
    public void Tone_GetsProtectiveAfterARestlessNight()
    {
        LifeProfile profile = RestlessProfile();

        Akagi.Life.Mood.ToneAdvice advice = Akagi.Life.Mood.ToneAdvisor.Advise(
            Akagi.Life.Mood.MoodAnalyzer.Analyze(profile, Morning), HealthSummary.Create(profile, Morning), null, Morning);

        Assert.Equal(Akagi.Life.Mood.Tone.Gentle, advice.Tone);
        Assert.Contains(advice.Guidance, g => g.Contains("restless") && g.Contains("nap"));
    }

    [Fact]
    public void ShortSleepRoutine_AlsoReactsToRestlessNights()
    {
        Akagi.Characters.Routines.ShortSleepRoutine routine = new();
        Akagi.Characters.Routines.RoutineState state = new() { NowUtc = Morning, Profile = RestlessProfile() };

        Akagi.Characters.Routines.RoutineDecision decision = routine.Evaluate(state);
        routine.ReactToRestless = false;

        Assert.True(decision.Fire);
        Assert.Equal("restless night", decision.Reason);
        Assert.False(routine.Evaluate(state).Fire);
    }
}
