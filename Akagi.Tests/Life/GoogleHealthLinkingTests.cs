using Akagi.Bridge.Chat.Transmissions.Requests;
using Akagi.Bridge.Chat.Transmissions.Responses;
using Akagi.Communication.SocketComs.Transmissions;
using Akagi.Connectors.GoogleHealth;
using Akagi.Users;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Bson;

namespace Akagi.Tests.Life;

/// <summary>
/// Renewing the Google Health link from the website: status with the expected expiry, starting, finishing and syncing.
/// </summary>
public class GoogleHealthLinkingTests
{
    private sealed class LinkClient : IGoogleHealthClient
    {
        public bool IsConfigured { get; set; } = true;
        public string? FailCompleteWith { get; set; }

        public string StartAuthorization(GoogleHealthAccount account)
        {
            account.PendingState = "s";
            account.PendingVerifier = "v";
            return "https://accounts.google.com/o/oauth2/v2/auth?redirect_uri=https%3A%2F%2Fwww.google.com&state=s";
        }

        public Task CompleteAuthorizationAsync(GoogleHealthAccount account, string codeOrRedirectUrl, CancellationToken cancellationToken)
        {
            if (FailCompleteWith != null)
            {
                throw new GoogleHealthAuthException(FailCompleteWith);
            }
            account.RefreshToken = "new-refresh";
            account.PendingState = string.Empty;
            account.PendingVerifier = string.Empty;
            account.ConnectedAt = new DateTime(2026, 11, 10, 8, 0, 0, DateTimeKind.Utc);
            account.LastError = string.Empty;
            return Task.CompletedTask;
        }

