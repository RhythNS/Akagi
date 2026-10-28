using Akagi.Connectors.GoogleHealth;
using Akagi.Life;
using Akagi.Life.Health;
using Akagi.Receivers.Commands.Life;
using Akagi.Tests.Support;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Web;

namespace Akagi.Tests.Life;

internal class InMemoryGoogleHealthAccountDatabase : InMemoryDatabase<GoogleHealthAccount>, IGoogleHealthAccountDatabase
{
    public InMemoryGoogleHealthAccountDatabase() : base("google_health_accounts")
    {
    }

    public Task<GoogleHealthAccount?> GetForUser(string userId) => Task.FromResult(Items.FirstOrDefault(a => a.UserId == userId));

    public async Task<GoogleHealthAccount> GetOrCreate(string userId) => await GetForUser(userId) ?? await Create(userId);

    public Task<List<GoogleHealthAccount>> GetConnected() => Task.FromResult(Items.Where(a => a.IsConnected).ToList());

    private async Task<GoogleHealthAccount> Create(string userId)
    {
        GoogleHealthAccount account = new() { UserId = userId };
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

internal sealed class StaticOptions(GoogleHealthOptions value) : IOptionsMonitor<GoogleHealthOptions>
{
    public GoogleHealthOptions CurrentValue => value;
    public GoogleHealthOptions Get(string? name) => value;
    public IDisposable? OnChange(Action<GoogleHealthOptions, string?> listener) => null;
}

public class GoogleHealthClientTests
{
    private const string TokenJson =
        """{"access_token":"access-2","refresh_token":"refresh-2","expires_in":3599,"token_type":"Bearer","scope":"https://www.googleapis.com/auth/googlehealth.sleep.readonly"}""";
    private const string RefreshJson = """{"access_token":"access-3","expires_in":3599,"token_type":"Bearer"}""";
    private const string IdentityJson = """{"name":"users/me/identity","legacyUserId":"ABC12","healthUserId":"111111256096816351"}""";

    internal const string NotLinkedJson = """
    {"error":{"code":400,"message":"The Google account is not linked to Google Health.","status":"FAILED_PRECONDITION",
     "details":[{"@type":"type.googleapis.com/google.rpc.ErrorInfo","reason":"ACCOUNT_NOT_LINKED","domain":"health.googleapis.com",
                 "metadata":{"redirect_uri":"https://fitbit.google.com/auth/signup"}}]}}
    """;

    private static GoogleHealthClient Client(QueueHandler handler) => new(new HttpClient(handler), new StaticOptions(new GoogleHealthOptions
    {
        ClientId = "client",
        ClientSecret = "secret",
        TokenUrl = "https://oauth2.test/token",
        RevokeUrl = "https://oauth2.test/revoke",
        ApiBaseUrl = "https://health.test/v4/"
    }));

    private static GoogleHealthAccount Linked(DateTime expires) => new()
    {
        UserId = ObjectId.GenerateNewId().ToString(),
        AccessToken = "access-1",
        RefreshToken = "refresh-1",
        ExpiresAtUtc = expires
    };

    [Theory]
    [InlineData("4/0AbC-d_e", "4/0AbC-d_e", null)]
    [InlineData("https://www.google.com/?state=xyz&code=4/0AbC-d_e&scope=https://www.googleapis.com/auth/googlehealth.sleep.readonly", "4/0AbC-d_e", "xyz")]
    [InlineData("https://www.google.com/?state=xyz&code=4%2F0AbC&scope=x#", "4/0AbC", "xyz")]
    [InlineData("code=abc123&state=s", "abc123", "s")]
    public void ExtractCode_AcceptsCodesAndRedirectAddresses(string input, string code, string? state)
    {
        Assert.Equal((code, state), GoogleHealthClient.ExtractCode(input));
    }

    [Fact]
    public void ExtractCode_ReportsADeniedConsent()
    {
        GoogleHealthAuthException ex = Assert.Throws<GoogleHealthAuthException>(() =>
            GoogleHealthClient.ExtractCode("https://www.google.com/?error=access_denied&state=xyz"));
        Assert.Contains("access_denied", ex.Message);
    }

    [Fact]
    public void StartAuthorization_AsksForOfflineAccessWithPkce()
    {
        GoogleHealthAccount account = new();

        string url = Client(new QueueHandler()).StartAuthorization(account);

        Uri uri = new(url);
        System.Collections.Specialized.NameValueCollection query = HttpUtility.ParseQueryString(uri.Query);
        string expectedChallenge = Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(account.PendingVerifier)))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        Assert.Equal("accounts.google.com", uri.Host);
        Assert.Equal("client", query["client_id"]);
        Assert.Equal("https://www.google.com", query["redirect_uri"]);
        Assert.Equal("offline", query["access_type"]);
        Assert.Equal("consent", query["prompt"]);
        Assert.Equal("S256", query["code_challenge_method"]);
        Assert.Equal(expectedChallenge, query["code_challenge"]);
        Assert.Equal(account.PendingState, query["state"]);
        Assert.Equal([GoogleHealthOptions.SleepScope, GoogleHealthOptions.ActivityScope, GoogleHealthOptions.MetricsScope], query["scope"]!.Split(' '));
    }

