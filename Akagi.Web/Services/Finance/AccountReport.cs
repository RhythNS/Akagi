using Akagi.Web.Models.Finance;

namespace Akagi.Web.Services.Finance;

/// <summary>Value at the end of a day and the net money put in up to then.</summary>
public readonly record struct ValuePoint(DateOnly Day, decimal Value, decimal Invested)
{
    public decimal Gain => Value - Invested;
}

/// <summary>Money into an account (positive) or out of it (negative) on a day.</summary>
public readonly record struct CashFlow(DateOnly Day, decimal Amount);

/// <summary>
/// What any kind of account reports to the overview: its value now, what was put in, and a daily series. Gains over a
/// timeframe come from these alone (<see cref="Performance"/>), so every account kind is compared the same way.
/// </summary>
public sealed class AccountReport
{
    public required IFinanceAccount Account { get; init; }
    public required string Link { get; init; }
    public decimal Value { get; init; }
    /// <summary>Net money put in so far.</summary>
    public decimal Invested { get; init; }
    public decimal Gain => Value - Invested;
    /// <summary>Daily, from the day before the first activity to today; empty when nothing happened yet.</summary>
    public List<ValuePoint> Series { get; init; } = [];
    public List<CashFlow> Flows { get; init; } = [];
    public List<string> Warnings { get; init; } = [];
    /// <summary>One line about the account for lists ("6 securities, 2 pending trades").</summary>
    public string Summary { get; init; } = string.Empty;
    /// <summary>Things waiting for the user, like pending trades.</summary>
    public int Pending { get; init; }

    /// <summary>
    /// Several accounts as one: values and money put in add up per day, an account counting 0 before its first day and
    /// keeping its last value after its series ends.
    /// </summary>
    public static (List<ValuePoint> Series, List<CashFlow> Flows) Combine(IEnumerable<AccountReport> reports)
    {
        List<AccountReport> list = [.. reports.Where(r => r.Series.Count > 0)];
        List<CashFlow> flows = [.. list.SelectMany(r => r.Flows).OrderBy(f => f.Day)];
        if (list.Count == 0)
        {
            return ([], flows);
        }
        DateOnly from = list.Min(r => r.Series[0].Day);
        DateOnly to = list.Max(r => r.Series[^1].Day);
        List<ValuePoint> series = [];
        int[] index = new int[list.Count];
        for (DateOnly day = from; day <= to; day = day.AddDays(1))
        {
            decimal value = 0;
            decimal invested = 0;
            for (int i = 0; i < list.Count; i++)
            {
                List<ValuePoint> points = list[i].Series;
                while (index[i] + 1 < points.Count && points[index[i] + 1].Day <= day)
                {
                    index[i]++;
                }
                if (points[index[i]].Day <= day)
                {
                    value += points[index[i]].Value;
                    invested += points[index[i]].Invested;
                }
            }
            series.Add(new ValuePoint(day, value, invested));
        }
        return (series, flows);
    }
}

/// <summary>A named stretch of time back from today.</summary>
public sealed record Timeframe(string Key, string Label, Func<DateOnly, DateOnly> StartFor)
{
    public static readonly Timeframe Week = new("1w", "1 week", today => today.AddDays(-7));
    public static readonly Timeframe Month = new("1m", "1 month", today => today.AddMonths(-1));
    public static readonly Timeframe Quarter = new("3m", "3 months", today => today.AddMonths(-3));
    public static readonly Timeframe Half = new("6m", "6 months", today => today.AddMonths(-6));
    public static readonly Timeframe YearToDate = new("ytd", "This year", today => new DateOnly(today.Year, 1, 1).AddDays(-1));
    public static readonly Timeframe Year = new("1y", "1 year", today => today.AddYears(-1));
    public static readonly Timeframe ThreeYears = new("3y", "3 years", today => today.AddYears(-3));
    public static readonly Timeframe All = new("all", "All time", _ => DateOnly.MinValue);

    public static IReadOnlyList<Timeframe> Presets { get; } = [Week, Month, Quarter, Half, YearToDate, Year, ThreeYears, All];

    public static Timeframe Find(string? key) => Presets.FirstOrDefault(t => t.Key == key) ?? Year;
}

/// <summary>How an account did over a timeframe: value change minus the money put in during it.</summary>
public sealed record PeriodResult(DateOnly From, DateOnly To, decimal StartValue, decimal EndValue, decimal NetFlow, decimal Gain, decimal? Percent);

public static class Performance
{
    /// <summary>
    /// Gain from the end of <paramref name="from"/> to the end of <paramref name="to"/>. The percentage is the
    /// modified Dietz return: money put in during the timeframe counts for the part of it it was there.
    /// A start before the series means from nothing.
    /// </summary>
    public static PeriodResult Over(IReadOnlyList<ValuePoint> series, IReadOnlyList<CashFlow> flows, DateOnly from, DateOnly to)
    {
        if (series.Count == 0)
        {
            return new PeriodResult(from, to, 0, 0, 0, 0, null);
        }
        if (from < series[0].Day)
        {
            from = series[0].Day.AddDays(-1);
        }
        decimal start = ValueOn(series, from);
        decimal end = ValueOn(series, to);
        List<CashFlow> inside = [.. flows.Where(f => f.Day > from && f.Day <= to)];
        decimal net = inside.Sum(f => f.Amount);
        decimal gain = end - start - net;
        decimal length = to.DayNumber - from.DayNumber;
        decimal weighted = length <= 0 ? 0 : inside.Sum(f => f.Amount * (to.DayNumber - f.Day.DayNumber) / length);
        decimal basis = start + weighted;
        // A basis this small (all money out early) would turn rounding into huge percentages
        decimal? percent = basis > 0.01m ? gain / basis * 100 : null;
        return new PeriodResult(from, to, start, end, net, gain, percent);
    }

    public static PeriodResult Over(IReadOnlyList<ValuePoint> series, IReadOnlyList<CashFlow> flows, Timeframe timeframe, DateOnly today) =>
        Over(series, flows, timeframe.StartFor(today), today);

    /// <summary>The value at the end of the day: the latest point on or before it, 0 before the series.</summary>
    public static decimal ValueOn(IReadOnlyList<ValuePoint> series, DateOnly day)
    {
        decimal value = 0;
        foreach (ValuePoint point in series)
        {
            if (point.Day > day)
            {
                break;
            }
            value = point.Value;
        }
        return value;
    }

    /// <summary>At most <paramref name="max"/> points for a chart, always keeping the first and last.</summary>
    public static List<ValuePoint> Thin(IReadOnlyList<ValuePoint> series, int max)
    {
        if (series.Count <= max || max < 2)
        {
            return [.. series];
        }
        List<ValuePoint> thinned = [];
        double step = (series.Count - 1) / (double)(max - 1);
        for (int i = 0; i < max; i++)
        {
            thinned.Add(series[(int)Math.Round(i * step)]);
        }
        return thinned;
    }
}
