namespace Akagi.Life.Mood;

internal enum MoodTrend
{
    Unknown,
    Falling,
    Steady,
    Rising
}

internal class MoodSnapshot
{
    public double? Valence { get; init; }
    public double? Energy { get; init; }
    public double? Stress { get; init; }
    public DateTime? LastEntryAt { get; init; }
    public string? LastNote { get; init; }
    public MoodTrend Trend { get; init; }
    public IReadOnlyList<MoodEntry> RecentVictories { get; init; } = [];
    public IReadOnlyList<Concern> OpenConcerns { get; init; } = [];

    public bool HasData => Valence.HasValue || Energy.HasValue || Stress.HasValue;
}

/// <summary>
/// Summarizes the mood journal. Newer observations weigh more (half life of 12 hours), anything older than
/// three days is ignored for the current state but still used for the trend.
/// </summary>
internal static class MoodAnalyzer
{
    public static readonly TimeSpan HalfLife = TimeSpan.FromHours(12);
    public static readonly TimeSpan CurrentWindow = TimeSpan.FromDays(3);
    public static readonly TimeSpan VictoryWindow = TimeSpan.FromDays(7);

    public static MoodSnapshot Analyze(LifeProfile profile, DateTime nowUtc)
    {
        List<MoodEntry> current = [.. profile.Mood
            .Where(m => m.Timestamp <= nowUtc && nowUtc - m.Timestamp <= CurrentWindow)
            .OrderBy(m => m.Timestamp)];

        MoodEntry? last = profile.Mood.Where(m => m.Timestamp <= nowUtc).MaxBy(m => m.Timestamp);

        return new MoodSnapshot
        {
            Valence = WeightedAverage(current, m => m.Valence, nowUtc),
            Energy = WeightedAverage(current, m => m.Energy, nowUtc),
            Stress = WeightedAverage(current, m => m.Stress, nowUtc),
            LastEntryAt = last?.Timestamp,
            LastNote = last?.Note,
            Trend = ComputeTrend(profile.Mood, nowUtc),
            RecentVictories = [.. profile.Mood
                .Where(m => m.Kind == MoodKind.Victory && m.Timestamp <= nowUtc && nowUtc - m.Timestamp <= VictoryWindow)
                .OrderByDescending(m => m.Timestamp)
                .Take(3)],
            OpenConcerns = [.. profile.Concerns
                .Where(c => !c.Resolved)
                .OrderBy(c => c.FollowUpAt ?? DateTime.MaxValue)
                .ThenByDescending(c => c.CreatedAt)
                .Take(5)]
        };
    }

    private static double? WeightedAverage(List<MoodEntry> entries, Func<MoodEntry, int?> selector, DateTime nowUtc)
    {
        double weightSum = 0;
        double valueSum = 0;
        foreach (MoodEntry entry in entries)
        {
            int? value = selector(entry);
            if (!value.HasValue)
            {
                continue;
            }
            double ageHours = (nowUtc - entry.Timestamp).TotalHours;
            double weight = Math.Pow(0.5, ageHours / HalfLife.TotalHours);
            weightSum += weight;
            valueSum += weight * value.Value;
        }
        return weightSum > 0 ? Math.Round(valueSum / weightSum, 1) : null;
    }

    /// <summary>
    /// Compares the average valence of the last 24 hours with the two days before.
    /// </summary>
    private static MoodTrend ComputeTrend(List<MoodEntry> entries, DateTime nowUtc)
    {
        double[] recent = [.. entries
            .Where(m => m.Valence.HasValue && m.Timestamp <= nowUtc && nowUtc - m.Timestamp <= TimeSpan.FromHours(24))
            .Select(m => (double)m.Valence!.Value)];
        double[] earlier = [.. entries
            .Where(m => m.Valence.HasValue && nowUtc - m.Timestamp > TimeSpan.FromHours(24) && nowUtc - m.Timestamp <= TimeSpan.FromHours(72))
            .Select(m => (double)m.Valence!.Value)];

        if (recent.Length == 0 || earlier.Length == 0)
        {
            return MoodTrend.Unknown;
        }

        double delta = recent.Average() - earlier.Average();
        return delta <= -1.5 ? MoodTrend.Falling
            : delta >= 1.5 ? MoodTrend.Rising
            : MoodTrend.Steady;
    }
}