    [Fact]
    public async Task CompleteAuthorization_ExchangesTheCodeAndReadsTheHealthIdentity()
    {
        QueueHandler handler = new QueueHandler().Then(TokenJson).Then(IdentityJson);
        GoogleHealthClient client = Client(handler);
        GoogleHealthAccount account = new();
        client.StartAuthorization(account);
        string verifier = account.PendingVerifier;

        await client.CompleteAuthorizationAsync(account, $"https://www.google.com/?state={account.PendingState}&code=the-code&scope=x", CancellationToken.None);

        (HttpRequestMessage tokenRequest, string body) = handler.Requests[0];
        Assert.Equal("https://oauth2.test/token", tokenRequest.RequestUri!.ToString());
        Assert.Contains("grant_type=authorization_code", body);
        Assert.Contains("code=the-code", body);
        Assert.Contains("client_secret=secret", body);
        Assert.Contains($"code_verifier={verifier}", body);
        Assert.Equal("https://health.test/v4/users/me/identity", handler.Requests[1].Request.RequestUri!.ToString());
        Assert.Equal("Bearer access-2", handler.Requests[1].Request.Headers.Authorization!.ToString());
        Assert.Equal(("access-2", "refresh-2", "111111256096816351"), (account.AccessToken, account.RefreshToken, account.HealthUserId));
        Assert.Equal(GoogleHealthOptions.SleepScope, account.Scopes);
        Assert.True(account.IsConnected);
        Assert.Empty(account.PendingVerifier);
        Assert.Empty(account.LastError);
    }

    [Fact]
    public async Task CompleteAuthorization_KeepsTheTokensWhenGoogleHealthIsNotSetUp()
    {
        QueueHandler handler = new QueueHandler().Then(TokenJson).Then(NotLinkedJson, HttpStatusCode.BadRequest);
        GoogleHealthClient client = Client(handler);
        GoogleHealthAccount account = new();
        client.StartAuthorization(account);

        await client.CompleteAuthorizationAsync(account, "the-code", CancellationToken.None);

        Assert.True(account.IsConnected);
        Assert.Equal(GoogleHealthClient.NotLinkedMessage, account.LastError);
    }

    [Fact]
    public async Task CompleteAuthorization_RejectsForeignStateAndMissingStart()
    {
        GoogleHealthClient client = Client(new QueueHandler());
        GoogleHealthAccount account = new();
        client.StartAuthorization(account);

        await Assert.ThrowsAsync<GoogleHealthAuthException>(() =>
            client.CompleteAuthorizationAsync(account, "https://www.google.com/?code=c&state=other", CancellationToken.None));
        await Assert.ThrowsAsync<GoogleHealthAuthException>(() =>
            client.CompleteAuthorizationAsync(new GoogleHealthAccount(), "code", CancellationToken.None));
    }

