using MongoDB.Bson.Serialization.Attributes;

namespace Akagi.Web.Models.Finance;

/// <summary>
/// A portfolio of stocks and funds: which securities it may hold, the strategy (target share of the portfolio per
/// security), and how the planner rounds. The trades themselves are <see cref="StockTrade"/>s.
/// </summary>
[BsonDiscriminator("StockAccount")]
[BsonIgnoreExtraElements]
public class StockAccount : FinanceAccount
{
    public override AccountKind Kind => AccountKind.Stocks;

    public List<StockSecurity> Securities { get; set; } = [];

    /// <summary>Target percent of the portfolio per ISIN; securities without an entry target 0 %.</summary>
    public List<StrategyTarget> Strategy { get; set; } = [];

    /// <summary>The planner suggests whole shares; off for brokers that sell fractions.</summary>
    public bool WholeShares { get; set; } = true;

    public StockSecurity? Security(string isin) =>
        Securities.FirstOrDefault(s => string.Equals(s.Isin, isin, StringComparison.OrdinalIgnoreCase));

    public decimal TargetPercent(string isin) =>
        Strategy.FirstOrDefault(t => string.Equals(t.Isin, isin, StringComparison.OrdinalIgnoreCase))?.Percent ?? 0m;

    public void SetTarget(string isin, decimal percent)
    {
        Strategy.RemoveAll(t => string.Equals(t.Isin, isin, StringComparison.OrdinalIgnoreCase));
        if (percent != 0)
        {
            Strategy.Add(new StrategyTarget { Isin = isin, Percent = percent });
        }
    }

    public string DisplayName(string isin) => Security(isin)?.DisplayName ?? isin;
}

/// <summary>
/// One security the portfolio may hold.
/// </summary>
public class StockSecurity
{
    public string Isin { get; set; } = string.Empty;

    /// <summary>A name the user knows it by; looked up when the ISIN is added, editable.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>The quote symbol prices are fetched for (Yahoo Finance, e.g. SXR8.DE); empty for manual prices only.</summary>
    public string Symbol { get; set; } = string.Empty;

    /// <summary>A price entered by hand, in the account's currency; used when it is newer than the fetched price.</summary>
    public decimal? ManualPrice { get; set; }

    public DateTime? ManualPriceAt { get; set; }

    public DateTime AddedAt { get; set; } = DateTime.UtcNow;

    [BsonIgnore]
    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? Isin : Name;
}

public class StrategyTarget
{
    public string Isin { get; set; } = string.Empty;

    /// <summary>0 to 100.</summary>
    public decimal Percent { get; set; }
}
