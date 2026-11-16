using Akagi.Web.Models.Finance;
using Akagi.Web.Services.Finance;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Akagi.Web.Tests;

/// <summary>
/// The finance system's arithmetic: ISINs, the planner (with the example from the request), holdings with average cost,
/// gains over timeframes, prices from several sources and currencies, and reading Yahoo's answers.
/// </summary>
public class FinanceTests
{
    private static readonly string[] RequestIsins =
        ["LU0908500753", "LU1900068161", "IE00BVGC6645", "IE00B4ND3602", "IE00B52MJY50", "IE00B52SF786", "IE00BZCQB185", "LU1681045024", "JE00B68GSP26"];

    [Fact]
    public void The_isins_from_the_request_are_valid_and_typos_are_caught()
    {
        Assert.All(RequestIsins, isin => Assert.Null(Isin.Problem(isin)));
        Assert.True(Isin.IsValid("US0378331005"));
        Assert.True(Isin.IsValid(" ie00 b4nd 3602 "));
        Assert.Equal("IE00B4ND3602", Isin.Normalize(" ie00 b4nd-3602 "));

        Assert.Contains("check digit", Isin.Problem("IE00B4ND3603"));
        Assert.Contains("check digit", Isin.Problem("IE00B5ND3602"));
        Assert.Contains("12 characters", Isin.Problem("IE00B4ND360"));
        Assert.Contains("two letters", Isin.Problem("1E00B4ND3602"));
    }

    [Theory]
    [InlineData("400", 400)]
    [InlineData("1.234,56", 1234.56)]
    [InlineData("1,234.56", 1234.56)]
    [InlineData("12,5", 12.5)]
    [InlineData("12.5", 12.5)]
    [InlineData("1.234.567", 1234567)]
    [InlineData("-3", -3)]
    [InlineData("100 €", 100)]
    public void Amounts_read_both_german_and_english_style(string text, double expected)
    {
        Assert.True(Money.TryParse(text, out decimal value));
        Assert.Equal((decimal)expected, value);
        Assert.False(Money.TryParse("", out _));
        Assert.False(Money.TryParse("abc", out _));
    }

    private static PlanRow Row(string isin, decimal price, decimal shares, decimal target) =>
        new() { Isin = isin, Name = isin, Price = price, Shares = shares, TargetPercent = target };

    [Fact]
    public void The_example_from_the_request_suggests_three_a_and_one_b()
    {
        // A 75 %, B 25 %, holding 3 A and 1 B at 100 € each, investing 400 €
        InvestmentPlan plan = new([Row("A", 100, 3, 75), Row("B", 100, 1, 25)], 400, wholeShares: true);

        plan.SuggestBuys();

        Assert.Equal([3m, 1m], plan.Rows.Select(r => r.Delta));
        Assert.Equal(0, plan.Leftover);
        Assert.Equal(75, plan.PercentAfter(plan.Rows[0]));
        Assert.Empty(plan.Problems());
    }

    [Fact]
    public void Selling_one_b_leaves_500_which_spend_the_rest_puts_into_a()
    {
        InvestmentPlan plan = new([Row("A", 100, 3, 75), Row("B", 100, 1, 25)], 400, wholeShares: true);
        PlanRow b = plan.Rows[1];
        b.Delta = -1;
        b.Edited = true;

        Assert.Equal(500, plan.Leftover);
        plan.Fill();

        Assert.Equal(5, plan.Rows[0].Delta);
        Assert.Equal(-1, b.Delta);
        Assert.Equal(0, plan.Leftover);
        Assert.Equal(100, plan.Sales);
        Assert.Equal(500, plan.Buys);

        List<StockTrade> trades = plan.ToTrades("u", "acc", DateTime.UtcNow);
        Assert.Equal([(TradeSide.Sell, "B", 1m), (TradeSide.Buy, "A", 5m)], trades.Select(t => (t.Side, t.Isin, t.PlannedShares)));
        Assert.All(trades, t => Assert.Equal(TradeStatus.Pending, t.Status));
        Assert.Single(trades.Select(t => t.PlanId).Distinct());
    }