    [Fact]
    public async Task Get_RefreshesAnExpiringTokenAndKeepsTheRefreshToken()
    {
        QueueHandler handler = new QueueHandler().Then(RefreshJson).Then("""{"dataPoints":[]}""");
        GoogleHealthAccount account = Linked(DateTime.UtcNow.AddMinutes(1));
        int persisted = 0;

        string body = await Client(handler).GetAsync(account, "users/me/dataTypes/sleep/dataPoints:reconcile?filter=x",
            _ => { persisted++; return Task.CompletedTask; }, CancellationToken.None);

        Assert.Equal("""{"dataPoints":[]}""", body);
        Assert.Equal(1, persisted);
        Assert.Contains("grant_type=refresh_token", handler.Requests[0].Body);
        Assert.Contains("refresh_token=refresh-1", handler.Requests[0].Body);
        Assert.Equal(("access-3", "refresh-1"), (account.AccessToken, account.RefreshToken));
        Assert.Equal("Bearer access-3", handler.Requests[1].Request.Headers.Authorization!.ToString());
        Assert.Equal("https://health.test/v4/users/me/dataTypes/sleep/dataPoints:reconcile?filter=x", handler.Requests[1].Request.RequestUri!.ToString());
    }

    [Fact]
    public async Task Get_RetriesOnceAfterUnauthorized()
    {
        QueueHandler handler = new QueueHandler()
            .Then("""{"error":{"code":401,"status":"UNAUTHENTICATED"}}""", HttpStatusCode.Unauthorized)
            .Then(RefreshJson)
            .Then("{}");
        GoogleHealthAccount account = Linked(DateTime.UtcNow.AddHours(1));

        await Client(handler).GetAsync(account, "users/me/identity", _ => Task.CompletedTask, CancellationToken.None);

        Assert.Equal(3, handler.Requests.Count);
        Assert.Equal("access-3", account.AccessToken);
    }

    [Fact]
    public async Task Get_DisconnectsWhenTheRefreshTokenIsRevoked()
    {
        QueueHandler handler = new QueueHandler().Then("""{"error":"invalid_grant","error_description":"Token has been expired or revoked."}""", HttpStatusCode.BadRequest);
        GoogleHealthAccount account = Linked(DateTime.UtcNow.AddMinutes(-5));
        int persisted = 0;

        await Assert.ThrowsAsync<GoogleHealthAuthException>(() =>
            Client(handler).GetAsync(account, "users/me/identity", _ => { persisted++; return Task.CompletedTask; }, CancellationToken.None));

        Assert.False(account.IsConnected);
        Assert.Contains("/health link", account.LastError);
        Assert.Equal(1, persisted);
    }

    [Fact]
    public async Task Errors_MapToMissingScopeAndNotLinked()
    {
        QueueHandler handler = new QueueHandler()
            .Then("""{"error":{"code":403,"status":"PERMISSION_DENIED","details":[{"reason":"MISSING_OAUTH_SCOPE"}]}}""", HttpStatusCode.Forbidden)
            .Then(NotLinkedJson, HttpStatusCode.BadRequest);
        GoogleHealthClient client = Client(handler);
        GoogleHealthAccount account = Linked(DateTime.UtcNow.AddHours(1));

        await Assert.ThrowsAsync<GoogleHealthMissingScopeException>(() => client.GetAsync(account, "x", _ => Task.CompletedTask, CancellationToken.None));
        GoogleHealthAuthException notLinked = await Assert.ThrowsAsync<GoogleHealthAuthException>(() => client.GetAsync(account, "x", _ => Task.CompletedTask, CancellationToken.None));

        Assert.Equal(GoogleHealthClient.NotLinkedMessage, notLinked.Message);
        Assert.True(account.IsConnected);
    }

    [Fact]
    public async Task PostJson_SendsTheBodyWithTheToken()
    {
        QueueHandler handler = new QueueHandler().Then("""{"rollupDataPoints":[]}""");
        GoogleHealthAccount account = Linked(DateTime.UtcNow.AddHours(1));

        await Client(handler).PostJsonAsync(account, "users/me/dataTypes/steps/dataPoints:dailyRollUp", """{"windowSizeDays":1}""",
            _ => Task.CompletedTask, CancellationToken.None);

        (HttpRequestMessage request, string body) = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("""{"windowSizeDays":1}""", body);
        Assert.Equal("application/json", request.Content!.Headers.ContentType!.MediaType);
        Assert.Equal("Bearer access-1", request.Headers.Authorization!.ToString());
    }

    [Fact]
    public async Task Revoke_PostsTheRefreshToken()
    {
        QueueHandler handler = new QueueHandler().Then("{}");

        await Client(handler).RevokeAsync(Linked(DateTime.UtcNow), CancellationToken.None);

        Assert.Equal("https://oauth2.test/revoke", handler.Requests[0].Request.RequestUri!.ToString());
        Assert.Equal("token=refresh-1", handler.Requests[0].Body);
    }
}

