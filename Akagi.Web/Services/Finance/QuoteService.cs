using Akagi.Web.Data;
using Akagi.Web.Models.Finance;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace Akagi.Web.Services.Finance;

public class FinanceOptions
{
    /// <summary>Fetch prices from the quote source; off means hand-typed and trade prices only.</summary>
    public bool QuotesEnabled { get; set; } = true;

    /// <summary>Prices older than this are fetched again when a page opens.</summary>
    public int RefreshMinutes { get; set; } = 60;
}

/// <summary>
/// Prices for a stock account: fetched closes (kept in the database, refreshed when stale), converted into the
/// account's currency, plus hand-typed prices and the prices of recorded trades.
/// </summary>
public interface IQuoteService
{
    public bool Enabled { get; }
    public Task<List<QuoteCandidate>> SearchAsync(string query);
    /// <summary>Fetches the symbol to show what it is (name, currency, latest close); throws <see cref="QuoteException"/>.</summary>
    public Task<QuoteSeries> CheckAsync(string symbol);
    /// <summary>All prices of the account's securities; <paramref name="refresh"/> fetches stale ones first.</summary>
    public Task<PriceBook> LoadAsync(StockAccount account, IReadOnlyList<StockTrade> trades, bool refresh = true, bool force = false);
}

public class QuoteService : IQuoteService
{
    private static readonly SemaphoreSlim FetchLock = new(1, 1);

    private readonly IQuoteSource _source;
    private readonly IPriceHistoryDatabase _histories;
    private readonly IOptionsMonitor<FinanceOptions> _options;
    private readonly TimeProvider _clock;
    private readonly ILogger<QuoteService> _logger;

    public QuoteService(IQuoteSource source, IPriceHistoryDatabase histories, IOptionsMonitor<FinanceOptions> options, TimeProvider clock, ILogger<QuoteService> logger)
    {
        _source = source;
        _histories = histories;
        _options = options;
        _clock = clock;
        _logger = logger;
    }

    public bool Enabled => _options.CurrentValue.QuotesEnabled;

    public async Task<List<QuoteCandidate>> SearchAsync(string query)
    {
        if (!Enabled || string.IsNullOrWhiteSpace(query))
        {
            return [];
        }
        try
        {
            return await _source.SearchAsync(query.Trim());
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            _logger.LogWarning(exception, "Quote search for {Query} failed", query);
            return [];
        }
    }

    public async Task<QuoteSeries> CheckAsync(string symbol)
    {
        if (!Enabled)
        {
            throw new QuoteException("Fetching prices is turned off (Finance:QuotesEnabled).");
        }
        try
        {
            return await _source.HistoryAsync(symbol.Trim(), Today.AddDays(-10));
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException or KeyNotFoundException or IndexOutOfRangeException or InvalidOperationException)
        {
            throw new QuoteException($"Could not fetch {symbol}: {exception.Message}");
        }
    }

    private DateOnly Today => DateOnly.FromDateTime(_clock.GetUtcNow().UtcDateTime);

    public async Task<PriceBook> LoadAsync(StockAccount account, IReadOnlyList<StockTrade> trades, bool refresh = true, bool force = false)
    {
        PriceBook book = new();
        string currency = account.Currency.ToUpperInvariant();

        foreach (StockSecurity security in account.Securities)
        {
            List<StockTrade> done = [.. trades.Where(t => t.Status == TradeStatus.Done && t.ExecutedAt != null && string.Equals(t.Isin, security.Isin, StringComparison.OrdinalIgnoreCase))];
            foreach (StockTrade trade in done)
            {
                book.Add(security.Isin, StockPortfolio.Day(trade.ExecutedAt!.Value), trade.Price, PriceSource.Trade);
            }
            if (security.ManualPrice is decimal manual)
            {
                book.Add(security.Isin, StockPortfolio.Day(security.ManualPriceAt ?? security.AddedAt), manual, PriceSource.Manual);
            }
            if (string.IsNullOrWhiteSpace(security.Symbol) || !Enabled)
            {
                continue;
            }

            DateOnly needed = done.Count > 0 ? DateOnly.FromDateTime(done.Min(t => t.ExecutedAt!.Value)).AddDays(-7) : Today.AddYears(-1);
            if (needed > Today.AddYears(-1))
            {
                needed = Today.AddYears(-1);
            }
            PriceHistory? history = await HistoryAsync(security.Symbol, needed, refresh, force, book);
            if (history == null || history.Points.Count == 0)
            {
                continue;
            }
            book.FetchedAt = book.FetchedAt == null || history.FetchedAt < book.FetchedAt ? history.FetchedAt : book.FetchedAt;
            (string quoteCurrency, decimal factor) = QuoteCurrency.Normalize(history.Currency);
            book.SetQuoteCurrency(security.Isin, history.Currency);
            if (string.IsNullOrEmpty(quoteCurrency) || quoteCurrency == currency)
            {
                foreach (PricePoint point in history.Points)
                {
                    book.Add(security.Isin, point.Date, point.Close * factor, PriceSource.Quote);
                }
                continue;
            }

            // Another currency: convert with that day's exchange rate
            PriceHistory? fx = await HistoryAsync(QuoteCurrency.FxSymbol(quoteCurrency, currency), needed, refresh, force, book);
            if (fx == null || fx.Points.Count == 0)
            {
                book.Warnings.Add($"{security.DisplayName}: {security.Symbol} is quoted in {quoteCurrency} and there is no {quoteCurrency}/{currency} rate, so its fetched prices are left out. Pick a listing in {currency} or type a price.");
                continue;
            }
            PriceBook rates = new();
            foreach (PricePoint point in fx.Points)
            {
                rates.Add("fx", point.Date, point.Close, PriceSource.Quote);
            }
            foreach (PricePoint point in history.Points)
            {
                if (rates.On("fx", point.Date) is PriceAt rate)
                {
                    book.Add(security.Isin, point.Date, Math.Round(point.Close * factor * rate.Price, 4), PriceSource.Quote);
                }
            }
        }
        return book;
    }

