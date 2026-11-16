using Akagi.Web.Models.Finance;
using Akagi.Web.Services.Finance;

namespace Akagi.Web.Pages.Finance;

/// <summary>
/// One stock account as the pages use it: the account, its trades, prices, and what they add up to. Pages load it from
/// the database first (fast) and then refresh stale prices after the first render.
/// </summary>
public sealed class StockSession
{
    private readonly IFinanceStore _store;
    private readonly IQuoteService _quotes;

    private StockSession(IFinanceStore store, IQuoteService quotes, string userId, string accountId)
    {
        _store = store;
        _quotes = quotes;
        UserId = userId;
        AccountId = accountId;
    }

    public string UserId { get; }
    public string AccountId { get; }
    public StockAccount? Account { get; private set; }
    public List<StockTrade> Trades { get; private set; } = [];
    public PriceBook Prices { get; private set; } = new();
    public StockPortfolio? Portfolio { get; private set; }
    public bool Missing => Account == null;
    public bool Refreshed { get; private set; }

    public static async Task<StockSession> LoadAsync(IFinanceStore store, IQuoteService quotes, string userId, string accountId)
    {
        StockSession session = new(store, quotes, userId, accountId);
        await session.ReloadAsync(refresh: false);
        return session;
    }

    /// <summary>Reads everything again; <paramref name="refresh"/> fetches stale prices, <paramref name="force"/> all of them.</summary>
    public async Task ReloadAsync(bool refresh, bool force = false)
    {
        Account = await _store.AccountAsync(UserId, AccountId) as StockAccount;
        if (Account == null)
        {
            Trades = [];
            Portfolio = null;
            return;
        }
        Trades = await _store.TradesAsync(UserId, AccountId);
        Prices = await _quotes.LoadAsync(Account, Trades, refresh, force);
        Portfolio = new StockPortfolio(Account, Trades, Prices);
        Refreshed |= refresh;
    }

    /// <summary>Works the trades and prices out again after a change to the account or the trades in memory.</summary>
    public async Task SaveAccountAsync()
    {
        if (Account != null)
        {
            await _store.SaveAccountAsync(Account);
            await ReloadAsync(refresh: false);
        }
    }

    public async Task SaveTradeAsync(StockTrade trade)
    {
        trade.UserId = UserId;
        trade.AccountId = AccountId;
        await _store.SaveTradeAsync(trade);
        await ReloadAsync(refresh: false);
    }

    public async Task DeleteTradeAsync(StockTrade trade)
    {
        if (trade.Id != null)
        {
            await _store.DeleteTradeAsync(UserId, trade.Id);
        }
        await ReloadAsync(refresh: false);
    }

    public string Name(string isin) => Account?.DisplayName(isin) ?? isin;
}
