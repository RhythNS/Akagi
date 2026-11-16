using Akagi.Web.Models;
using Akagi.Web.Models.Finance;
using Akagi.Web.Pages.Finance;
using Akagi.Web.Services.Finance;
using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Akagi.Web.Tests;

/// <summary>
/// The finance pages: the overview, a stock account, its strategy, the investment planner with the example from the
/// request (plan, sell and reinvest, save as pending or apply), recording pending trades, and the trade details.
/// </summary>
public class FinancePageTests : BunitContext
{
    private const string UserId = "6adcc28c4f351a3450e30ab7";
    private const string AccountId = "6b0000000000000000000a01";
    private const string A = "IE00B4ND3602";
    private const string B = "LU0908500753";

    private readonly FakeClock _clock = new();
    private readonly FakeFinanceStore _store = new();
    private readonly FakeQuotes _quotes = new();

    public FinancePageTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton<TimeProvider>(_clock);
        Services.AddSingleton<IFinanceStore>(_store);
        Services.AddSingleton<IQuoteService>(_quotes);
        Services.AddSingleton<IAccountSystem>(sp => new StockAccountSystem(_store, _quotes, _clock));
        Services.AddSingleton<IAccountSystems>(sp => new AccountSystems(sp.GetServices<IAccountSystem>()));
        AddAuthorization();
    }

    /// <summary>
    /// The request's example: A targets 75 %, B 25 %, 3 A and 1 B held, both at 100 €.
    /// </summary>
    private StockAccount Seed(decimal priceA = 100)
    {
        StockAccount account = new()
        {
            Id = AccountId, UserId = UserId, Name = "ETF depot", CreatedAt = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
            Securities = [new StockSecurity { Isin = A, Name = "Share A", Symbol = "SXRA.DE" }, new StockSecurity { Isin = B, Name = "Share B", Symbol = "MEUD.PA" }],
            Strategy = [new StrategyTarget { Isin = A, Percent = 75 }, new StrategyTarget { Isin = B, Percent = 25 }]
        };
        _store.Accounts.Add(account);
        DateTime bought = new(2026, 10, 1, 9, 0, 0, DateTimeKind.Utc);
        _store.Trades.Add(Done(A, TradeSide.Buy, 3, 100, bought));
        _store.Trades.Add(Done(B, TradeSide.Buy, 1, 100, bought));
        _quotes.Close(A, new DateOnly(2026, 11, 6), priceA);
        _quotes.Close(B, new DateOnly(2026, 11, 6), 100);
        return account;
    }

    private static StockTrade Done(string isin, TradeSide side, decimal shares, decimal price, DateTime when) => new()
    {
        Id = MongoDB.Bson.ObjectId.GenerateNewId().ToString(), UserId = UserId, AccountId = AccountId, Isin = isin, Side = side,
        Status = TradeStatus.Done, Shares = shares, Price = price, PlannedShares = shares, PlannedPrice = price, ExecutedAt = when, CreatedAt = when
    };

    private IRenderedComponent<TComponent> RenderWithUser<TComponent>(Action<ComponentParameterCollectionBuilder<TComponent>>? parameters = null) where TComponent : IComponent =>
        Render<TComponent>(p =>
        {
            p.AddCascadingValue("CurrentUser", new User { Id = UserId, GoogleId = "g", Name = "Tester", Email = "t" });
            parameters?.Invoke(p);
        });

    private IRenderedComponent<TComponent> RenderAccountPage<TComponent>() where TComponent : IComponent
    {
        IRenderedComponent<TComponent> cut = RenderWithUser<TComponent>(p => p.TryAdd("Id", AccountId));
        return cut.Settled();
    }

    private static string? Value(IRenderedComponent<StockPlanPage> cut, string selector) => cut.Find(selector).GetAttribute("value");

    private static Task Change(IElement element, string value) => element.ChangeAsync(new ChangeEventArgs { Value = value });

    [Fact]
    public async Task The_planner_suggests_three_a_and_one_b_for_400()
    {
        Seed();
        IRenderedComponent<StockPlanPage> cut = RenderAccountPage<StockPlanPage>();

        await Change(cut.Find("[data-field='cash']"), "400");
        await cut.Find("[data-suggest]").ClickAsync(new());

        Assert.Equal("3", Value(cut, $"[data-delta='{A}']"));
        Assert.Equal("1", Value(cut, $"[data-delta='{B}']"));
        Assert.Equal("0,00 €", cut.Find("[data-leftover]").TextContent.Trim());
        Assert.Equal("400,00 €", cut.Find("[data-buys]").TextContent.Trim());
        Assert.Contains("6", cut.Find($"[data-after='{A}']").TextContent);
    }

    [Fact]
    public async Task Selling_b_and_spending_the_rest_puts_500_into_a_and_saves_pending_trades()
    {
        Seed();
        IRenderedComponent<StockPlanPage> cut = RenderAccountPage<StockPlanPage>();

        await Change(cut.Find("[data-field='cash']"), "400");
        await Change(cut.Find($"[data-delta='{B}']"), "-1");
        Assert.Equal("500,00 €", cut.Find("[data-leftover]").TextContent.Trim());
        Assert.Equal("100,00 €", cut.Find("[data-sales]").TextContent.Trim());

        await cut.Find("[data-fill]").ClickAsync(new());
        Assert.Equal("5", Value(cut, $"[data-delta='{A}']"));
        Assert.Equal("-1", Value(cut, $"[data-delta='{B}']"));
        Assert.Equal("0,00 €", cut.Find("[data-leftover]").TextContent.Trim());

        await cut.Find("[data-save-pending]").ClickAsync(new());

        List<StockTrade> pending = [.. _store.Trades.Where(t => t.Status == TradeStatus.Pending)];
        Assert.Equal([(B, TradeSide.Sell, 1m, 100m), (A, TradeSide.Buy, 5m, 100m)], pending.Select(t => (t.Isin, t.Side, t.PlannedShares, t.PlannedPrice)));
        cut.WaitForAssertion(() => Assert.Contains("2 pending trades", cut.Find("[data-plan-done]").TextContent));
        Assert.Equal(2, cut.FindAll("[data-pending-trade]").Count);
        // The holdings are unchanged until the trades are recorded
        Assert.Equal("0", Value(cut, $"[data-delta='{A}']"));
        Assert.Contains("3", cut.Find($"[data-after='{A}']").TextContent);
    }

    [Fact]
    public async Task Selling_more_than_is_held_cannot_be_saved()
    {
        Seed();
        IRenderedComponent<StockPlanPage> cut = RenderAccountPage<StockPlanPage>();
        await Change(cut.Find($"[data-delta='{B}']"), "-2");
        Assert.Contains("only 1 are held", cut.Find("[data-plan-problem]").TextContent);
        Assert.True(cut.Find("[data-save-pending]").HasAttribute("disabled"));
    }

    [Fact]
    public async Task Apply_records_the_exact_amounts_and_time()
    {
        Seed();
        IRenderedComponent<StockPlanPage> cut = RenderAccountPage<StockPlanPage>();
        await Change(cut.Find("[data-field='cash']"), "400");
        await cut.Find("[data-suggest]").ClickAsync(new());
        await cut.Find("[data-apply]").ClickAsync(new());

        await Change(cut.Find("[data-field='apply-time']"), "2026-11-08T10:15");
        await Change(cut.Find($"[data-apply-total='{A}']"), "301,50");
        await Change(cut.Find($"[data-apply-fees='{A}']"), "1");
        await cut.Find("[data-apply-save]").ClickAsync(new());

        StockTrade a = Assert.Single(_store.Trades, t => t.Isin == A && t.PlanId != null);
        Assert.Equal((TradeStatus.Done, 3m, 100.5m, 1m), (a.Status, a.Shares, a.Price, a.Fees));
        Assert.Equal(new DateTime(2026, 11, 8, 10, 15, 0, DateTimeKind.Local).ToUniversalTime(), a.ExecutedAt);
        StockTrade b = Assert.Single(_store.Trades, t => t.Isin == B && t.PlanId != null);
        Assert.Equal((TradeStatus.Done, 1m, 100m), (b.Status, b.Shares, b.Price));
        cut.WaitForAssertion(() => Assert.Contains("Recorded 2 trades", cut.Find("[data-plan-done]").TextContent));
        Assert.Contains("6", cut.Find($"[data-after='{A}']").TextContent);
    }

    [Fact]
    public async Task A_pending_trade_is_recorded_later_with_what_went_through()
    {
        Seed();
        _store.Trades.Add(new StockTrade
        {
            Id = "6b0000000000000000000b01", UserId = UserId, AccountId = AccountId, Isin = A, Side = TradeSide.Buy, Status = TradeStatus.Pending,
            PlannedShares = 2, PlannedPrice = 100, Shares = 2, Price = 100, PlanId = "p1", CreatedAt = _clock.UtcNow.AddDays(-1)
        });
        IRenderedComponent<StockAccountPage> cut = RenderAccountPage<StockAccountPage>();

        Assert.Equal("3", cut.Find($"[data-holding='{A}'] [data-shares]").TextContent);
        await cut.Find("[data-record='6b0000000000000000000b01']").ClickAsync(new());
        await Change(cut.Find("[data-trade-form='record'] [data-field='total']"), "205");
        await cut.Find("[data-trade-form='record'] [data-trade-save]").ClickAsync(new());

        StockTrade trade = Assert.Single(_store.Trades, t => t.Id == "6b0000000000000000000b01");
        Assert.Equal((TradeStatus.Done, 2m, 102.5m, 100m), (trade.Status, trade.Shares, trade.Price, trade.PlannedPrice));
        Assert.NotNull(trade.ExecutedAt);
        cut.WaitForAssertion(() => Assert.Equal("5", cut.Find($"[data-holding='{A}'] [data-shares]").TextContent));
        Assert.Empty(cut.FindAll("[data-pending-trade]"));
    }

    [Fact]
    public async Task Past_trades_are_entered_with_amount_and_time()
    {
        Seed();
        IRenderedComponent<StockAccountPage> cut = RenderAccountPage<StockAccountPage>();
        await cut.Find("[data-new-trade]").ClickAsync(new());
        await Change(cut.Find("[data-field='security']"), B);
        await Change(cut.Find("[data-field='time']"), "2026-03-02T09:30");
        await Change(cut.Find("[data-field='shares']"), "4");
        await Change(cut.Find("[data-field='total']"), "350");
        await cut.Find("[data-trade-save]").ClickAsync(new());

        StockTrade trade = Assert.Single(_store.Trades, t => t.Isin == B && t.Shares == 4);
        Assert.Equal((TradeSide.Buy, TradeStatus.Done, 87.5m), (trade.Side, trade.Status, trade.Price));
        Assert.Equal(new DateTime(2026, 3, 2, 9, 30, 0, DateTimeKind.Local).ToUniversalTime(), trade.ExecutedAt);
        cut.WaitForAssertion(() => Assert.Equal("5", cut.Find($"[data-holding='{B}'] [data-shares]").TextContent));

        // Selling more than is held is refused
        await cut.Find("[data-new-trade]").ClickAsync(new());
        await Change(cut.Find("[data-field='security']"), A);
        await Change(cut.Find("[data-field='side']"), "Sell");
        await Change(cut.Find("[data-field='shares']"), "4");
        await cut.Find("[data-trade-save]").ClickAsync(new());
        Assert.Contains("Only 3 shares", cut.Find("[data-trade-error]").TextContent);
    }

    [Fact]
    public async Task Isins_are_added_with_a_looked_up_listing_and_bad_ones_explained()
    {
        Seed();
        IRenderedComponent<StockAccountPage> cut = RenderAccountPage<StockAccountPage>();

        await Change(cut.Find("[data-field='isins']"), "IE00BVGC6645, JE00B68GSP26\nIE00B4ND3602 XX123 IE00B52SF787");
        await cut.Find("[data-add-isins]").ClickAsync(new());

        StockAccount account = (StockAccount)_store.Accounts.Single();
        Assert.Equal([A, B, "IE00BVGC6645", "JE00B68GSP26"], account.Securities.Select(s => s.Isin));
        Assert.Equal(("IE00.DE", "Fund IE00BVGC6645"), (account.Securities[2].Symbol, account.Securities[2].Name));
        Assert.Equal(string.Empty, account.Securities[3].Symbol);
        string message = cut.Find("[data-add-result]").TextContent;
        Assert.Contains("Added 2", message);
        Assert.Contains("No listing found for JE00B68GSP26", message);
        Assert.Contains("IE00B4ND3602 is already in the account", message);
        Assert.Contains("XX123", message);
        Assert.Contains("IE00B52SF787: The check digit", message);
        Assert.Equal(4, cut.FindAll("[data-holding]").Count);
    }

    [Fact]
    public async Task A_security_gets_a_symbol_and_a_typed_price()
    {
        Seed();
        IRenderedComponent<StockAccountPage> cut = RenderAccountPage<StockAccountPage>();
        await cut.Find($"[data-edit-security='{B}']").ClickAsync(new());
        await Change(cut.Find("[data-security-editor] [data-field='symbol']"), "meud.de");
        await cut.Find("[data-security-editor] [data-check]").ClickAsync(new());
        Assert.Contains("Checked fund on XETRA", cut.Find("[data-check-result]").TextContent);
        await Change(cut.Find("[data-security-editor] [data-field='manual-price']"), "120,5");
        await cut.Find("[data-security-save]").ClickAsync(new());

        StockSecurity security = ((StockAccount)_store.Accounts.Single()).Security(B)!;
        Assert.Equal(("MEUD.DE", 120.5m), (security.Symbol, security.ManualPrice));
        cut.WaitForAssertion(() => Assert.Equal("120,50 €", cut.Find($"[data-holding='{B}'] [data-price]").TextContent));
        Assert.Contains("420,50 €", cut.Find("[data-value]").TextContent);
    }

    [Fact]
    public async Task The_strategy_is_saved_and_off_targets_are_warned_about()
    {
        Seed();
        IRenderedComponent<StockStrategyPage> cut = RenderAccountPage<StockStrategyPage>();
        Assert.Equal("100,0 %", cut.Find("[data-sum]").TextContent);

        await Change(cut.Find($"[data-target='{A}']"), "60");
        Assert.Contains("85,0 %", cut.Find("[data-sum-warning]").TextContent);
        await Change(cut.Find($"[data-target='{B}']"), "40");
        await cut.Find("[data-field='whole-shares']").ChangeAsync(new ChangeEventArgs { Value = false });
        await cut.Find("[data-save-strategy]").ClickAsync(new());

        StockAccount account = (StockAccount)_store.Accounts.Single();
        Assert.Equal((60m, 40m, false), (account.TargetPercent(A), account.TargetPercent(B), account.WholeShares));
        Assert.NotNull(cut.Find("[data-strategy-saved]"));

        await cut.Find("[data-even]").ClickAsync(new());
        Assert.Equal("50", cut.Find($"[data-target='{A}']").GetAttribute("value"));
        await cut.Find("[data-from-current]").ClickAsync(new());
        Assert.Equal("75", cut.Find($"[data-target='{A}']").GetAttribute("value"));
    }

    [Fact]
    public void The_overview_adds_up_accounts_and_shows_gains_per_timeframe()
    {
        Seed(priceA: 110);
        IRenderedComponent<FinanceOverviewPage> cut = RenderWithUser<FinanceOverviewPage>().Settled();

        cut.WaitForAssertion(() => Assert.Equal("430,00 €", cut.Find("[data-total-value]").TextContent));
        Assert.Equal("400,00 €", cut.Find("[data-total-invested]").TextContent);
        Assert.Equal("+30,00 €", cut.Find("[data-total-gain]").TextContent);
        Assert.Equal(Timeframe.Presets.Count, cut.FindAll("[data-timeframe]").Count);
        // Bought on Oct 1 and A rose on Nov 6: a week ago it was still worth what went in
        Assert.Equal("+30,00 €", cut.Find("[data-timeframe='1w'] [data-made]").TextContent);
        Assert.Equal("+30,00 €", cut.Find("[data-timeframe='all'] [data-made]").TextContent);
        IElement row = cut.Find($"[data-account='{AccountId}']");
        Assert.Contains("ETF depot", row.TextContent);
        Assert.Contains("2 of 2 securities held", row.TextContent);
        Assert.Equal($"Finance/Stocks/{AccountId}", row.QuerySelector("a")!.GetAttribute("href"));
        Assert.Empty(cut.FindAll("[data-chart-empty]"));
    }

    [Fact]
    public async Task A_new_account_is_created_from_the_overview()
    {
        IRenderedComponent<FinanceOverviewPage> cut = RenderWithUser<FinanceOverviewPage>().Settled();
        Assert.NotNull(cut.Find("[data-create]"));

        await cut.Find("[data-create-save]").ClickAsync(new());
        Assert.Contains("name", cut.Find("[data-create-error]").TextContent);

        await cut.Find("[data-field='account-name']").InputAsync(new ChangeEventArgs { Value = "Depot" });
        await cut.Find("[data-create-save]").ClickAsync(new());

        FinanceAccount account = Assert.Single(_store.Accounts);
        Assert.Equal(("Depot", AccountKind.Stocks, UserId), (account.Name, account.Kind, account.UserId));
        Assert.EndsWith($"Finance/Stocks/{account.Id}", Services.GetRequiredService<NavigationManager>().Uri);
    }

    [Fact]
    public void The_trades_page_shows_each_security_and_the_log()
    {
        Seed(priceA: 110);
        _store.Trades.Add(Done(A, TradeSide.Sell, 1, 120, new DateTime(2026, 11, 2, 9, 0, 0, DateTimeKind.Utc)));
        IRenderedComponent<StockTradesPage> cut = RenderAccountPage<StockTradesPage>();

        Assert.Equal(3, cut.FindAll("[data-trade]").Count);
        Assert.Equal("3", cut.Find("[data-count]").TextContent);
        Assert.Equal("400,00 €", cut.Find("[data-bought]").TextContent);
        Assert.Equal("120,00 €", cut.Find("[data-sold]").TextContent);
        // A: sold 1 bought at 100 for 120
        Assert.Contains("+20,00 €", cut.Find($"[data-security-row='{A}']").TextContent);
    }

    [Fact]
    public async Task One_security_shows_its_price_chart_with_the_trades()
    {
        Seed(priceA: 110);
        _store.Trades.Add(Done(A, TradeSide.Sell, 1, 120, new DateTime(2026, 11, 2, 9, 0, 0, DateTimeKind.Utc)));
        Services.GetRequiredService<NavigationManager>().NavigateTo($"Finance/Stocks/{AccountId}/Trades?isin={A}");
        IRenderedComponent<StockTradesPage> cut = RenderWithUser<StockTradesPage>(p => p.TryAdd("Id", AccountId)).Settled();

        IElement detail = cut.Find($"[data-security-detail='{A}']");
        Assert.Equal("+20,00 €", detail.QuerySelector("[data-realized]")!.TextContent);
        // 2 left at 110 bought at 100, plus 20 from the sale
        Assert.Equal("+40,00 €", detail.QuerySelector("[data-total]")!.TextContent);
        Assert.Equal(2, cut.FindAll("[data-marker]").Count);
        Assert.Equal(2, cut.FindAll("[data-trade]").Count);

        await Change(cut.Find("[data-filter='side']"), "Sell");
        Assert.Single(cut.FindAll("[data-trade]"));
    }

    [Fact]
    public async Task A_trade_is_edited_right_under_its_row()
    {
        Seed();
        StockTrade sale = Done(A, TradeSide.Sell, 1, 120, new DateTime(2026, 11, 2, 9, 0, 0, DateTimeKind.Utc));
        _store.Trades.Add(sale);
        IRenderedComponent<StockTradesPage> cut = RenderAccountPage<StockTradesPage>();
        Assert.Empty(cut.FindAll("[data-trade-form]"));

        await cut.Find($"[data-edit-trade='{sale.Id}']").ClickAsync();

        IElement row = cut.Find($"[data-trade='{sale.Id}']");
        Assert.Equal($"{sale.Id}", row.NextElementSibling!.GetAttribute("data-edit-row"));
        Assert.Single(cut.FindAll("[data-trade-form]"));
        Assert.Equal("1", cut.Find("[data-trade-form] [data-field='shares']").GetAttribute("value"));

        await Change(cut.Find("[data-trade-form] [data-field='shares']"), "2");
        await cut.Find("[data-trade-save]").ClickAsync();
        cut.Settled();

        Assert.Empty(cut.FindAll("[data-trade-form]"));
        Assert.Equal(2, _store.Trades.Single(t => t.Id == sale.Id).Shares);
    }

    [Fact]
    public async Task Securities_sold_completely_are_hidden_until_asked_for()
    {
        StockAccount account = Seed();
        const string old = "IE00B52SF786";
        account.Securities.Add(new StockSecurity { Isin = old, Name = "Old fund" });
        _store.Trades.Add(Done(old, TradeSide.Buy, 2, 50, new DateTime(2026, 9, 2, 9, 0, 0, DateTimeKind.Utc)));
        _store.Trades.Add(Done(old, TradeSide.Sell, 2, 60, new DateTime(2026, 9, 9, 9, 0, 0, DateTimeKind.Utc)));

        IRenderedComponent<StockAccountPage> holdings = RenderAccountPage<StockAccountPage>();
        Assert.Empty(holdings.FindAll($"[data-holding='{old}']"));
        Assert.Contains("Show 1 sold out (Old fund)", holdings.Find("[data-show-sold-out]").TextContent);
        await holdings.Find("[data-show-sold-out]").ClickAsync();
        Assert.Single(holdings.FindAll($"[data-holding='{old}']"));

        IRenderedComponent<StockPlanPage> plan = RenderAccountPage<StockPlanPage>();
        Assert.Empty(plan.FindAll($"[data-plan-row='{old}']"));

        IRenderedComponent<StockStrategyPage> strategy = RenderAccountPage<StockStrategyPage>();
        Assert.Empty(strategy.FindAll($"[data-target-row='{old}']"));
        await strategy.Find("[data-even]").ClickAsync();
        Assert.Equal("50", strategy.Find($"[data-target='{A}']").GetAttribute("value"));
    }
}
