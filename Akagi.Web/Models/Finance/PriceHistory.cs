using Akagi.Web.Data;
using MongoDB.Bson.Serialization.Attributes;

namespace Akagi.Web.Models.Finance;

/// <summary>
/// Daily closing prices of one quote symbol (a security or a currency pair like USDEUR=X) as fetched, in the symbol's
/// own currency. Shared by all users: prices are public.
/// </summary>
[BsonIgnoreExtraElements]
public class PriceHistory : Savable
{
    public string Symbol { get; set; } = string.Empty;

    /// <summary>The currency the quote source reports, e.g. EUR, USD, GBp (pence).</summary>
    public string Currency { get; set; } = string.Empty;

    public string Exchange { get; set; } = string.Empty;

    public List<PricePoint> Points { get; set; } = [];

    public DateTime FetchedAt { get; set; }

    /// <summary>yyyy-MM-dd of the earliest day asked for; earlier days need another fetch.</summary>
    public string CoveredFrom { get; set; } = string.Empty;

    /// <summary>The last fetch failed with this; empty when it worked.</summary>
    public string Error { get; set; } = string.Empty;

    /// <summary>Merges fetched points in, newer values winning, kept in date order.</summary>
    public void Merge(IEnumerable<PricePoint> points)
    {
        Dictionary<string, decimal> byDay = Points.ToDictionary(p => p.Day, p => p.Close);
        foreach (PricePoint point in points)
        {
            byDay[point.Day] = point.Close;
        }
        Points = [.. byDay.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => new PricePoint { Day = p.Key, Close = p.Value })];
    }
}

public class PricePoint
{
    /// <summary>yyyy-MM-dd, the trading day at the exchange.</summary>
    public string Day { get; set; } = string.Empty;

    public decimal Close { get; set; }

    [BsonIgnore]
    public DateOnly Date => DateOnly.ParseExact(Day, "yyyy-MM-dd");

    public static PricePoint At(DateOnly date, decimal close) => new() { Day = date.ToString("yyyy-MM-dd"), Close = close };
}
