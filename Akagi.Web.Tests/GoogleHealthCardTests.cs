using Akagi.Bridge.Chat.Models;
using Akagi.Bridge.Chat.Transmissions.Requests;
using Akagi.Bridge.Chat.Transmissions.Responses;
using Akagi.Web.Models;
using Akagi.Web.Pages.Health;
using Akagi.Web.Services.Sockets;
using Akagi.Web.Services.Sockets.Requests;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net;

namespace Akagi.Web.Tests;

/// <summary>
/// The dashboard card that shows when the Google Health link runs out and renews it without chat commands.
/// </summary>
public class GoogleHealthCardTests : BunitContext
{
    private sealed class Sockets : ISocketClientContainer
    {
        public SocketClient? Client { get; init; }
        public bool IsConnected => Client != null;
#pragma warning disable CS0067
        public event Action? ConnectionStateChanged;
#pragma warning restore CS0067
        public Task<SocketClient?> EnsureConnectedAsync() => Task.FromResult(Client);
        public Task TryConnectAsync() => Task.CompletedTask;
        public Task TryDisconnectAsync() => Task.CompletedTask;
    }

    private const string GoogleConsent = "https://accounts.google.com/o/oauth2/v2/auth?client_id=c&redirect_uri=https%3A%2F%2Fwww.google.com&state=s";

    private readonly SocketClient _client;
    private readonly HashSet<IRequest> _answered = [];