public class GoogleHealthMapperTests
{
    /// <summary>
    /// A reconcile page shaped like the Google Health docs: a stages night (mainSleep), a nap that day, and a classic
    /// night flagged "main" as in the reconcile sample, with civil times.
    /// </summary>
    internal const string SleepJson = """
    {"dataPoints":[
      {"name":"users/1/dataTypes/sleep/dataPoints/1","dataSource":{"recordingMethod":"DERIVED","platform":"FITBIT"},
       "sleep":{"interval":{"startTime":"2026-10-26T22:40:00Z","startUtcOffset":"3600s","endTime":"2026-10-27T06:00:00Z","endUtcOffset":"3600s"},
         "type":"STAGES","metadata":{"stagesStatus":"SUCCEEDED","processed":true,"mainSleep":true},
         "summary":{"minutesInSleepPeriod":"456","minutesAsleep":"392","minutesAwake":"49",
           "stagesSummary":[{"type":"AWAKE","minutes":"48","count":"12"},{"type":"LIGHT","minutes":"230","count":"19"},
                            {"type":"DEEP","minutes":"35","count":"4"},{"type":"REM","minutes":"127","count":"5"}]}}},
      {"name":"users/1/dataTypes/sleep/dataPoints/2",
       "sleep":{"interval":{"startTime":"2026-10-27T13:00:00Z","startUtcOffset":"3600s","endTime":"2026-10-27T13:30:00Z","endUtcOffset":"3600s"},
         "type":"CLASSIC","metadata":{"nap":true},"summary":{"minutesInSleepPeriod":"30","minutesAsleep":"25"}}},
      {"name":"users/1/dataTypes/sleep/dataPoints/3",
       "sleep":{"interval":{"startTime":"2026-10-25T23:00:00Z","endTime":"2026-10-26T07:00:00Z",
           "civilStartTime":{"date":{"year":2026,"month":10,"day":26},"time":{}},
           "civilEndTime":{"date":{"year":2026,"month":10,"day":26},"time":{"hours":8}}},
         "type":"CLASSIC","metadata":{"processed":true,"main":true},
         "summary":{"minutesInSleepPeriod":"480","minutesAsleep":"450","minutesAwake":"20",
           "stagesSummary":[{"type":"ASLEEP","minutes":"430"},{"type":"RESTLESS","minutes":"20"},{"type":"AWAKE","minutes":"5"}]}}}
    ],"nextPageToken":""}
    """;

    internal const string StepsJson = """
    {"rollupDataPoints":[
      {"civilStartTime":{"date":{"year":2026,"month":10,"day":27},"time":{}},"civilEndTime":{"date":{"year":2026,"month":10,"day":27},"time":{"hours":23,"minutes":59,"seconds":59}},"steps":{"countSum":"1203"}},
      {"civilStartTime":{"date":{"year":2026,"month":10,"day":26},"time":{}},"civilEndTime":{"date":{"year":2026,"month":10,"day":26},"time":{"hours":23,"minutes":59,"seconds":59}},"steps":{"countSum":"8421"}},
      {"civilStartTime":{"date":{"year":2026,"month":10,"day":25},"time":{}},"civilEndTime":{"date":{"year":2026,"month":10,"day":25},"time":{"hours":23,"minutes":59,"seconds":59}}}
    ]}
    """;

    internal const string HeartJson = """
    {"dataPoints":[
      {"name":"users/1/dataTypes/daily-resting-heart-rate/dataPoints/9",
       "dailyRestingHeartRate":{"date":{"year":2026,"month":10,"day":26},"beatsPerMinute":"58",
         "dailyRestingHeartRateMetadata":{"calculationMethod":"WITH_SLEEP"}}}
    ]}
    """;