    [Fact]
    public void Rebalancing_sells_what_is_over_its_target_and_buys_the_rest()
    {
        // 1 A and 3 B: B is 75 % instead of 25 %
        InvestmentPlan plan = new([Row("A", 100, 1, 75), Row("B", 100, 3, 25)], 0, wholeShares: true);

        plan.SuggestBuys();
        Assert.All(plan.Rows, r => Assert.Equal(0, r.Delta));

        plan.SuggestRebalance();
        Assert.Equal([2m, -2m], plan.Rows.Select(r => r.Delta));
        Assert.Equal(0, plan.Leftover);
    }

    [Fact]
    public void A_security_without_a_target_is_sold_completely_on_rebalance()
    {
        InvestmentPlan plan = new([Row("A", 50, 2, 100), Row("Old", 10, 7, 0)], 0, wholeShares: true);
        plan.SuggestRebalance();
        Assert.Equal(-7, plan.Rows[1].Delta);
        Assert.Equal(1, plan.Rows[0].Delta);
        Assert.Equal(20, plan.Leftover);
    }

    [Fact]
    public void Buys_only_never_add_to_what_is_already_over_target()
    {
        // A is way over; the money goes only into B and C, in their 1:1 proportion
        InvestmentPlan plan = new([Row("A", 10, 100, 50), Row("B", 10, 0, 25), Row("C", 10, 0, 25)], 200, wholeShares: false);
        plan.SuggestBuys();
        Assert.Equal([0m, 10m, 10m], plan.Rows.Select(r => r.Delta));

        InvestmentPlan fractions = new([Row("A", 30, 0, 50), Row("B", 70, 0, 50)], 100, wholeShares: false);
        fractions.SuggestBuys();
        Assert.Equal(1.6666m, fractions.Rows[0].Delta);
        Assert.Equal(0.7142m, fractions.Rows[1].Delta);
        Assert.True(fractions.Leftover is >= 0 and < 0.01m);
    }

    [Fact]
    public void Whole_shares_use_up_what_rounding_leaves_where_it_fits_best()
    {
        // 1000 € at 75/25 with prices 130 and 70: rounding down gives 5 A (650) and 3 B (210), 140 left. A sixth A
        // (780 for a 750 target) is closer than a fourth B (280 for 250 plus 100 under on A)
        InvestmentPlan plan = new([Row("A", 130, 0, 75), Row("B", 70, 0, 25)], 1000, wholeShares: true);
        plan.SuggestBuys();
        Assert.Equal([6m, 3m], plan.Rows.Select(r => r.Delta));
        Assert.Equal(10, plan.Leftover);
    }

    [Fact]
    public void Plans_that_oversell_or_overspend_are_flagged()
    {
        InvestmentPlan plan = new([Row("A", 100, 1, 50), Row("B", 100, 0, 50)], 100, wholeShares: true);
        plan.Rows[0].Delta = -2;
        plan.Rows[1].Delta = 5;
        List<string> problems = [.. plan.Problems()];
        Assert.Contains(problems, p => p.Contains("only 1 are held"));
        Assert.Contains(problems, p => p.Contains("more than the money"));
    }

    private static StockTrade Trade(string isin, TradeSide side, decimal shares, decimal price, DateTime when, decimal fees = 0) => new()
    {
        UserId = "u", AccountId = "acc", Isin = isin, Side = side, Status = TradeStatus.Done, Shares = shares, Price = price, Fees = fees,
        PlannedShares = shares, PlannedPrice = price, ExecutedAt = when, CreatedAt = when
    };

