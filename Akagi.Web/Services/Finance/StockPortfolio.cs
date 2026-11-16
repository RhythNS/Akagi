using Akagi.Web.Models.Finance;

namespace Akagi.Web.Services.Finance;

/// <summary>
/// What the done trades add up to for one security, with average cost: a sale takes out the average cost of the
/// shares it sold, and the rest of its proceeds is realized gain.
/// </summary>
public sealed class Holding
{
    public required string Isin { get; init; }
    public decimal Shares { get; internal set; }
    /// <summary>What the shares still held cost, fees included.</summary>
    public decimal CostBasis { get; internal set; }
    public decimal RealizedGain { get; internal set; }
    public decimal Fees { get; internal set; }
    /// <summary>Money spent on buys, fees included.</summary>
    public decimal Bought { get; internal set; }
    /// <summary>Money received from sales, after fees.</summary>
    public decimal Sold { get; internal set; }
    public int Buys { get; internal set; }
    public int Sells { get; internal set; }
    public DateTime? FirstTrade { get; internal set; }
    public DateTime? LastTrade { get; internal set; }

    public decimal AverageCost => Shares > 0 ? CostBasis / Shares : 0;

    /// <summary>Latest price in the account's currency, if any is known.</summary>
    public PriceAt? Price { get; internal set; }

    public decimal Value => Shares * (Price?.Price ?? 0);
    public decimal UnrealizedGain => Price == null ? 0 : Value - CostBasis;
    public decimal? UnrealizedPercent => CostBasis > 0 && Price != null ? UnrealizedGain / CostBasis * 100 : null;
    public decimal TotalGain => UnrealizedGain + RealizedGain;
}

/// <summary>
/// A stock account's trades and prices, worked out: holdings now, shares and value on any day, and the money that went
/// in and out.
/// </summary>
public sealed class StockPortfolio
{
    private readonly Dictionary<string, List<(DateOnly Day, decimal Delta)>> _changes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Holding> _holdings = new(StringComparer.OrdinalIgnoreCase);

    public StockPortfolio(StockAccount account, IEnumerable<StockTrade> trades, PriceBook prices)
    {
        Account = account;
        Prices = prices;
        Done = [.. trades.Where(t => t.Status == TradeStatus.Done && t.ExecutedAt != null).OrderBy(t => t.ExecutedAt).ThenBy(t => t.CreatedAt)];
        Pending = [.. trades.Where(t => t.Status == TradeStatus.Pending).OrderBy(t => t.CreatedAt)];

        foreach (StockSecurity security in account.Securities)
        {
            Get(security.Isin);
        }
        foreach (StockTrade trade in Done)
        {
            Holding holding = Get(trade.Isin);
            DateOnly day = Day(trade.ExecutedAt!.Value);
            holding.Fees += trade.Fees;
            holding.FirstTrade ??= trade.ExecutedAt;
            holding.LastTrade = trade.ExecutedAt;
            if (trade.Side == TradeSide.Buy)
            {
                holding.Shares += trade.Shares;
                holding.CostBasis += trade.Amount + trade.Fees;
                holding.Bought += trade.Amount + trade.Fees;
                holding.Buys++;
            }
            else
            {
                decimal sold = Math.Min(trade.Shares, Math.Max(holding.Shares, 0));
                decimal cost = holding.Shares > 0 ? holding.CostBasis / holding.Shares * sold : 0;
                decimal proceeds = trade.Amount - trade.Fees;
                holding.RealizedGain += proceeds - cost;
                holding.CostBasis -= cost;
                holding.Shares -= trade.Shares;
                holding.Sold += proceeds;
                holding.Sells++;
                if (holding.Shares <= 0)
                {
                    holding.CostBasis = 0;
                }
            }
            Flows.Add(new CashFlow(day, trade.CashFlow));
            if (!_changes.TryGetValue(trade.Isin, out List<(DateOnly, decimal)>? list))
            {
                _changes[trade.Isin] = list = [];
            }
            list.Add((day, trade.Side == TradeSide.Buy ? trade.Shares : -trade.Shares));
        }
        foreach (Holding holding in _holdings.Values)
        {
            holding.Price = prices.Latest(holding.Isin);
        }
    }

