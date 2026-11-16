namespace Akagi.Web.Services.Finance;

/// <summary>Where a price came from; on the same day a later value here wins.</summary>
public enum PriceSource
{
    /// <summary>The price a recorded trade went through at.</summary>
    Trade,
    /// <summary>A fetched daily close.</summary>
    Quote,
    /// <summary>Typed in by hand.</summary>
    Manual
}

public readonly record struct PriceAt(DateOnly Day, decimal Price, PriceSource Source);

/// <summary>
/// Prices per ISIN in the account's currency, from every source. The price on a day is the latest one on or before it;
/// on the same day a manual price beats a close, and a close beats a trade price.
/// </summary>
public sealed class PriceBook
{
    private readonly Dictionary<string, List<PriceAt>> _points = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _quoteCurrency = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Things that went wrong while loading, to show the user (a symbol Yahoo does not know, no exchange rate).</summary>
    public List<string> Warnings { get; } = [];

    /// <summary>When prices were last fetched, if they were.</summary>
    public DateTime? FetchedAt { get; set; }

    public void Add(string isin, DateOnly day, decimal price, PriceSource source)
    {
        if (price <= 0)
        {
            return;
        }
        if (!_points.TryGetValue(isin, out List<PriceAt>? list))
        {
            _points[isin] = list = [];
        }
        PriceAt point = new(day, price, source);
        int index = list.BinarySearch(point, Order);
        if (index >= 0)
        {
            if (list[index].Source <= source)
            {
                list[index] = point;
            }
            return;
        }
        list.Insert(~index, point);
    }

    public void SetQuoteCurrency(string isin, string currency) => _quoteCurrency[isin] = currency;

    /// <summary>The currency the fetched prices came in before conversion, if any were fetched.</summary>
    public string? QuoteCurrencyOf(string isin) => _quoteCurrency.GetValueOrDefault(isin);

    /// <summary>The latest price on or before the day; before the first known price, the first one.</summary>
    public PriceAt? On(string isin, DateOnly day)
    {
        if (!_points.TryGetValue(isin, out List<PriceAt>? list) || list.Count == 0)
        {
            return null;
        }
        int index = list.BinarySearch(new PriceAt(day, 0, PriceSource.Manual), Order);
        if (index < 0)
        {
            index = ~index - 1;
        }
        return index >= 0 ? list[index] : list[0];
    }

    public PriceAt? Latest(string isin) => _points.TryGetValue(isin, out List<PriceAt>? list) && list.Count > 0 ? list[^1] : null;

    public IReadOnlyList<PriceAt> Points(string isin) => _points.TryGetValue(isin, out List<PriceAt>? list) ? list : [];

    private static readonly Comparer<PriceAt> Order = Comparer<PriceAt>.Create((a, b) => a.Day.CompareTo(b.Day));
}
