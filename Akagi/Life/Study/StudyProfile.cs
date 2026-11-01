namespace Akagi.Life.Study;

/// <summary>
/// A vocabulary item scheduled with a five box Leitner system.
/// </summary>
internal class VocabItem
{
    public static readonly int[] BoxIntervalsDays = [1, 3, 7, 14, 30];
    public const int KnownBox = 4;

    public string Term { get; set; } = string.Empty;
    public string Reading { get; set; } = string.Empty;
    public string Meaning { get; set; } = string.Empty;
    public int Box { get; set; } = 1;
    public int Seen { get; set; }
    public int Correct { get; set; }
    public DateTime AddedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastReviewedAt { get; set; }
    public DateTime NextReviewAt { get; set; } = DateTime.UtcNow;

    public bool IsKnown => Box >= KnownBox;

    public bool IsDue(DateTime nowUtc) => NextReviewAt <= nowUtc;

    public void Review(bool correct, DateTime nowUtc)
    {
        Seen++;
        if (correct)
        {
            Correct++;
            Box = Math.Min(Box + 1, BoxIntervalsDays.Length);
        }
        else
        {
            Box = 1;
        }
        LastReviewedAt = nowUtc;
        NextReviewAt = nowUtc.AddDays(BoxIntervalsDays[Box - 1]);
    }
}

internal class GrammarMistake
{
    public string Pattern { get; set; } = string.Empty;
    public string Explanation { get; set; } = string.Empty;
    public string LastExample { get; set; } = string.Empty;
    public string Correction { get; set; } = string.Empty;
    public int Count { get; set; }
    public DateTime FirstSeen { get; set; } = DateTime.UtcNow;
    public DateTime LastSeen { get; set; } = DateTime.UtcNow;
}

internal class StudyDay
{
    public string Date { get; set; } = string.Empty;
    public int Minutes { get; set; }
    public int Reviews { get; set; }
    public int NewItems { get; set; }
    public List<string> Activities { get; set; } = [];

    public bool Active => Minutes > 0 || Reviews > 0 || NewItems > 0;
}

internal class StudyProfile
{
    public const int MaxDays = 400;

    public string TargetLevel { get; set; } = "N2";
    /// <summary>
    /// Approximate vocabulary size expected for the target level.
    /// </summary>
    public int TargetVocabulary { get; set; } = 6000;
    public List<VocabItem> Vocabulary { get; set; } = [];
    public List<GrammarMistake> Grammar { get; set; } = [];
    public List<StudyDay> Days { get; set; } = [];

