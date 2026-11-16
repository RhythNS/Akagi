using Akagi.Web.Models.Finance;

namespace Akagi.Web.Services.Finance;

/// <summary>One security in a plan: what is held, the target, and the planned change in shares (+ buy, − sell).</summary>
public sealed class PlanRow
{
    public required string Isin { get; init; }
    public string Name { get; init; } = string.Empty;
    /// <summary>Price per share the plan works with, in the account's currency.</summary>
    public decimal Price { get; set; }
    public decimal Shares { get; init; }
    /// <summary>Target percent as set in the strategy (0 to 100).</summary>
    public decimal TargetPercent { get; init; }
    public decimal Delta { get; set; }
    /// <summary>The user changed this row by hand; "spend the rest" leaves it alone.</summary>
    public bool Edited { get; set; }

    public decimal Value => Shares * Price;
    public decimal SharesAfter => Shares + Delta;
    public decimal ValueAfter => SharesAfter * Price;
    /// <summary>Money the change costs (+) or brings in (−).</summary>
    public decimal Cost => Delta * Price;
    public TradeSide? Side => Delta > 0 ? TradeSide.Buy : Delta < 0 ? TradeSide.Sell : null;
}

/// <summary>
/// Planning what to buy and sell: a sum of new money, the holdings, the strategy. Suggestions fill the rows; every row
/// can be changed by hand, and the money left over is always what the rows leave of the sum and the sales.
/// </summary>
public sealed class InvestmentPlan
{
    public InvestmentPlan(IEnumerable<PlanRow> rows, decimal cash, bool wholeShares)
    {
        Rows = [.. rows];
        Cash = cash;
        WholeShares = wholeShares;
    }

    public List<PlanRow> Rows { get; }

    /// <summary>New money to put in.</summary>
    public decimal Cash { get; set; }

    public bool WholeShares { get; set; }

    /// <summary>Buys minus sales.</summary>
    public decimal Spent => Rows.Sum(r => r.Cost);

    /// <summary>What is left of the new money and the sales; negative means the plan needs more money.</summary>
    public decimal Leftover => Cash - Spent;

    public decimal Buys => Rows.Where(r => r.Delta > 0).Sum(r => r.Cost);
    public decimal Sales => -Rows.Where(r => r.Delta < 0).Sum(r => r.Cost);

    public decimal ValueBefore => Rows.Sum(r => r.Value);

    /// <summary>Holdings after the plan plus the money left over.</summary>
    public decimal TotalAfter => Rows.Sum(r => r.ValueAfter) + Leftover;

    /// <summary>Sum of the targets; the plan scales them to 100 % if they do not add up.</summary>
    public decimal TargetSum => Rows.Sum(r => r.TargetPercent);

    public decimal Weight(PlanRow row) => TargetSum > 0 ? row.TargetPercent / TargetSum : 0;

    public decimal PercentBefore(PlanRow row) => ValueBefore > 0 ? row.Value / ValueBefore * 100 : 0;

    public decimal PercentAfter(PlanRow row) => TotalAfter > 0 ? row.ValueAfter / TotalAfter * 100 : 0;

    /// <summary>Distance from the target after the plan, in percentage points (+ over, − under).</summary>
    public decimal Drift(PlanRow row) => PercentAfter(row) - Weight(row) * 100;

    /// <summary>A sale of more shares than are held, or a plan that spends more than it has.</summary>
    public IEnumerable<string> Problems()
    {
        foreach (PlanRow row in Rows.Where(r => r.SharesAfter < 0))
        {
            yield return $"{row.Name}: sells {Money.Shares(-row.Delta)} shares but only {Money.Shares(row.Shares)} are held.";
        }
        foreach (PlanRow row in Rows.Where(r => r.Delta != 0 && r.Price <= 0))
        {
            yield return $"{row.Name} has no price, so the plan cannot count its cost.";
        }
        if (Leftover < -0.005m)
        {
            yield return $"The plan spends {Money.Format(-Leftover)} more than the money to invest and the sales.";
        }
    }

    public void Clear()
    {
        foreach (PlanRow row in Rows)
        {
            row.Delta = 0;
            row.Edited = false;
        }
    }

    /// <summary>Only buys: spends the money on what is furthest under its target.</summary>
    public void SuggestBuys()
    {
        Clear();
        Fill();
    }