    [Fact]
    public void Sleep_MapsMainNightStagesAndNaps()
    {
        LifeProfile profile = new() { TimeZoneId = "Europe/Berlin" };

        Assert.Equal(2, GoogleHealthMapper.ApplySleep(profile, SleepJson));

        HealthDay night = profile.HealthDays.Single(d => d.Date == "2026-10-27");
        Assert.Equal(392, night.SleepMinutes);
        Assert.Equal((35, 127, 230, 48), (night.DeepSleepMinutes, night.RemSleepMinutes, night.LightSleepMinutes, night.AwakeMinutes));
        Assert.Equal(86, night.SleepEfficiency);
        Assert.Equal(25, night.NapMinutes);
        Assert.Equal(new DateTime(2026, 10, 26, 22, 40, 0, DateTimeKind.Utc), night.SleepStartUtc);
        Assert.Equal(new DateTime(2026, 10, 27, 6, 0, 0, DateTimeKind.Utc), night.SleepEndUtc);
        Assert.Equal(GoogleHealthMapper.Source, night.Source);

        HealthDay classic = profile.HealthDays.Single(d => d.Date == "2026-10-26");
        Assert.Equal(450, classic.SleepMinutes);
        Assert.Null(classic.DeepSleepMinutes);
        Assert.Equal(5, classic.AwakeMinutes);
        Assert.Equal(94, classic.SleepEfficiency);
        Assert.Null(classic.NapMinutes);
    }

    [Fact]
    public void Sleep_WithoutCivilTimes_UsesTheRecordedOffsetForTheWakeDate()
    {
        // 23:30 UTC at UTC+9 is the next morning in Tokyo, whatever the profile's time zone says
        const string json = """
        {"dataPoints":[{"sleep":{"interval":{"startTime":"2026-10-27T15:00:00Z","endTime":"2026-10-27T22:30:00Z","endUtcOffset":"32400s"},
          "summary":{"minutesAsleep":"420"}}}]}
        """;
        LifeProfile profile = new() { TimeZoneId = "Europe/Berlin" };

        GoogleHealthMapper.ApplySleep(profile, json);

        HealthDay day = Assert.Single(profile.HealthDays);
        Assert.Equal("2026-10-28", day.Date);
        Assert.Equal(420, day.SleepMinutes);
        Assert.Equal(93, day.SleepEfficiency);
    }

    [Fact]
    public void Sleep_ReadsAllPages()
    {
        LifeProfile profile = new();
        string first = SleepJson.Replace("\"nextPageToken\":\"\"", "\"nextPageToken\":\"abc\"");

        Assert.Equal("abc", GoogleHealthMapper.NextPageToken(first));
        Assert.Null(GoogleHealthMapper.NextPageToken(SleepJson));
        Assert.Equal(1, GoogleHealthMapper.ApplySleep(profile, [
            """{"dataPoints":[]}""",
            """{"dataPoints":[{"sleep":{"interval":{"startTime":"2026-10-26T23:00:00Z","endTime":"2026-10-27T06:00:00Z","endUtcOffset":"0s"},"summary":{"minutesAsleep":"400"}}}]}"""]));
    }

    [Fact]
    public void StepsAndHeart_MapPerDayAndSkipDaysWithoutValues()
    {
        LifeProfile profile = new();

        Assert.Equal(2, GoogleHealthMapper.ApplySteps(profile, StepsJson));
        Assert.Equal(1, GoogleHealthMapper.ApplyRestingHeartRate(profile, HeartJson));

        Assert.Equal(8421, profile.HealthDays.Single(d => d.Date == "2026-10-26").Steps);
        Assert.Equal(1203, profile.HealthDays.Single(d => d.Date == "2026-10-27").Steps);
        Assert.Equal(58, profile.HealthDays.Single(d => d.Date == "2026-10-26").RestingHeartRate);
        Assert.Null(profile.HealthDays.Single(d => d.Date == "2026-10-27").RestingHeartRate);
        Assert.DoesNotContain(profile.HealthDays, d => d.Date == "2026-10-25");
    }

    [Fact]
    public void Mapping_IsIdempotent()
    {
        LifeProfile profile = new();
        GoogleHealthMapper.ApplySleep(profile, SleepJson);
        GoogleHealthMapper.ApplySleep(profile, SleepJson);

        Assert.Equal(2, profile.HealthDays.Count);
    }

    [Fact]
    public void Summary_DescribesQualityAndRestlessNights()
    {
        LifeProfile profile = new();
        GoogleHealthMapper.ApplySleep(profile, SleepJson);
        DateTime morning = new(2026, 10, 27, 9, 0, 0, DateTimeKind.Utc);

        HealthSummary summary = HealthSummary.Create(profile, morning);

        Assert.True(summary.LastNightWasRestless);
        Assert.Contains("Sleep quality: deep 35m, REM 2h07m, awake 48m, efficiency 86%, napped 25m", summary.Describe());
    }
}