    public GoogleHealthCardTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        _client = new SocketClient(IPAddress.Loopback, 1, new User { GoogleId = "g", Name = "Tester", Email = "t" }, "circuit",
            NullLoggerFactory.Instance, [], null!);
        GC.SuppressFinalize(_client);
        Services.AddSingleton<ISocketClientContainer>(new Sockets { Client = _client });
    }

    private GoogleHealthRequestTransmission Respond(Func<GoogleHealthRequestTransmission, GoogleHealthResponseTransmission> answer)
    {
        GoogleHealthRequest? request = null;
        for (int i = 0; i < 300 && request == null; i++)
        {
            request = _client.GetRequests<GoogleHealthRequest>().FirstOrDefault(r => !_answered.Contains(r));
            if (request == null)
            {
                Thread.Sleep(10);
            }
        }
        Assert.NotNull(request);
        _answered.Add(request);
        request.Fulfill(answer(request.Transmission));
        return request.Transmission;
    }

    private static GoogleHealthStatus Linked(TimeSpan left) => new()
    {
        Configured = true,
        Linked = true,
        LinkedAtUtc = DateTime.UtcNow + left - TimeSpan.FromDays(7),
        ExpiresAtUtc = DateTime.UtcNow + left,
        LastSyncUtc = DateTime.UtcNow.AddHours(-1)
    };

    private IRenderedComponent<GoogleHealthCard> Open(GoogleHealthStatus status)
    {
        IRenderedComponent<GoogleHealthCard> cut = Render<GoogleHealthCard>();
        Assert.Equal(GoogleHealthAction.Status, Respond(_ => new GoogleHealthResponseTransmission { Status = status }).Action);
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("[data-health-state]")));
        return cut.Settled();
    }

    [Fact]
    public void A_fresh_link_shows_when_to_renew()
    {
        IRenderedComponent<GoogleHealthCard> cut = Open(Linked(TimeSpan.FromDays(5).Add(TimeSpan.FromHours(3))));

        Assert.Equal("Ok", cut.Find("[data-health-state]").GetAttribute("data-health-state"));
        Assert.Equal("Google Health linked", cut.Find("[data-health-headline]").TextContent);
        Assert.Contains("(5 days left)", cut.Find("[data-health-detail]").TextContent);
        Assert.Contains("Renew link", cut.Find("[data-health-renew]").TextContent);
    }

    [Fact]
    public void A_link_about_to_run_out_warns()
    {
        IRenderedComponent<GoogleHealthCard> cut = Open(Linked(TimeSpan.FromHours(20.5)));

        Assert.Equal("Soon", cut.Find("[data-health-state]").GetAttribute("data-health-state"));
        Assert.Equal("Google Health link runs out in 20 hours", cut.Find("[data-health-headline]").TextContent);
    }

    [Fact]
    public async Task Renewing_opens_google_and_a_pasted_address_finishes_the_link()
    {
        IRenderedComponent<GoogleHealthCard> cut = Open(new GoogleHealthStatus { Configured = true, LastError = "The Google Health link expired." });
        Assert.Equal("Expired", cut.Find("[data-health-state]").GetAttribute("data-health-state"));
        Assert.Empty(cut.FindAll("[data-health-sync]"));

        Task click = cut.Find("[data-health-renew]").ClickAsync(new());
        Assert.Equal(GoogleHealthAction.Start, Respond(r => new GoogleHealthResponseTransmission
        {
            Status = new GoogleHealthStatus { Configured = true, LastError = "The Google Health link expired." },
            AuthorizationUrl = GoogleConsent
        }).Action);
        await click;

        cut.WaitForAssertion(() => Assert.Equal(GoogleConsent, cut.Find("[data-health-consent]").GetAttribute("href")));
        Assert.Equal("_blank", cut.Find("[data-health-consent]").GetAttribute("target"));
        Assert.Empty(cut.FindAll("[data-health-renew]"));

        Task paste = cut.Find("[data-health-code]").InputAsync(new ChangeEventArgs { Value = "https://www.google.com/?state=s&code=4/abc&scope=x" });
        GoogleHealthRequestTransmission complete = Respond(r => new GoogleHealthResponseTransmission
        {
            Status = Linked(TimeSpan.FromDays(7)),
            Message = "Google Health linked. Synced sleep for 2 day(s)."
        });
        await paste;

        Assert.Equal(GoogleHealthAction.Complete, complete.Action);
        Assert.Equal("https://www.google.com/?state=s&code=4/abc&scope=x", complete.Code);
        cut.WaitForAssertion(() => Assert.Equal("Google Health linked. Synced sleep for 2 day(s).", cut.Find("[data-health-message]").TextContent));
        Assert.Empty(cut.FindAll("[data-health-steps]"));
        Assert.Equal("Ok", cut.Find("[data-health-state]").GetAttribute("data-health-state"));
    }

    [Fact]
    public async Task A_refused_code_keeps_the_steps_open_with_the_error()
    {
        IRenderedComponent<GoogleHealthCard> cut = Open(Linked(TimeSpan.FromDays(1)));
        Task click = cut.Find("[data-health-renew]").ClickAsync(new());
        Respond(_ => new GoogleHealthResponseTransmission { Status = Linked(TimeSpan.FromDays(1)), AuthorizationUrl = GoogleConsent });
        await click;
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("[data-health-code]")));

        await cut.Find("[data-health-code]").InputAsync(new ChangeEventArgs { Value = "4/abc" });
        Task finish = cut.Find("[data-health-finish]").ClickAsync(new());
        Respond(_ => new GoogleHealthResponseTransmission { Status = Linked(TimeSpan.FromDays(1)), Error = "Google refused the authorization: invalid_grant" });
        await finish;

        cut.WaitForAssertion(() => Assert.Contains("invalid_grant", cut.Find("[data-health-error]").TextContent));
        Assert.NotEmpty(cut.FindAll("[data-health-steps]"));
    }

    [Fact]
    public async Task When_google_returns_to_this_site_nothing_has_to_be_pasted()
    {
        IRenderedComponent<GoogleHealthCard> cut = Open(Linked(TimeSpan.FromDays(1)));
        string here = Services.GetRequiredService<NavigationManager>().BaseUri + GoogleHealthView.CallbackPath;

        Task click = cut.Find("[data-health-renew]").ClickAsync(new());
        Respond(_ => new GoogleHealthResponseTransmission
        {
            Status = Linked(TimeSpan.FromDays(1)),
            AuthorizationUrl = "https://accounts.google.com/o/oauth2/v2/auth?redirect_uri=" + Uri.EscapeDataString(here) + "&state=s"
        });
        await click;

        cut.WaitForAssertion(() => Assert.Contains("brings you back", cut.Find("[data-health-steps]").TextContent));
        Assert.Empty(cut.FindAll("[data-health-code]"));
    }

    [Fact]
    public void States_follow_the_expiry()
    {
        DateTime now = new(2026, 11, 10, 12, 0, 0, DateTimeKind.Utc);
        GoogleHealthStatus status = new() { Configured = true, Linked = true, ExpiresAtUtc = now.AddDays(3) };

        Assert.Equal(GoogleHealthState.Ok, GoogleHealthView.State(status, now));
        Assert.Equal(GoogleHealthState.Soon, GoogleHealthView.State(status, now.AddDays(1.5)));
        Assert.Equal(GoogleHealthState.Expired, GoogleHealthView.State(status, now.AddDays(3)));
        Assert.Equal(GoogleHealthState.Ok, GoogleHealthView.State(new GoogleHealthStatus { Configured = true, Linked = true }, now));
        Assert.Equal(GoogleHealthState.NotLinked, GoogleHealthView.State(new GoogleHealthStatus { Configured = true }, now));
        Assert.Equal(GoogleHealthState.NotConfigured, GoogleHealthView.State(new GoogleHealthStatus(), now));
        Assert.Equal("less than an hour", GoogleHealthView.Remaining(TimeSpan.FromMinutes(40)));
        Assert.Equal("1 hour", GoogleHealthView.Remaining(TimeSpan.FromMinutes(90)));
        Assert.Equal("47 hours", GoogleHealthView.Remaining(TimeSpan.FromHours(47.9)));
        Assert.Equal("2 days", GoogleHealthView.Remaining(TimeSpan.FromHours(48)));
        Assert.True(GoogleHealthView.LooksComplete("https://www.google.com/?state=s&code=4/x"));
        Assert.False(GoogleHealthView.LooksComplete("4/x"));
    }
}
