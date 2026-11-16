using Akagi.Web.Models.Finance;

namespace Akagi.Web.Services.Finance;

/// <summary>
/// One kind of financial record system. It knows where its account pages live and turns an account's records into an
/// <see cref="AccountReport"/>; the overview needs nothing else. Add a kind by adding an <see cref="AccountKind"/>, a
/// <see cref="FinanceAccount"/> subclass and one of these.
/// </summary>
public interface IAccountSystem
{
    public AccountKind Kind { get; }
    public string Label { get; }
    public string Description { get; }
    public string Link(IFinanceAccount account);
    public Task<AccountReport> ReportAsync(FinanceAccount account, PriceRefresh refresh = PriceRefresh.Stale);
}

/// <summary>How much outside data (prices, rates) a report may fetch first.</summary>
public enum PriceRefresh
{
    /// <summary>Only what is stored; fast.</summary>
    None,
    /// <summary>Fetch what is older than the refresh interval.</summary>
    Stale,
    /// <summary>Fetch everything again.</summary>
    All
}

public interface IAccountSystems
{
    public IReadOnlyList<IAccountSystem> All { get; }
    public IAccountSystem For(AccountKind kind);
}

public class AccountSystems : IAccountSystems
{
    public AccountSystems(IEnumerable<IAccountSystem> systems)
    {
        All = [.. systems.OrderBy(s => s.Kind)];
    }

    public IReadOnlyList<IAccountSystem> All { get; }

    public IAccountSystem For(AccountKind kind) =>
        All.FirstOrDefault(s => s.Kind == kind) ?? throw new InvalidOperationException($"No account system handles {kind}.");
}

/// <summary>
/// Stock portfolios: valued with fetched, hand-typed and trade prices.
/// </summary>
public class StockAccountSystem : IAccountSystem
{
    private readonly IFinanceStore _store;
    private readonly IQuoteService _quotes;
    private readonly TimeProvider _clock;

    public StockAccountSystem(IFinanceStore store, IQuoteService quotes, TimeProvider clock)
    {
        _store = store;
        _quotes = quotes;
        _clock = clock;
    }

    public AccountKind Kind => AccountKind.Stocks;
    public string Label => "Stock portfolio";
    public string Description => "Stocks and funds by ISIN, with a target strategy, an investment planner and a trade log.";

    public string Link(IFinanceAccount account) => $"Finance/Stocks/{account.Id}";

    public async Task<AccountReport> ReportAsync(FinanceAccount account, PriceRefresh refresh = PriceRefresh.Stale)
    {
        StockAccount stocks = (StockAccount)account;
        List<StockTrade> trades = await _store.TradesAsync(account.UserId, account.Id!);
        PriceBook prices = await _quotes.LoadAsync(stocks, trades, refresh != PriceRefresh.None, refresh == PriceRefresh.All);
        StockPortfolio portfolio = new(stocks, trades, prices);
        return Report(portfolio, Link(account), Today(_clock));
    }

    public static AccountReport Report(StockPortfolio portfolio, string link, DateOnly today)
    {
        List<ValuePoint> series = portfolio.Start is DateOnly start ? portfolio.Series(start.AddDays(-1), today) : [];
        int held = portfolio.Holdings.Count(h => h.Shares > 0);
        string summary = $"{held} of {portfolio.Account.Securities.Count} {(portfolio.Account.Securities.Count == 1 ? "security" : "securities")} held";
        if (portfolio.Pending.Count > 0)
        {
            summary += $", {portfolio.Pending.Count} pending {(portfolio.Pending.Count == 1 ? "trade" : "trades")}";
        }
        List<string> warnings = [.. portfolio.Prices.Warnings];
        foreach (Holding holding in portfolio.Holdings.Where(h => h.Shares > 0 && h.Price == null))
        {
            warnings.Add($"{portfolio.Account.DisplayName(holding.Isin)} has no price yet, so it counts as 0.");
        }
        return new AccountReport
        {
            Account = portfolio.Account,
            Link = link,
            Value = portfolio.Value,
            Invested = portfolio.Invested,
            Series = series,
            Flows = portfolio.Flows,
            Warnings = warnings,
            Summary = summary,
            Pending = portfolio.Pending.Count
        };
    }

    public static DateOnly Today(TimeProvider clock) => DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
}