    private static DateTime Utc(int month, int day) => new(2026, month, day, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Holdings_use_average_cost_and_sales_realize_the_difference()
    {
        StockAccount account = new() { Id = "acc", UserId = "u", Securities = [new StockSecurity { Isin = "IE00B4ND3602", Name = "Gold" }] };
        List<StockTrade> trades =
        [
            Trade("IE00B4ND3602", TradeSide.Buy, 10, 100, Utc(1, 5), fees: 1),
            Trade("IE00B4ND3602", TradeSide.Buy, 10, 120, Utc(2, 5)),
            Trade("IE00B4ND3602", TradeSide.Sell, 5, 150, Utc(3, 5), fees: 1),
            new StockTrade { UserId = "u", AccountId = "acc", Isin = "IE00B4ND3602", Side = TradeSide.Buy, Status = TradeStatus.Pending, PlannedShares = 99, PlannedPrice = 1 }
        ];
        PriceBook prices = new();
        prices.Add("IE00B4ND3602", new DateOnly(2026, 11, 1), 160, PriceSource.Quote);

        StockPortfolio portfolio = new(account, trades, prices);
        Holding holding = portfolio.Holding("IE00B4ND3602");

        Assert.Equal(15, holding.Shares);
        Assert.Equal(110.05m, holding.AverageCost);
        Assert.Equal(749 - 5 * 110.05m, holding.RealizedGain);
        Assert.Equal(15 * 160, holding.Value);
        Assert.Equal(15 * 160 - 15 * 110.05m, holding.UnrealizedGain);
        Assert.Equal(1001 + 1200 - 749, portfolio.Invested);
        Assert.Equal(portfolio.Value - portfolio.Invested, holding.UnrealizedGain + holding.RealizedGain);
        Assert.Single(portfolio.Pending);
        Assert.Equal(new DateOnly(2026, 1, 5), portfolio.Start);
        Assert.Equal(10, portfolio.SharesOn("IE00B4ND3602", new DateOnly(2026, 1, 31)));
    }

    [Fact]
    public void Gains_over_a_timeframe_do_not_count_new_money_as_profit()
    {
        // 1000 in on day 1, worth 1100 by day 10, 500 more in on day 11, worth 1700 on day 20
        DateOnly d0 = new(2026, 1, 1);
        List<ValuePoint> series = [];
        for (int i = 0; i <= 20; i++)
        {
            decimal value = i switch { 0 => 0, < 10 => 1000 + i * 10, 10 => 1100, < 20 when i >= 11 => 1600, _ => 1700 };
            decimal invested = i == 0 ? 0 : i < 11 ? 1000 : 1500;
            series.Add(new ValuePoint(d0.AddDays(i), value, invested));
        }
        List<CashFlow> flows = [new(d0.AddDays(1), 1000), new(d0.AddDays(11), 500)];

        PeriodResult all = Performance.Over(series, flows, DateOnly.MinValue, d0.AddDays(20));
        Assert.Equal(200, all.Gain);
        Assert.Equal(1500, all.NetFlow);

        PeriodResult late = Performance.Over(series, flows, d0.AddDays(10), d0.AddDays(20));
        Assert.Equal(1100, late.StartValue);
        Assert.Equal(100, late.Gain);
        // Modified Dietz: 100 / (1100 + 500 * 9/10)
        Assert.Equal(Math.Round(100m / 1550 * 100, 6), Math.Round(late.Percent!.Value, 6));

        PeriodResult quiet = Performance.Over(series, flows, d0.AddDays(11), d0.AddDays(19));
        Assert.Equal(0, quiet.Gain);
    }

    [Fact]
    public void Accounts_combine_day_by_day()
    {
        DateOnly d = new(2026, 3, 1);
        AccountReport a = new()
        {
            Account = new StockAccount { Name = "A" }, Link = "a",
            Series = [new(d, 100, 100), new(d.AddDays(1), 110, 100), new(d.AddDays(2), 120, 100)],
            Flows = [new(d, 100)]
        };
        AccountReport b = new()
        {
            Account = new StockAccount { Name = "B" }, Link = "b",
            Series = [new(d.AddDays(1), 50, 50)],
            Flows = [new(d.AddDays(1), 50)]
        };
        (List<ValuePoint> series, List<CashFlow> flows) = AccountReport.Combine([a, b]);
        Assert.Equal([100m, 160m, 170m], series.Select(p => p.Value));
        Assert.Equal([100m, 150m, 150m], series.Select(p => p.Invested));
        Assert.Equal(2, flows.Count);
    }

    [Fact]
    public void The_portfolio_series_follows_shares_and_prices_day_by_day()
    {
        StockAccount account = new() { Securities = [new StockSecurity { Isin = "X" }] };
        PriceBook prices = new();
        prices.Add("X", new DateOnly(2026, 1, 5), 10, PriceSource.Trade);
        prices.Add("X", new DateOnly(2026, 1, 6), 12, PriceSource.Quote);
        prices.Add("X", new DateOnly(2026, 1, 8), 15, PriceSource.Quote);
        StockPortfolio portfolio = new(account, [Trade("X", TradeSide.Buy, 10, 10, Utc(1, 5))], prices);

        List<ValuePoint> series = portfolio.Series(new DateOnly(2026, 1, 4), new DateOnly(2026, 1, 9));
        Assert.Equal([0m, 100m, 120m, 120m, 150m, 150m], series.Select(p => p.Value));
        Assert.Equal([0m, 100m, 100m, 100m, 100m, 100m], series.Select(p => p.Invested));
    }

    [Fact]
    public void Prices_take_the_latest_day_and_prefer_typed_over_close_over_trade()
    {
        PriceBook book = new();
        DateOnly day = new(2026, 5, 4);
        book.Add("X", day, 10, PriceSource.Trade);
        book.Add("X", day, 11, PriceSource.Quote);
        Assert.Equal(11, book.On("X", day)!.Value.Price);
        book.Add("X", day, 12, PriceSource.Manual);
        book.Add("X", day, 13, PriceSource.Quote);
        Assert.Equal(12, book.On("X", day)!.Value.Price);

        book.Add("X", day.AddDays(3), 20, PriceSource.Quote);
        Assert.Equal(12, book.On("X", day.AddDays(2))!.Value.Price);
        Assert.Equal(20, book.Latest("X")!.Value.Price);
        // Before the first price: the first one
        Assert.Equal(12, book.On("X", day.AddDays(-30))!.Value.Price);
        Assert.Null(book.On("Y", day));
    }

    [Fact]
    public void Yahoo_answers_are_read_into_daily_closes()
    {
        const string chart = """
            {"chart":{"result":[{"meta":{"currency":"EUR","symbol":"SXR1.DE","fullExchangeName":"XETRA","gmtoffset":3600,"longName":"iShares Core MSCI Pac ex-Jpn"},
            "timestamp":[1793001600,1793088000,1793174400,1794816000],
            "indicators":{"quote":[{"close":[218.3000030517578,null,219.5500030517578,219.8000030517578]}]}}],"error":null}}
            """;
        QuoteSeries series = YahooQuoteSource.ParseChart("SXR1.DE", chart);
        Assert.Equal(("EUR", "XETRA", "iShares Core MSCI Pac ex-Jpn"), (series.Currency, series.Exchange, series.Name));
        Assert.Equal([218.3m, 219.55m, 219.8m], series.Points.Select(p => p.Close));
        Assert.Equal("2026-11-16", series.Points[^1].Day);

        Assert.Throws<QuoteException>(() => YahooQuoteSource.ParseChart("NOPE", """{"chart":{"result":null,"error":{"code":"Not Found","description":"No data found, symbol may be delisted"}}}"""));

        const string search = """{"quotes":[{"exchange":"PAR","quoteType":"ETF","symbol":"MEUD.PA","longname":"Amundi Core Stoxx Europe 600 UCITS ETF Acc","exchDisp":"Paris","typeDisp":"ETF"},{"index":"x"}]}""";
        QuoteCandidate candidate = Assert.Single(YahooQuoteSource.ParseSearch(search));
        Assert.Equal(new QuoteCandidate("MEUD.PA", "Amundi Core Stoxx Europe 600 UCITS ETF Acc", "Paris", "ETF"), candidate);
    }

    private static QuoteService Service(FakeQuoteSource source, FakePriceHistories histories, FakeClock clock) =>
        new(source, histories, new OptionsMonitorStub(new FinanceOptions()), clock, NullLogger<QuoteService>.Instance);

    [Fact]
    public async Task Prices_in_other_currencies_are_converted_with_the_days_rate()
    {
        FakeClock clock = new();
        FakeQuoteSource source = new();
        DateOnly d1 = new(2026, 11, 2);
        DateOnly d2 = new(2026, 11, 3);
        source.Series["CSPX.L"] = new QuoteSeries("CSPX.L", "S&P 500", "USD", "LSE", [PricePoint.At(d1, 100), PricePoint.At(d2, 110)]);
        source.Series["USDEUR=X"] = new QuoteSeries("USDEUR=X", "USD/EUR", "EUR", "CCY", [PricePoint.At(d1, 0.9m), PricePoint.At(d2, 0.8m)]);
        source.Series["IGLN.L"] = new QuoteSeries("IGLN.L", "Gold", "GBp", "LSE", [PricePoint.At(d2, 5000)]);
        source.Series["GBPEUR=X"] = new QuoteSeries("GBPEUR=X", "GBP/EUR", "EUR", "CCY", [PricePoint.At(d1, 1.2m)]);
        FakePriceHistories histories = new();
        QuoteService service = Service(source, histories, clock);
        StockAccount account = new()
        {
            Securities = [new StockSecurity { Isin = "US", Symbol = "CSPX.L" }, new StockSecurity { Isin = "GB", Symbol = "IGLN.L" }, new StockSecurity { Isin = "NONE", Symbol = "MISSING" }]
        };

        PriceBook book = await service.LoadAsync(account, []);

        Assert.Equal(90, book.On("US", d1)!.Value.Price);
        Assert.Equal(88, book.On("US", d2)!.Value.Price);
        // 5000 pence = 50 £, at 1.2 = 60 €
        Assert.Equal(60, book.Latest("GB")!.Value.Price);
        Assert.Equal("USD", book.QuoteCurrencyOf("US"));
        Assert.Contains(book.Warnings, w => w.Contains("MISSING"));
        Assert.Equal(5, source.Fetches.Count);

        // Fresh: nothing is fetched again, not even the symbol that failed
        await service.LoadAsync(account, []);
        Assert.Equal(5, source.Fetches.Count);

        // An hour later only the latest days are asked for again
        clock.Advance(3601);
        await service.LoadAsync(account, []);
        Assert.Equal(10, source.Fetches.Count);
        Assert.Equal(d2.AddDays(-7), source.Fetches.Last(f => f.Symbol == "CSPX.L").From);

        // An older trade needs history further back
        DateTime old = new(2024, 6, 3, 9, 0, 0, DateTimeKind.Utc);
        await service.LoadAsync(account, [Trade("US", TradeSide.Buy, 1, 50, old)]);
        Assert.Equal(new DateOnly(2024, 5, 27), source.Fetches.Last(f => f.Symbol == "CSPX.L").From);
    }

    private sealed class OptionsMonitorStub(FinanceOptions value) : IOptionsMonitor<FinanceOptions>
    {
        public FinanceOptions CurrentValue => value;
        public FinanceOptions Get(string? name) => value;
        public IDisposable? OnChange(Action<FinanceOptions, string?> listener) => null;
    }
}