public class GoogleHealthSyncTests
{
    private sealed class StubClient : IGoogleHealthClient
    {
        public List<string> Calls { get; } = [];
        public List<string> Bodies { get; } = [];
        public string SleepFirstPage { get; set; } = GoogleHealthMapperTests.SleepJson;
        public bool IsConfigured => true;
        public string StartAuthorization(GoogleHealthAccount account) => "url";
        public Task CompleteAuthorizationAsync(GoogleHealthAccount account, string codeOrRedirectUrl, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task RevokeAsync(GoogleHealthAccount account, CancellationToken cancellationToken) => Task.CompletedTask;

        public async Task<string> GetAsync(GoogleHealthAccount account, string path, Func<GoogleHealthAccount, Task> persist, CancellationToken cancellationToken)
        {
            Calls.Add("GET " + path);
            account.AccessToken = "refreshed";
            await persist(account);
            if (path.Contains("sleep"))
            {
                return path.Contains("pageToken=") ? """{"dataPoints":[]}""" : SleepFirstPage;
            }
            return GoogleHealthMapperTests.HeartJson;
        }

        public Task<string> PostJsonAsync(GoogleHealthAccount account, string path, string json, Func<GoogleHealthAccount, Task> persist, CancellationToken cancellationToken)
        {
            Calls.Add("POST " + path);
            Bodies.Add(json);
            return Task.FromResult(GoogleHealthMapperTests.StepsJson);
        }
    }

    private sealed class Fixture
    {
        public StubClient Client { get; } = new();
        public InMemoryGoogleHealthAccountDatabase Accounts { get; } = new();
        public InMemoryLifeProfileDatabase Profiles { get; } = new();
        public GoogleHealthSync Sync { get; }
        public string UserId { get; } = ObjectId.GenerateNewId().ToString();

        public Fixture()
        {
            Sync = new GoogleHealthSync(Client, Accounts, Profiles, new StaticOptions(new GoogleHealthOptions { DaysToSync = 3 }));
        }

        public Task Link(string scopes = "") =>
            Accounts.SaveDocumentAsync(new GoogleHealthAccount { UserId = UserId, RefreshToken = "r", Scopes = scopes, ExpiresAtUtc = DateTime.UtcNow.AddHours(1) });
    }

    [Fact]
    public async Task SyncUser_FillsTheLifeProfileAndSavesTheAccount()
    {
        Fixture fixture = new();
        await fixture.Link();

        GoogleHealthSyncResult? result = await fixture.Sync.SyncUserAsync(fixture.UserId);

        Assert.Equal(new GoogleHealthSyncResult(2, 2, 1), result);
        Assert.Equal(3, fixture.Client.Calls.Count);
        string sleepCall = Uri.UnescapeDataString(fixture.Client.Calls[0]);
        Assert.StartsWith("GET users/me/dataTypes/sleep/dataPoints:reconcile?filter=sleep.interval.civil_end_time >= \"", sleepCall);
        Assert.Contains("&pageSize=25", sleepCall);
        Assert.Equal("POST users/me/dataTypes/steps/dataPoints:dailyRollUp", fixture.Client.Calls[1]);
        Assert.StartsWith("GET users/me/dataTypes/daily-resting-heart-rate/dataPoints?filter=daily_resting_heart_rate.date >= \"", Uri.UnescapeDataString(fixture.Client.Calls[2]));
        Assert.Equal(392, fixture.Profiles.Items.Single().HealthDays.Single(d => d.Date == "2026-10-27").SleepMinutes);
        GoogleHealthAccount account = fixture.Accounts.Items.Single();
        Assert.Equal("refreshed", account.AccessToken);
        Assert.NotNull(account.LastSyncUtc);
        Assert.Empty(account.LastError);
    }