    public VocabItem? FindVocab(string term)
    {
        string trimmed = term.Trim();
        return Vocabulary.FirstOrDefault(v => string.Equals(v.Term, trimmed, StringComparison.OrdinalIgnoreCase))
            ?? Vocabulary.FirstOrDefault(v => string.Equals(v.Reading, trimmed, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Adds a word or refreshes its reading and meaning. Returns true when the word is new.
    /// </summary>
    public bool AddVocab(string term, string reading, string meaning, DateTime nowUtc, out VocabItem item)
    {
        VocabItem? existing = FindVocab(term);
        if (existing != null)
        {
            if (!string.IsNullOrWhiteSpace(reading))
            {
                existing.Reading = reading.Trim();
            }
            if (!string.IsNullOrWhiteSpace(meaning))
            {
                existing.Meaning = meaning.Trim();
            }
            item = existing;
            return false;
        }

        item = new VocabItem
        {
            Term = term.Trim(),
            Reading = reading.Trim(),
            Meaning = meaning.Trim(),
            AddedAt = nowUtc,
            NextReviewAt = nowUtc.AddDays(VocabItem.BoxIntervalsDays[0])
        };
        Vocabulary.Add(item);
        return true;
    }

    public GrammarMistake RecordMistake(string pattern, string explanation, string example, string correction, DateTime nowUtc)
    {
        GrammarMistake? mistake = Grammar.FirstOrDefault(g => string.Equals(g.Pattern, pattern.Trim(), StringComparison.OrdinalIgnoreCase));
        if (mistake == null)
        {
            mistake = new GrammarMistake { Pattern = pattern.Trim(), FirstSeen = nowUtc };
            Grammar.Add(mistake);
        }
        mistake.Count++;
        mistake.LastSeen = nowUtc;
        if (!string.IsNullOrWhiteSpace(explanation))
        {
            mistake.Explanation = explanation.Trim();
        }
        if (!string.IsNullOrWhiteSpace(example))
        {
            mistake.LastExample = example.Trim();
        }
        if (!string.IsNullOrWhiteSpace(correction))
        {
            mistake.Correction = correction.Trim();
        }
        return mistake;
    }

    public StudyDay GetDay(string date)
    {
        StudyDay? day = Days.FirstOrDefault(d => d.Date == date);
        if (day == null)
        {
            day = new StudyDay { Date = date };
            Days.Add(day);
            Days.Sort((a, b) => string.CompareOrdinal(a.Date, b.Date));
            if (Days.Count > MaxDays)
            {
                Days.RemoveRange(0, Days.Count - MaxDays);
            }
        }
        return day;
    }
}

internal class StudySummary
{
    /// <summary>
    /// Marks the <see cref="StudyDay.Activities"/> entries of finished lessons.
    /// </summary>
    public const string LessonActivityPrefix = "Lesson: ";

    public int Streak { get; init; }
    public bool StudiedToday { get; init; }
    public int VocabularyCount { get; init; }
    public int KnownCount { get; init; }
    public int DueCount { get; init; }
    public int TargetVocabulary { get; init; }
    public string TargetLevel { get; init; } = "N2";
    public int MinutesThisWeek { get; init; }
    public IReadOnlyList<GrammarMistake> TopMistakes { get; init; } = [];
    /// <summary>
    /// The latest lesson within the last week, with its date.
    /// </summary>
    public string? LastLesson { get; init; }

    public bool HasData => VocabularyCount > 0 || TopMistakes.Count > 0 || Streak > 0 || MinutesThisWeek > 0 || LastLesson != null;

    public double ProgressPercent => TargetVocabulary <= 0 ? 0 : Math.Min(100, 100.0 * KnownCount / TargetVocabulary);

    public static StudySummary Create(LifeProfile profile, DateTime nowUtc)
    {
        StudyProfile study = profile.Study;
        DateTime localNow = profile.ToLocal(nowUtc);
        HashSet<string> activeDays = [.. study.Days.Where(d => d.Active).Select(d => d.Date)];

        string today = LifeClock.DateKey(localNow);
        bool studiedToday = activeDays.Contains(today);

        // A streak stays alive until the end of today even if today has no study yet
        int streak = 0;
        DateTime cursor = studiedToday ? localNow.Date : localNow.Date.AddDays(-1);
        while (activeDays.Contains(LifeClock.DateKey(cursor)))
        {
            streak++;
            cursor = cursor.AddDays(-1);
        }

        string weekStart = LifeClock.DateKey(localNow.AddDays(-6));
        return new StudySummary
        {
            Streak = streak,
            StudiedToday = studiedToday,
            VocabularyCount = study.Vocabulary.Count,
            KnownCount = study.Vocabulary.Count(v => v.IsKnown),
            DueCount = study.Vocabulary.Count(v => v.IsDue(nowUtc)),
            TargetVocabulary = study.TargetVocabulary,
            TargetLevel = study.TargetLevel,
            MinutesThisWeek = study.Days.Where(d => string.CompareOrdinal(d.Date, weekStart) >= 0).Sum(d => d.Minutes),
            TopMistakes = [.. study.Grammar.OrderByDescending(g => g.Count).ThenByDescending(g => g.LastSeen).Take(3)],
            LastLesson = FindLastLesson(study, weekStart)
        };
    }

    private static string? FindLastLesson(StudyProfile study, string since)
    {
        StudyDay? day = study.Days.LastOrDefault(d => string.CompareOrdinal(d.Date, since) >= 0
            && d.Activities.Any(a => a.StartsWith(LessonActivityPrefix, StringComparison.Ordinal)));
        string? lesson = day?.Activities.Last(a => a.StartsWith(LessonActivityPrefix, StringComparison.Ordinal));
        return lesson == null ? null : $"{lesson[LessonActivityPrefix.Length..]} on {day!.Date}";
    }

    public string Describe()
    {
        List<string> lines = [];
        string streak = Streak > 0 ? $"study streak {Streak} day(s){(StudiedToday ? string.Empty : ", not studied yet today")}" : "no active study streak";
        lines.Add($"Japanese ({TargetLevel} goal): {streak}; {MinutesThisWeek} min this week");
        if (VocabularyCount > 0)
        {
            lines.Add($"Vocabulary: {VocabularyCount} words tracked, {KnownCount} known ({ProgressPercent:0.#}% of ~{TargetVocabulary} for {TargetLevel}), {DueCount} due for review");
        }
        if (TopMistakes.Count > 0)
        {
            lines.Add("Recurring grammar mistakes: " + string.Join("; ", TopMistakes.Select(m => $"{m.Pattern} x{m.Count}")));
        }
        if (LastLesson != null)
        {
            lines.Add($"Last lesson with their tutor: {LastLesson}");
        }
        return string.Join(Environment.NewLine, lines);
    }
}