    /// <summary>
    /// The stored history of a symbol, fetched first when it is stale or does not reach back to <paramref name="needed"/>.
    /// </summary>
    private async Task<PriceHistory?> HistoryAsync(string symbol, DateOnly needed, bool refresh, bool force, PriceBook book)
    {
        PriceHistory? history = (await _histories.GetDocumentsByPredicateAsync(Builders<PriceHistory>.Filter.Eq(h => h.Symbol, symbol))).FirstOrDefault();
        if (!refresh || !Stale(history, needed, force))
        {
            ReportError(history, book);
            return history;
        }

        await FetchLock.WaitAsync();
        try
        {
            // Another page may have fetched it while this one waited
            history = (await _histories.GetDocumentsByPredicateAsync(Builders<PriceHistory>.Filter.Eq(h => h.Symbol, symbol))).FirstOrDefault();
            if (!Stale(history, needed, force))
            {
                ReportError(history, book);
                return history;
            }
            history ??= new PriceHistory { Symbol = symbol };
            DateOnly from = history.Points.Count > 0 && Covered(history) <= needed ? history.Points[^1].Date.AddDays(-7) : needed;
            try
            {
                QuoteSeries series = await _source.HistoryAsync(symbol, from);
                history.Merge(series.Points);
                history.Currency = series.Currency;
                history.Exchange = series.Exchange;
                history.Error = string.Empty;
                if (Covered(history) > needed)
                {
                    history.CoveredFrom = needed.ToString("yyyy-MM-dd");
                }
            }
            catch (Exception exception) when (exception is QuoteException or HttpRequestException or TaskCanceledException or System.Text.Json.JsonException or KeyNotFoundException or IndexOutOfRangeException or InvalidOperationException)
            {
                _logger.LogWarning(exception, "Fetching prices for {Symbol} failed", symbol);
                history.Error = exception is QuoteException ? exception.Message : $"Fetching {symbol} failed: {exception.Message}";
            }
            history.FetchedAt = _clock.GetUtcNow().UtcDateTime;
            await _histories.SaveDocumentAsync(history);
            ReportError(history, book);
            return history;
        }
        finally
        {
            FetchLock.Release();
        }
    }

    private static void ReportError(PriceHistory? history, PriceBook book)
    {
        if (history != null && !string.IsNullOrEmpty(history.Error))
        {
            book.Warnings.Add($"{history.Symbol}: {history.Error}");
        }
    }

    private static DateOnly Covered(PriceHistory history) =>
        string.IsNullOrEmpty(history.CoveredFrom) ? DateOnly.MaxValue : DateOnly.ParseExact(history.CoveredFrom, "yyyy-MM-dd");

    private bool Stale(PriceHistory? history, DateOnly needed, bool force)
    {
        if (history == null || force)
        {
            return true;
        }
        int minutes = Math.Max(1, _options.CurrentValue.RefreshMinutes);
        bool old = _clock.GetUtcNow().UtcDateTime - history.FetchedAt > TimeSpan.FromMinutes(minutes);
        // A failed fetch waits as long before the next try, so a wrong symbol does not hit the source on every page
        if (!string.IsNullOrEmpty(history.Error))
        {
            return old;
        }
        return old || Covered(history) > needed;
    }
}
