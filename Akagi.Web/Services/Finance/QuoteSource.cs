using Akagi.Web.Models.Finance;
using System.Globalization;
using System.Text.Json;

namespace Akagi.Web.Services.Finance;

/// <summary>A listing a search found for an ISIN or name.</summary>
public sealed record QuoteCandidate(string Symbol, string Name, string Exchange, string Type);

/// <summary>Daily closes of a symbol in its own currency.</summary>
public sealed record QuoteSeries(string Symbol, string Name, string Currency, string Exchange, List<PricePoint> Points);

/// <summary>
/// Where prices come from. The only one is Yahoo Finance's public chart and search endpoints, which need no key but are
/// unofficial; everything also works with prices typed in by hand.
/// </summary>
public interface IQuoteSource
{
    public Task<List<QuoteCandidate>> SearchAsync(string query, CancellationToken cancellationToken = default);
    public Task<QuoteSeries> HistoryAsync(string symbol, DateOnly from, CancellationToken cancellationToken = default);
}

public class YahooQuoteSource : IQuoteSource
{
    private readonly HttpClient _http;

    public YahooQuoteSource(HttpClient http)
    {
        _http = http;
        _http.Timeout = TimeSpan.FromSeconds(20);
        if (_http.DefaultRequestHeaders.UserAgent.Count == 0)
        {
            // Yahoo refuses requests without a browser-like agent
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (compatible)");
        }
    }

    public async Task<List<QuoteCandidate>> SearchAsync(string query, CancellationToken cancellationToken = default)
    {
        string url = $"https://query2.finance.yahoo.com/v1/finance/search?q={Uri.EscapeDataString(query)}&quotesCount=10&newsCount=0&listsCount=0";
        using HttpResponseMessage response = await _http.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();
        return ParseSearch(await response.Content.ReadAsStringAsync(cancellationToken));
    }

    public async Task<QuoteSeries> HistoryAsync(string symbol, DateOnly from, CancellationToken cancellationToken = default)
    {
        long period1 = new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).ToUnixTimeSeconds();
        long period2 = DateTimeOffset.UtcNow.AddDays(1).ToUnixTimeSeconds();
        string url = $"https://query1.finance.yahoo.com/v8/finance/chart/{Uri.EscapeDataString(symbol)}?period1={period1}&period2={period2}&interval=1d&events=history";
        using HttpResponseMessage response = await _http.GetAsync(url, cancellationToken);
        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new QuoteException(ChartError(body) ?? $"Yahoo answered {(int)response.StatusCode} for {symbol}.");
        }
        return ParseChart(symbol, body);
    }

    public static List<QuoteCandidate> ParseSearch(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        List<QuoteCandidate> candidates = [];
        if (!document.RootElement.TryGetProperty("quotes", out JsonElement quotes) || quotes.ValueKind != JsonValueKind.Array)
        {
            return candidates;
        }
        foreach (JsonElement quote in quotes.EnumerateArray())
        {
            string? symbol = Text(quote, "symbol");
            if (string.IsNullOrWhiteSpace(symbol))
            {
                continue;
            }
            candidates.Add(new QuoteCandidate(
                symbol,
                Text(quote, "longname") ?? Text(quote, "shortname") ?? symbol,
                Text(quote, "exchDisp") ?? Text(quote, "exchange") ?? string.Empty,
                Text(quote, "typeDisp") ?? Text(quote, "quoteType") ?? string.Empty));
        }
        return candidates;
    }

    public static QuoteSeries ParseChart(string symbol, string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        if (ChartError(document.RootElement) is string error)
        {
            throw new QuoteException(error);
        }
        JsonElement result = document.RootElement.GetProperty("chart").GetProperty("result")[0];
        JsonElement meta = result.GetProperty("meta");
        int offset = meta.TryGetProperty("gmtoffset", out JsonElement gmt) && gmt.ValueKind == JsonValueKind.Number ? gmt.GetInt32() : 0;
        List<PricePoint> points = [];
        if (result.TryGetProperty("timestamp", out JsonElement timestamps) && timestamps.ValueKind == JsonValueKind.Array)
        {
            JsonElement closes = result.GetProperty("indicators").GetProperty("quote")[0].GetProperty("close");
            int i = 0;
            foreach (JsonElement stamp in timestamps.EnumerateArray())
            {
                JsonElement close = i < closes.GetArrayLength() ? closes[i] : default;
                i++;
                if (close.ValueKind != JsonValueKind.Number || close.GetDouble() <= 0)
                {
                    continue;
                }
                DateOnly day = DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeSeconds(stamp.GetInt64() + offset).UtcDateTime);
                points.Add(PricePoint.At(day, Math.Round((decimal)close.GetDouble(), 4)));
            }
        }
        // The same day can come twice (the running day next to its last close); the later one wins
        points = [.. points.GroupBy(p => p.Day).Select(g => g.Last()).OrderBy(p => p.Day, StringComparer.Ordinal)];
        return new QuoteSeries(
            Text(meta, "symbol") ?? symbol,
            Text(meta, "longName") ?? Text(meta, "shortName") ?? symbol,
            Text(meta, "currency") ?? string.Empty,
            Text(meta, "fullExchangeName") ?? Text(meta, "exchangeName") ?? string.Empty,
            points);
    }

    private static string? ChartError(string body)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(body);
            return ChartError(document.RootElement);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? ChartError(JsonElement root) =>
        root.TryGetProperty("chart", out JsonElement chart) && chart.TryGetProperty("error", out JsonElement error) && error.ValueKind == JsonValueKind.Object
            ? Text(error, "description") ?? Text(error, "code") ?? "Yahoo did not know the symbol."
            : null;

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}

public class QuoteException : Exception
{
    public QuoteException(string message) : base(message)
    {
    }
}

/// <summary>
/// Quote currencies that are a fraction of a real one (London quotes in pence).
/// </summary>
public static class QuoteCurrency
{
    public static (string Currency, decimal Factor) Normalize(string currency) => currency switch
    {
        "GBp" or "GBX" => ("GBP", 0.01m),
        "ILA" => ("ILS", 0.01m),
        "ZAc" => ("ZAR", 0.01m),
        _ => (currency.ToUpperInvariant(), 1m)
    };

    /// <summary>The Yahoo symbol of the rate that turns one <paramref name="from"/> into <paramref name="to"/>.</summary>
    public static string FxSymbol(string from, string to) => $"{from}{to}=X".ToUpper(CultureInfo.InvariantCulture);
}