    [Fact]
    public async Task SyncUser_FollowsSleepPages()
    {
        Fixture fixture = new();
        await fixture.Link();
        fixture.Client.SleepFirstPage = GoogleHealthMapperTests.SleepJson.Replace("\"nextPageToken\":\"\"", "\"nextPageToken\":\"next/1\"");

        await fixture.Sync.SyncUserAsync(fixture.UserId);

        Assert.Equal(2, fixture.Client.Calls.Count(c => c.Contains("sleep")));
        Assert.Contains(fixture.Client.Calls, c => c.EndsWith("&pageToken=next%2F1", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SyncUser_SkipsDataTheUserDidNotShare()
    {
        Fixture fixture = new();
        await fixture.Link(GoogleHealthOptions.SleepScope);

        GoogleHealthSyncResult? result = await fixture.Sync.SyncUserAsync(fixture.UserId);

        Assert.Equal(new GoogleHealthSyncResult(2, 0, 0, "steps, resting heart rate"), result);
        Assert.Single(fixture.Client.Calls);
        Assert.Contains("Not shared with Akagi: steps, resting heart rate", result!.ToString());
    }

    [Fact]
    public void Requests_UseTheUsersCalendarDays()
    {
        DateTime first = new(2026, 10, 25);
        DateTime last = new(2026, 10, 27);

        Assert.Equal("sleep.interval.civil_end_time >= \"2026-10-25\" AND sleep.interval.civil_end_time < \"2026-10-28\"",
            GoogleHealthSync.CivilFilter("sleep.interval.civil_end_time", first, last));

        using JsonDocument body = JsonDocument.Parse(GoogleHealthSync.DailyRollUpBody(first, last));
        JsonElement range = body.RootElement.GetProperty("range");
        Assert.Equal(25, range.GetProperty("start").GetProperty("date").GetProperty("day").GetInt32());
        Assert.Equal(27, range.GetProperty("end").GetProperty("date").GetProperty("day").GetInt32());
        Assert.Equal(23, range.GetProperty("end").GetProperty("time").GetProperty("hours").GetInt32());
        Assert.Equal(1, body.RootElement.GetProperty("windowSizeDays").GetInt32());
    }

    [Fact]
    public async Task SyncUser_ReturnsNullWhenNotLinked()
    {
        Fixture fixture = new();

        Assert.Null(await fixture.Sync.SyncUserAsync(fixture.UserId));
        Assert.Empty(fixture.Client.Calls);
    }

    [Fact]
    public void IsDue_SyncsMoreOftenInTheMorning()
    {
        Fixture fixture = new();
        LifeProfile profile = new() { TimeZoneId = "UTC" };
        GoogleHealthAccount account = new() { RefreshToken = "r", LastSyncUtc = new DateTime(2026, 10, 27, 6, 0, 0, DateTimeKind.Utc) };

        Assert.True(fixture.Sync.IsDue(account, profile, new DateTime(2026, 10, 27, 6, 25, 0, DateTimeKind.Utc)));
        account.LastSyncUtc = new DateTime(2026, 10, 27, 14, 0, 0, DateTimeKind.Utc);
        Assert.False(fixture.Sync.IsDue(account, profile, new DateTime(2026, 10, 27, 14, 25, 0, DateTimeKind.Utc)));
        Assert.True(fixture.Sync.IsDue(account, profile, new DateTime(2026, 10, 27, 16, 5, 0, DateTimeKind.Utc)));
        Assert.False(fixture.Sync.IsDue(new GoogleHealthAccount(), profile, DateTime.UtcNow));
    }

    [Fact]
    public async Task SyncHealthTool_ReportsSyncAndSummary()
    {
        LifeTestContext test = new();
        Fixture fixture = new();
        await fixture.Accounts.SaveDocumentAsync(new GoogleHealthAccount { UserId = test.User.Id!, RefreshToken = "r" });
        GoogleHealthSync sync = new(fixture.Client, fixture.Accounts, test.Profiles, new StaticOptions(new GoogleHealthOptions()));
        SyncHealthCommand command = new(sync) { Arguments = [] };

        await command.Execute(test.Context);

        string output = ((Akagi.Characters.Conversations.CommandMessage)test.Conversation.Messages[^1]).Output;
        Assert.Contains("Synced sleep for 2 day(s)", output);
        Assert.Contains("from Google Health", output);
    }
}

public class RestlessNightTests
{
    private static readonly DateTime Morning = new(2026, 10, 27, 9, 0, 0, DateTimeKind.Utc);

    private static LifeProfile RestlessProfile()
    {
        LifeProfile profile = new();
        GoogleHealthMapper.ApplySleep(profile, GoogleHealthMapperTests.SleepJson);
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
