using Akagi.Web.Models.Finance;
using Microsoft.Extensions.Options;

namespace Akagi.Web.Data;

public interface IFinanceAccountDatabase : IDatabase<FinanceAccount>;

public class FinanceAccountDatabase : Database<FinanceAccount>, IFinanceAccountDatabase
{
    public FinanceAccountDatabase(IOptionsMonitor<DatabaseOptions> options) : base(options, "finance_accounts")
    {
    }
}

public interface IStockTradeDatabase : IDatabase<StockTrade>;

public class StockTradeDatabase : Database<StockTrade>, IStockTradeDatabase
{
    public StockTradeDatabase(IOptionsMonitor<DatabaseOptions> options) : base(options, "finance_trades")
    {
    }
}

public interface IPriceHistoryDatabase : IDatabase<PriceHistory>;

public class PriceHistoryDatabase : Database<PriceHistory>, IPriceHistoryDatabase
{
    public PriceHistoryDatabase(IOptionsMonitor<DatabaseOptions> options) : base(options, "finance_prices")
    {
    }
}
