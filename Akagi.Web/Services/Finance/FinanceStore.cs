using Akagi.Web.Data;
using Akagi.Web.Models.Finance;
using MongoDB.Driver;

namespace Akagi.Web.Services.Finance;

/// <summary>
/// The user's accounts and stock trades in the website's database. Everything is checked against the user id, so a
/// guessed id never reaches another user's records.
/// </summary>
public interface IFinanceStore
{
    public Task<List<FinanceAccount>> AccountsAsync(string userId);
    public Task<FinanceAccount?> AccountAsync(string userId, string id);
    public Task<FinanceAccount> SaveAccountAsync(FinanceAccount account);
    /// <summary>Deletes the account and all of its trades.</summary>
    public Task DeleteAccountAsync(string userId, string id);

    public Task<List<StockTrade>> TradesAsync(string userId, string accountId);
    public Task<StockTrade?> TradeAsync(string userId, string id);
    public Task<StockTrade> SaveTradeAsync(StockTrade trade);
    public Task DeleteTradeAsync(string userId, string id);
}

public class FinanceStore : IFinanceStore
{
    private readonly IFinanceAccountDatabase _accounts;
    private readonly IStockTradeDatabase _trades;

    public FinanceStore(IFinanceAccountDatabase accounts, IStockTradeDatabase trades)
    {
        _accounts = accounts;
        _trades = trades;
    }

    public async Task<List<FinanceAccount>> AccountsAsync(string userId) =>
        [.. (await _accounts.GetDocumentsByPredicateAsync(Builders<FinanceAccount>.Filter.Eq(a => a.UserId, userId))).OrderBy(a => a.CreatedAt)];

    public async Task<FinanceAccount?> AccountAsync(string userId, string id)
    {
        FinanceAccount? account = await _accounts.GetDocumentByIdAsync(id);
        return account?.UserId == userId ? account : null;
    }

    public Task<FinanceAccount> SaveAccountAsync(FinanceAccount account) => _accounts.SaveDocumentAsync(account);

    public async Task DeleteAccountAsync(string userId, string id)
    {
        if (await AccountAsync(userId, id) == null)
        {
            return;
        }
        foreach (StockTrade trade in await TradesAsync(userId, id))
        {
            await _trades.DeleteDocumentAsync(trade.Id!);
        }
        await _accounts.DeleteDocumentAsync(id);
    }

    public async Task<List<StockTrade>> TradesAsync(string userId, string accountId) =>
        [.. (await _trades.GetDocumentsByPredicateAsync(Builders<StockTrade>.Filter.And(
                Builders<StockTrade>.Filter.Eq(t => t.UserId, userId),
                Builders<StockTrade>.Filter.Eq(t => t.AccountId, accountId))))
            .OrderBy(t => t.ExecutedAt ?? t.CreatedAt)];

    public async Task<StockTrade?> TradeAsync(string userId, string id)
    {
        StockTrade? trade = await _trades.GetDocumentByIdAsync(id);
        return trade?.UserId == userId ? trade : null;
    }

    public Task<StockTrade> SaveTradeAsync(StockTrade trade) => _trades.SaveDocumentAsync(trade);

    public async Task DeleteTradeAsync(string userId, string id)
    {
        if (await TradeAsync(userId, id) != null)
        {
            await _trades.DeleteDocumentAsync(id);
        }
    }
}