        public Task<string> GetAsync(GoogleHealthAccount account, string path, Func<GoogleHealthAccount, Task> persist, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<string> PostJsonAsync(GoogleHealthAccount account, string path, string json, Func<GoogleHealthAccount, Task> persist, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task RevokeAsync(GoogleHealthAccount account, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class CountingSync : IGoogleHealthSync
    {
        public int Runs { get; private set; }
        public bool Fail { get; set; }

        public Task<GoogleHealthSyncResult?> SyncUserAsync(string userId, CancellationToken cancellationToken = default)
        {
            Runs++;
            if (Fail)
            {
                throw new HttpRequestException("Google Health rate limit reached; the next sync will try again.");
            }
            return Task.FromResult<GoogleHealthSyncResult?>(new GoogleHealthSyncResult(2, 3, 1));
        }

        public bool IsDue(GoogleHealthAccount account, Akagi.Life.LifeProfile profile, DateTime nowUtc) => false;
    }

    private sealed class Fixture
    {
        public LinkClient Client { get; } = new();
        public CountingSync Sync { get; } = new();
        public InMemoryGoogleHealthAccountDatabase Accounts { get; } = new();
        public User User { get; } = new() { Id = ObjectId.GenerateNewId().ToString(), Name = "Rhyth" };
        public GoogleHealthOptions Options { get; } = new();
        public GoogleHealthLinking Linking { get; }
        public GoogleHealthRequestHandler Handler { get; }

        public Fixture()
        {
            Linking = new GoogleHealthLinking(Client, Accounts, Sync, new StaticOptions(Options));
            Handler = new GoogleHealthRequestHandler(Linking, Sync, NullLogger<GoogleHealthRequestHandler>.Instance);
        }

        public Task<GoogleHealthResponseTransmission> Send(GoogleHealthAction action, string code = "") =>
            Handler.ProcessAsync(User, new GoogleHealthRequestTransmission { RequestId = "r1", Action = action, Code = code });
    }

    [Fact]
    public async Task Status_ShowsTheLinkAndWhenGoogleDropsIt()
    {
        Fixture fixture = new();
        DateTime connected = new(2026, 11, 5, 9, 30, 0, DateTimeKind.Utc);
        await fixture.Accounts.SaveDocumentAsync(new GoogleHealthAccount
        {
            UserId = fixture.User.Id!,
            RefreshToken = "r",
            ConnectedAt = connected,
            LastSyncUtc = connected.AddHours(2)
        });

        GoogleHealthResponseTransmission response = await fixture.Send(GoogleHealthAction.Status);

        Assert.Equal("r1", response.RequestId);
        Assert.Null(response.Error);
        Assert.True(response.Status.Configured);
        Assert.True(response.Status.Linked);
        Assert.Equal(connected, response.Status.LinkedAtUtc);
        Assert.Equal(connected.AddDays(7), response.Status.ExpiresAtUtc);
        Assert.Equal(connected.AddHours(2), response.Status.LastSyncUtc);
        Assert.Null(response.Status.LastError);
    }

    [Fact]
    public async Task Status_HasNoExpiryOnceTheClientIsPublished()
    {
        Fixture fixture = new();
        fixture.Options.LinkLifetimeDays = 0;
        await fixture.Accounts.SaveDocumentAsync(new GoogleHealthAccount { UserId = fixture.User.Id!, RefreshToken = "r", ConnectedAt = DateTime.UtcNow });

        Assert.Null((await fixture.Send(GoogleHealthAction.Status)).Status.ExpiresAtUtc);
    }

    [Fact]
    public async Task Status_ReportsAnExpiredLinkWithItsError()
    {
        Fixture fixture = new();
        await fixture.Accounts.SaveDocumentAsync(new GoogleHealthAccount
        {
            UserId = fixture.User.Id!,
            ConnectedAt = DateTime.UtcNow.AddDays(-8),
            LastError = "The Google Health link expired."
        });

        GoogleHealthResponseTransmission response = await fixture.Send(GoogleHealthAction.Status);

        Assert.False(response.Status.Linked);
        Assert.Null(response.Status.ExpiresAtUtc);
        Assert.Equal("The Google Health link expired.", response.Status.LastError);
    }

    [Fact]
    public async Task Renewing_StartsAndFinishesTheLinkAndSyncsOnce()
    {
        Fixture fixture = new();
        await fixture.Accounts.SaveDocumentAsync(new GoogleHealthAccount { UserId = fixture.User.Id!, RefreshToken = "old", ConnectedAt = DateTime.UtcNow.AddDays(-6) });

        GoogleHealthResponseTransmission start = await fixture.Send(GoogleHealthAction.Start);
        Assert.StartsWith("https://accounts.google.com/", start.AuthorizationUrl);
        GoogleHealthAccount pending = (await fixture.Accounts.GetForUser(fixture.User.Id!))!;
        Assert.Equal("v", pending.PendingVerifier);
        Assert.Equal("old", pending.RefreshToken);

        GoogleHealthResponseTransmission done = await fixture.Send(GoogleHealthAction.Complete, "https://www.google.com/?state=s&code=4/abc");

        Assert.Null(done.Error);
        Assert.StartsWith("Google Health linked. Synced sleep for 2 day(s)", done.Message);
        Assert.Equal(1, fixture.Sync.Runs);
        Assert.Equal("new-refresh", (await fixture.Accounts.GetForUser(fixture.User.Id!))!.RefreshToken);
        Assert.Equal(new DateTime(2026, 11, 17, 8, 0, 0, DateTimeKind.Utc), done.Status.ExpiresAtUtc);
    }

    [Fact]
    public async Task Finishing_ReportsGoogleRefusalsAndEmptyInput()
    {
        Fixture fixture = new();
        fixture.Client.FailCompleteWith = "Google refused the authorization: invalid_grant";

        Assert.Equal("Paste the address your browser landed on after allowing access.", (await fixture.Send(GoogleHealthAction.Complete, " ")).Error);
        GoogleHealthResponseTransmission refused = await fixture.Send(GoogleHealthAction.Complete, "code");

        Assert.Equal("Google refused the authorization: invalid_grant", refused.Error);
        Assert.Null(refused.Message);
        Assert.Equal(0, fixture.Sync.Runs);
    }

    [Fact]
    public async Task Finishing_KeepsTheLinkWhenOnlyTheFirstSyncFails()
    {
        Fixture fixture = new();
        fixture.Sync.Fail = true;

        GoogleHealthResponseTransmission done = await fixture.Send(GoogleHealthAction.Complete, "code");

        Assert.Null(done.Error);
        Assert.StartsWith("Google Health linked, but the first sync failed", done.Message);
        Assert.True(done.Status.Linked);
    }

    [Fact]
    public async Task Sync_ReportsWhatCameIn()
    {
        Fixture fixture = new();

        GoogleHealthResponseTransmission response = await fixture.Send(GoogleHealthAction.Sync);

        Assert.Null(response.Error);
        Assert.Contains("steps for 3 day(s)", response.Message);
    }

    [Fact]
    public async Task WithoutAnOAuthClientOnlyTheStatusAnswers()
    {
        Fixture fixture = new();
        fixture.Client.IsConfigured = false;

        GoogleHealthResponseTransmission status = await fixture.Send(GoogleHealthAction.Status);
        GoogleHealthResponseTransmission start = await fixture.Send(GoogleHealthAction.Start);

        Assert.Null(status.Error);
        Assert.False(status.Status.Configured);
        Assert.Contains("not configured", start.Error);
        Assert.Null(start.AuthorizationUrl);
    }
}