    /// <summary>
    /// Sells what is over its target (all of what targets 0 %), then buys with the money and the proceeds, so the
    /// portfolio ends as close to the strategy as whole shares allow.
    /// </summary>
    public void SuggestRebalance()
    {
        Clear();
        decimal total = TotalAfter;
        foreach (PlanRow row in Rows.Where(r => r.Price > 0 && r.Shares > 0))
        {
            decimal target = Weight(row) * total;
            if (target <= 0)
            {
                row.Delta = -row.Shares;
                continue;
            }
            decimal over = row.Value - target;
            if (over > 0)
            {
                row.Delta = -Math.Min(row.Shares, RoundDown(over / row.Price));
            }
        }
        Fill();
    }

    /// <summary>
    /// Spends the money left over on buys, keeping every planned change. Rows changed by hand are left alone when
    /// others can take the money.
    /// </summary>
    public void Fill()
    {
        if (Leftover <= 0)
        {
            return;
        }
        List<PlanRow> candidates = [.. Rows.Where(r => r.Price > 0 && r.TargetPercent > 0 && !r.Edited)];
        if (candidates.Count == 0)
        {
            candidates = [.. Rows.Where(r => r.Price > 0 && r.TargetPercent > 0)];
        }
        if (candidates.Count == 0)
        {
            return;
        }

        // Water filling: whatever is already over its share of (its value + the money) gets nothing
        decimal money = Leftover;
        List<PlanRow> active = [.. candidates];
        Dictionary<PlanRow, decimal> targets = [];
        while (active.Count > 0)
        {
            decimal weights = active.Sum(r => r.TargetPercent);
            decimal pool = active.Sum(r => r.ValueAfter) + money;
            targets = active.ToDictionary(r => r, r => r.TargetPercent / weights * pool);
            List<PlanRow> over = [.. active.Where(r => r.ValueAfter > targets[r])];
            if (over.Count == 0)
            {
                break;
            }
            active.RemoveAll(over.Contains);
        }
        foreach (PlanRow row in active)
        {
            decimal add = RoundDown((targets[row] - row.ValueAfter) / row.Price);
            if (add > 0)
            {
                row.Delta += add;
            }
        }

        if (!WholeShares)
        {
            return;
        }

        // Rounding down left some money: one share at a time where it brings the portfolio closest to the targets
        decimal weightSum = candidates.Sum(r => r.TargetPercent);
        for (int guard = 0; guard < 10_000; guard++)
        {
            decimal left = Leftover;
            decimal pool = candidates.Sum(r => r.ValueAfter) + left;
            PlanRow? best = null;
            decimal bestGain = 0;
            foreach (PlanRow row in candidates.Where(r => r.Price <= left))
            {
                decimal gap = row.TargetPercent / weightSum * pool - row.ValueAfter;
                // How much one more share shrinks the squared distance from the target
                decimal gain = row.Price * (2 * gap - row.Price);
                if (gain > bestGain)
                {
                    best = row;
                    bestGain = gain;
                }
            }
            if (best == null)
            {
                break;
            }
            best.Delta += 1;
        }
    }

    private decimal RoundDown(decimal shares) =>
        WholeShares ? Math.Floor(shares) : Math.Floor(shares * 10_000) / 10_000;

    /// <summary>The plan's changes as pending trades.</summary>
    public List<StockTrade> ToTrades(string userId, string accountId, DateTime now)
    {
        string planId = Guid.NewGuid().ToString("N");
        return [.. Rows.Where(r => r.Delta != 0).OrderBy(r => r.Delta > 0).Select(r => new StockTrade
        {
            UserId = userId,
            AccountId = accountId,
            Isin = r.Isin,
            Side = r.Delta > 0 ? TradeSide.Buy : TradeSide.Sell,
            Status = TradeStatus.Pending,
            PlannedShares = Math.Abs(r.Delta),
            PlannedPrice = r.Price,
            Shares = Math.Abs(r.Delta),
            Price = r.Price,
            CreatedAt = now,
            PlanId = planId
        })];
    }

    /// <summary>A plan over every security of the account with the given holdings and prices.</summary>
    public static InvestmentPlan For(StockPortfolio portfolio, decimal cash) =>
        new(portfolio.Current.Select(s => new PlanRow
        {
            Isin = s.Isin,
            Name = s.DisplayName,
            Price = portfolio.Holding(s.Isin).Price?.Price ?? 0,
            Shares = Math.Max(0, portfolio.Holding(s.Isin).Shares),
            TargetPercent = portfolio.Account.TargetPercent(s.Isin)
        }), cash, portfolio.Account.WholeShares);
}