    public StockAccount Account { get; }
    public PriceBook Prices { get; }
    public List<StockTrade> Done { get; }
    public List<StockTrade> Pending { get; }

    /// <summary>Money into the account (+, buys) and out of it (−, sales), per trade day.</summary>
    public List<CashFlow> Flows { get; } = [];

    /// <summary>Every security of the account and every one ever traded, in the account's order.</summary>
    public IReadOnlyList<Holding> Holdings =>
        [.. _holdings.Values.OrderBy(h => Order(h.Isin))];

    public Holding Holding(string isin) => Get(isin);

    /// <summary>Traded before, nothing left and no target: kept for the history, left out of planning.</summary>
    public bool IsSoldOut(string isin)
    {
        Holding holding = Get(isin);
        return holding.Buys + holding.Sells > 0 && holding.Shares <= 0 && Account.TargetPercent(isin) == 0;
    }

    /// <summary>The account's securities without the sold out ones.</summary>
    public List<StockSecurity> Current => [.. Account.Securities.Where(s => !IsSoldOut(s.Isin))];

    public List<StockSecurity> SoldOut => [.. Account.Securities.Where(s => IsSoldOut(s.Isin))];

    public decimal Value => _holdings.Values.Sum(h => h.Value);

    /// <summary>Net money put in: buys minus what sales paid out.</summary>
    public decimal Invested => Flows.Sum(f => f.Amount);

    public decimal CostBasis => _holdings.Values.Sum(h => h.CostBasis);
    public decimal RealizedGain => _holdings.Values.Sum(h => h.RealizedGain);
    public decimal UnrealizedGain => _holdings.Values.Sum(h => h.UnrealizedGain);
    public decimal Fees => _holdings.Values.Sum(h => h.Fees);

    /// <summary>The day of the first done trade.</summary>
    public DateOnly? Start => Done.Count > 0 ? Day(Done[0].ExecutedAt!.Value) : null;

    public decimal SharesOn(string isin, DateOnly day) =>
        _changes.TryGetValue(isin, out List<(DateOnly Day, decimal Delta)>? list) ? list.Where(c => c.Day <= day).Sum(c => c.Delta) : 0;

    /// <summary>
    /// Value at the end of each day and money put in by then, from <paramref name="from"/> to <paramref name="to"/>.
    /// </summary>
    public List<ValuePoint> Series(DateOnly from, DateOnly to)
    {
        List<ValuePoint> points = [];
        Dictionary<string, decimal> shares = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, int> next = _changes.Keys.ToDictionary(k => k, _ => 0, StringComparer.OrdinalIgnoreCase);
        List<CashFlow> flows = [.. Flows.OrderBy(f => f.Day)];
        int flowIndex = 0;
        decimal invested = 0;
        for (DateOnly day = from; day <= to; day = day.AddDays(1))
        {
            foreach ((string isin, List<(DateOnly Day, decimal Delta)> changes) in _changes)
            {
                int i = next[isin];
                while (i < changes.Count && changes[i].Day <= day)
                {
                    shares[isin] = shares.GetValueOrDefault(isin) + changes[i].Delta;
                    i++;
                }
                next[isin] = i;
            }
            while (flowIndex < flows.Count && flows[flowIndex].Day <= day)
            {
                invested += flows[flowIndex].Amount;
                flowIndex++;
            }
            decimal value = 0;
            foreach ((string isin, decimal count) in shares)
            {
                if (count != 0 && Prices.On(isin, day) is PriceAt price)
                {
                    value += count * price.Price;
                }
            }
            points.Add(new ValuePoint(day, value, invested));
        }
        return points;
    }

    public static DateOnly Day(DateTime utc) => DateOnly.FromDateTime(utc.Kind == DateTimeKind.Unspecified ? utc : utc.ToLocalTime());

    private Holding Get(string isin)
    {
        if (!_holdings.TryGetValue(isin, out Holding? holding))
        {
            _holdings[isin] = holding = new Holding { Isin = isin };
        }
        return holding;
    }

    private int Order(string isin)
    {
        int index = Account.Securities.FindIndex(s => string.Equals(s.Isin, isin, StringComparison.OrdinalIgnoreCase));
        return index < 0 ? int.MaxValue : index;
    }
}
