using Akagi.Web.Data;
using Akagi.Web.Models.Finance;
using Akagi.Web.Services.Finance;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;

namespace Akagi.Web.Tests;

internal sealed class FakeFinanceStore : IFinanceStore
{
    public List<FinanceAccount> Accounts { get; } = [];
    public List<StockTrade> Trades { get; } = [];

    private static string NewId() => ObjectId.GenerateNewId().ToString();

    public Task<List<FinanceAccount>> AccountsAsync(string userId) => Task.FromResult(Accounts.Where(a => a.UserId == userId).ToList());
    public Task<FinanceAccount?> AccountAsync(string userId, string id) => Task.FromResult(Accounts.FirstOrDefault(a => a.Id == id && a.UserId == userId));
    public Task<FinanceAccount> SaveAccountAsync(FinanceAccount account)
    {
        account.Id ??= NewId();
        if (!Accounts.Contains(account))
        {
            Accounts.RemoveAll(a => a.Id == account.Id);
            Accounts.Add(account);
        }
        return Task.FromResult(account);
    }
    public Task DeleteAccountAsync(string userId, string id)
    {
        Accounts.RemoveAll(a => a.Id == id && a.UserId == userId);
        Trades.RemoveAll(t => t.AccountId == id);
        return Task.CompletedTask;
    }
    public Task<List<StockTrade>> TradesAsync(string userId, string accountId) =>
        Task.FromResult(Trades.Where(t => t.UserId == userId && t.AccountId == accountId).Select(t => t.Copy()).OrderBy(t => t.ExecutedAt ?? t.CreatedAt).ToList());
    public Task<StockTrade?> TradeAsync(string userId, string id) => Task.FromResult(Trades.FirstOrDefault(t => t.Id == id && t.UserId == userId)?.Copy());
    public Task<StockTrade> SaveTradeAsync(StockTrade trade)
    {
        trade.Id ??= NewId();
        Trades.RemoveAll(t => t.Id == trade.Id);
        Trades.Add(trade.Copy());
        return Task.FromResult(trade);
    }
    public Task DeleteTradeAsync(string userId, string id)
    {
        Trades.RemoveAll(t => t.Id == id && t.UserId == userId);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Prices without the network: fixed closes per ISIN, plus trade and hand-typed prices like the real service.
/// </summary>
internal sealed class FakeQuotes : IQuoteService
{
    public Dictionary<string, List<PricePoint>> Closes { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> Searched { get; } = [];
    public bool Enabled { get; set; } = true;

    public void Close(string isin, DateOnly day, decimal price)
    {
        if (!Closes.TryGetValue(isin, out List<PricePoint>? list))
        {
            Closes[isin] = list = [];
        }
        list.Add(PricePoint.At(day, price));
    }

    public Task<List<QuoteCandidate>> SearchAsync(string query)
    {
        Searched.Add(query);
        return Task.FromResult(query.StartsWith("JE", StringComparison.Ordinal)
            ? new List<QuoteCandidate>()
            : [new QuoteCandidate($"{query[..4]}.DE", $"Fund {query}", "XETRA", "ETF")]);
    }

    public Task<QuoteSeries> CheckAsync(string symbol) =>
        Task.FromResult(new QuoteSeries(symbol, "Checked fund", "EUR", "XETRA", [PricePoint.At(new DateOnly(2026, 11, 6), 42m)]));

    public Task<PriceBook> LoadAsync(StockAccount account, IReadOnlyList<StockTrade> trades, bool refresh = true, bool force = false)
    {
        PriceBook book = new();
        foreach (StockTrade trade in trades.Where(t => t.Status == TradeStatus.Done && t.ExecutedAt != null))
        {
            book.Add(trade.Isin, StockPortfolio.Day(trade.ExecutedAt!.Value), trade.Price, PriceSource.Trade);
        }
        foreach (StockSecurity security in account.Securities)
        {
            if (security.ManualPrice is decimal manual)
            {
                book.Add(security.Isin, StockPortfolio.Day(security.ManualPriceAt ?? security.AddedAt), manual, PriceSource.Manual);
            }
            foreach (PricePoint point in Closes.GetValueOrDefault(security.Isin) ?? [])
            {
                book.Add(security.Isin, point.Date, point.Close, PriceSource.Quote);
            }
        }
        return Task.FromResult(book);
    }
}

internal sealed class FakeQuoteSource : IQuoteSource
{
    public Dictionary<string, QuoteSeries> Series { get; } = [];
    public List<(string Symbol, DateOnly From)> Fetches { get; } = [];

    public Task<List<QuoteCandidate>> SearchAsync(string query, CancellationToken cancellationToken = default) => Task.FromResult(new List<QuoteCandidate>());

    public Task<QuoteSeries> HistoryAsync(string symbol, DateOnly from, CancellationToken cancellationToken = default)
    {
        Fetches.Add((symbol, from));
        return Series.TryGetValue(symbol, out QuoteSeries? series)
            ? Task.FromResult(series with { Points = [.. series.Points.Where(p => p.Date >= from)] })
            : throw new QuoteException($"No data found, symbol may be delisted ({symbol})");
    }
}

/// <summary>Price histories in memory; filters are rendered to read the symbol they ask for.</summary>
internal sealed class FakePriceHistories : IPriceHistoryDatabase
{
    public List<PriceHistory> Stored { get; } = [];

    public Task<PriceHistory> SaveDocumentAsync(PriceHistory document)
    {
        document.Id ??= ObjectId.GenerateNewId().ToString();
        Stored.RemoveAll(h => h.Id == document.Id);
        Stored.Add(document);
        return Task.FromResult(document);
    }
    public Task<List<PriceHistory>> GetDocumentsAsync() => Task.FromResult(Stored.ToList());
    public Task<PriceHistory?> GetDocumentByIdAsync(string id) => Task.FromResult(Stored.FirstOrDefault(h => h.Id == id));
    public Task<List<PriceHistory>> GetDocumentsByPredicateAsync(FilterDefinition<PriceHistory> predicate)
    {
        BsonDocument filter = predicate.Render(new RenderArgs<PriceHistory>(BsonSerializer.SerializerRegistry.GetSerializer<PriceHistory>(), BsonSerializer.SerializerRegistry));
        string symbol = filter["Symbol"].AsString;
        return Task.FromResult(Stored.Where(h => h.Symbol == symbol).ToList());
    }
    public Task DeleteDocumentAsync(string id)
    {
        Stored.RemoveAll(h => h.Id == id);
        return Task.CompletedTask;
    }
}
