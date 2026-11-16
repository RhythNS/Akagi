using Akagi.Web.Models.Finance;
using Akagi.Web.Services.Finance;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;

namespace Akagi.Web.Tests;

/// <summary>
/// How finance records look in MongoDB, and (opt-in, AKAGI_LIVE_QUOTES=1) whether Yahoo still answers the way the
/// quote source reads it.
/// </summary>
public class FinanceStorageTests
{
    [Fact]
    public void Accounts_round_trip_as_their_own_kind()
    {
        StockAccount account = new()
        {
            Id = ObjectId.GenerateNewId().ToString(), UserId = ObjectId.GenerateNewId().ToString(), Name = "Depot",
            Securities = [new StockSecurity { Isin = "IE00B4ND3602", Name = "Gold", Symbol = "4GLD.DE", ManualPrice = 75.1234m, ManualPriceAt = DateTime.UtcNow }],
            Strategy = [new StrategyTarget { Isin = "IE00B4ND3602", Percent = 12.5m }],
            WholeShares = false
        };

        BsonDocument document = account.ToBsonDocument<FinanceAccount>();
        Assert.Equal("StockAccount", document["_t"].AsBsonArray.Last().AsString);
        Assert.False(document.Contains("Kind"));

        FinanceAccount read = BsonSerializer.Deserialize<FinanceAccount>(document);
        StockAccount stocks = Assert.IsType<StockAccount>(read);
        Assert.Equal((AccountKind.Stocks, "Depot", false), (stocks.Kind, stocks.Name, stocks.WholeShares));
        Assert.Equal(75.1234m, stocks.Securities[0].ManualPrice);
        Assert.Equal(12.5m, stocks.TargetPercent("IE00B4ND3602"));
    }

    [Fact]
    public void Trades_and_prices_keep_exact_decimals()
    {
        StockTrade trade = new()
        {
            UserId = ObjectId.GenerateNewId().ToString(), AccountId = ObjectId.GenerateNewId().ToString(), Isin = "LU0908500753",
            Side = TradeSide.Sell, Status = TradeStatus.Pending, Shares = 0.123456m, Price = 101.123456m, PlanId = "p"
        };
        StockTrade read = BsonSerializer.Deserialize<StockTrade>(trade.ToBsonDocument());
        Assert.Equal((0.123456m, 101.123456m, TradeSide.Sell, TradeStatus.Pending), (read.Shares, read.Price, read.Side, read.Status));
        Assert.False(trade.ToBsonDocument().Contains("Amount"));

        PriceHistory history = new() { Symbol = "SXR1.DE", Points = [PricePoint.At(new DateOnly(2026, 11, 13), 220.7m)] };
        PriceHistory readHistory = BsonSerializer.Deserialize<PriceHistory>(history.ToBsonDocument());
        Assert.Equal(new DateOnly(2026, 11, 13), readHistory.Points[0].Date);
    }

    /// <summary>
    /// The real database classes against a throwaway MongoDB (AKAGI_LIVE_MONGO=mongodb://127.0.0.1:27098/finance_test).
    /// </summary>
    [Fact]
    public async Task The_store_works_on_a_real_database()
    {
        string? connection = Environment.GetEnvironmentVariable("AKAGI_LIVE_MONGO");
        if (string.IsNullOrEmpty(connection))
        {
            return;
        }
        OptionsStub options = new(new Data.DatabaseOptions { ConnectionString = connection });
        FinanceStore store = new(new Data.FinanceAccountDatabase(options), new Data.StockTradeDatabase(options));
        string user = ObjectId.GenerateNewId().ToString();
        string other = ObjectId.GenerateNewId().ToString();

        FinanceAccount account = await store.SaveAccountAsync(FinanceAccount.Create(AccountKind.Stocks, user, "Depot"));
        ((StockAccount)account).Securities.Add(new StockSecurity { Isin = "IE00B4ND3602" });
        await store.SaveAccountAsync(account);
        await store.SaveAccountAsync(FinanceAccount.Create(AccountKind.Stocks, other, "Not mine"));
        await store.SaveTradeAsync(new StockTrade { UserId = user, AccountId = account.Id!, Isin = "IE00B4ND3602", Shares = 1.5m, Price = 10, ExecutedAt = DateTime.UtcNow });

        List<FinanceAccount> mine = await store.AccountsAsync(user);
        StockAccount read = Assert.IsType<StockAccount>(Assert.Single(mine));
        Assert.Equal("IE00B4ND3602", Assert.Single(read.Securities).Isin);
        Assert.Null(await store.AccountAsync(other, account.Id!));
        Assert.Equal(1.5m, Assert.Single(await store.TradesAsync(user, account.Id!)).Shares);
        Assert.Empty(await store.TradesAsync(other, account.Id!));

        await store.DeleteAccountAsync(user, account.Id!);
        Assert.Empty(await store.AccountsAsync(user));
        Assert.Empty(await store.TradesAsync(user, account.Id!));

        Data.PriceHistoryDatabase histories = new(options);
        QuoteService quotes = new(new FakeQuoteSource { Series = { ["X.DE"] = new QuoteSeries("X.DE", "X", "EUR", "XETRA", [PricePoint.At(new DateOnly(2026, 11, 6), 12.34m)]) } },
            histories, new FinanceOptionsStub(), new FakeClock(), Microsoft.Extensions.Logging.Abstractions.NullLogger<QuoteService>.Instance);
        PriceBook book = await quotes.LoadAsync(new StockAccount { Securities = [new StockSecurity { Isin = "X", Symbol = "X.DE" }] }, []);
        Assert.Equal(12.34m, book.Latest("X")!.Value.Price);
        Assert.Single(await histories.GetDocumentsAsync(), h => h.Symbol == "X.DE");
    }

    private sealed class OptionsStub(Data.DatabaseOptions value) : Microsoft.Extensions.Options.IOptionsMonitor<Data.DatabaseOptions>
    {
        public Data.DatabaseOptions CurrentValue => value;
        public Data.DatabaseOptions Get(string? name) => value;
        public IDisposable? OnChange(Action<Data.DatabaseOptions, string?> listener) => null;
    }

    private sealed class FinanceOptionsStub : Microsoft.Extensions.Options.IOptionsMonitor<FinanceOptions>
    {
        public FinanceOptions CurrentValue { get; } = new();
        public FinanceOptions Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<FinanceOptions, string?> listener) => null;
    }

    [Fact]
    public async Task Yahoo_answers_live()
    {
        if (Environment.GetEnvironmentVariable("AKAGI_LIVE_QUOTES") != "1")
        {
            return;
        }
        YahooQuoteSource source = new(new HttpClient());
        List<QuoteCandidate> found = await source.SearchAsync("IE00B4ND3602");
        Assert.NotEmpty(found);

        QuoteSeries series = await source.HistoryAsync("SXR1.DE", DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-30));
        Assert.Equal("EUR", series.Currency);
        Assert.True(series.Points.Count > 10, $"{series.Points.Count} closes");
        Assert.All(series.Points, p => Assert.True(p.Close > 0));

        QuoteSeries fx = await source.HistoryAsync("USDEUR=X", DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-10));
        Assert.True(fx.Points[^1].Close is > 0.5m and < 1.5m);

        await Assert.ThrowsAsync<QuoteException>(() => source.HistoryAsync("NOSUCHSYMBOL123.DE", DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-10)));
    }
}
