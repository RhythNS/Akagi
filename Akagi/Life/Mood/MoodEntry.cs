namespace Akagi.Life.Mood;

internal enum MoodKind
{
    CheckIn,
    Victory,
    Worry,
    Event
}

/// <summary>
/// One observation of how the user feels. Scales: valence -5 (awful) to +5 (great), energy and stress 0 to 10.
/// Any dimension may be unknown.
/// </summary>
internal class MoodEntry
{
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public MoodKind Kind { get; set; } = MoodKind.CheckIn;
    public int? Valence { get; set; }
    public int? Energy { get; set; }
    public int? Stress { get; set; }
    public string Note { get; set; } = string.Empty;
    public string? CharacterId { get; set; }

    public static int? ClampValence(int? value) => value.HasValue ? Math.Clamp(value.Value, -5, 5) : null;
    public static int? ClampLevel(int? value) => value.HasValue ? Math.Clamp(value.Value, 0, 10) : null;
}

/// <summary>
/// Something the user is worried about or looking forward to, which the companion should follow up on later.
/// </summary>
internal class Concern
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..6];
    public MoodKind Kind { get; set; } = MoodKind.Worry;
    public string Text { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? FollowUpAt { get; set; }
    public DateTime? FollowedUpAt { get; set; }
    public bool Resolved { get; set; }
    public string Outcome { get; set; } = string.Empty;

    public bool IsDueForFollowUp(DateTime nowUtc) =>
        !Resolved && FollowUpAt.HasValue && FollowUpAt.Value <= nowUtc && FollowedUpAt == null;
}
