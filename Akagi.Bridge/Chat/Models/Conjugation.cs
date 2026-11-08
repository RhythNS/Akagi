using Akagi.Bridge.Japanese;
using MessagePack;

namespace Akagi.Bridge.Chat.Models;

/// <summary>
/// Which verbs a practice session draws from.
/// </summary>
public enum ConjugationPool
{
    /// <summary>The most common verbs in the dictionary, up to <see cref="ConjugationSettingsView.CommonLimit"/>.</summary>
    Common,
    /// <summary>Verbs that came up in the user's calls.</summary>
    Calls,
    Both
}

/// <summary>
/// How questions are chosen.
/// </summary>
public enum ConjugationMode
{
    /// <summary>Everything, with what goes wrong coming back more often.</summary>
    Mixed,
    /// <summary>Mostly what goes wrong, new items now and then.</summary>
    Focus,
    /// <summary>Only items and rules that currently go wrong.</summary>
    TroubleOnly
}

/// <summary>
/// How an item, a form, a rule or a verb is going.
/// </summary>
public enum ConjugationStatus
{
    New,
    Learning,
    Trouble,
    Fine
}

/// <summary>
/// A verb the practice can ask about.
/// </summary>
[MessagePackObject]
public class ConjugationVerbView
{
    /// <summary>
    /// Written form and reading, e.g. <c>書く[かく]</c>; progress is stored under it.
    /// </summary>
    [Key(0)]
    public string Id { get; set; } = string.Empty;
    /// <summary>
    /// How the verb is usually written: kanji, or kana for verbs usually written in kana (する, いる).
    /// </summary>
    [Key(1)]
    public string Written { get; set; } = string.Empty;
    [Key(2)]
    public string Reading { get; set; } = string.Empty;
    [Key(3)]
    public VerbClass Class { get; set; }
    [Key(4)]
    public string Meaning { get; set; } = string.Empty;
    /// <summary>
    /// Approximate frequency rank among all words; <see cref="int.MaxValue"/> when it is not a common word.
    /// </summary>
    [Key(5)]
    public int Rank { get; set; } = int.MaxValue;
    /// <summary>
    /// How often the user said it in their calls.
    /// </summary>
    [Key(6)]
    public int LearnerUses { get; set; }
    /// <summary>
    /// How often their partners said it.
    /// </summary>
    [Key(7)]
    public int PartnerUses { get; set; }

    public static string MakeId(string written, string reading) => $"{written}[{reading}]";
}

/// <summary>
/// Progress on one question: a verb in a form, asked one way round.
/// </summary>
[MessagePackObject]
public class ConjugationItemView
{
    [Key(0)]
    public string Verb { get; set; } = string.Empty;
    [Key(1)]
    public ConjugationForm Form { get; set; }
    /// <summary>
    /// Asked from the conjugated form back to the dictionary form.
    /// </summary>
    [Key(2)]
    public bool Reverse { get; set; }
    [Key(3)]
    public int Attempts { get; set; }
    [Key(4)]
    public int Correct { get; set; }
    /// <summary>
    /// The latest results, oldest first: 1 right, 0 wrong. At most <see cref="ConjugationProgress.RecentLength"/>.
    /// </summary>
    [Key(5)]
    public string Recent { get; set; } = string.Empty;
    [Key(6)]
    public DateTime LastSeen { get; set; }
    /// <summary>
    /// The latest wrong answers, newest last.
    /// </summary>
    [Key(7)]
    public string[] WrongAnswers { get; set; } = [];
    [Key(8)]
    public DateTime? LastWrong { get; set; }
    [Key(9)]
    public string Written { get; set; } = string.Empty;
    [Key(10)]
    public string Reading { get; set; } = string.Empty;
    [Key(11)]
    public VerbClass Class { get; set; }

    public string Key => ConjugationProgress.ItemKey(Verb, Form, Reverse);
}

/// <summary>
/// One answer, for the list of recent mistakes.
/// </summary>
[MessagePackObject]
public class ConjugationAttemptView
{
    [Key(0)]
    public string Verb { get; set; } = string.Empty;
    [Key(1)]
    public string Written { get; set; } = string.Empty;
    [Key(2)]
    public string Reading { get; set; } = string.Empty;
    [Key(3)]
    public VerbClass Class { get; set; }
    [Key(4)]
    public ConjugationForm Form { get; set; }
    [Key(5)]
    public bool Reverse { get; set; }
    [Key(6)]
    public string Answer { get; set; } = string.Empty;
    [Key(7)]
    public bool Correct { get; set; }
    [Key(8)]
    public DateTime At { get; set; }
    /// <summary>
    /// How long the answer took, in milliseconds.
    /// </summary>
    [Key(9)]
    public int Millis { get; set; }
}

/// <summary>
/// Answers on one day, in the user's time zone.
/// </summary>
[MessagePackObject]
public class ConjugationDayView
{
    [Key(0)]
    public DateTime Date { get; set; }
    [Key(1)]
    public int Attempts { get; set; }
    [Key(2)]
    public int Correct { get; set; }
}

/// <summary>
/// The practice settings, kept with the user's progress so they follow them to any browser.
/// </summary>
[MessagePackObject]
public class ConjugationSettingsView
{
    public static readonly ConjugationForm[] DefaultForms =
        [ConjugationForm.Negative, ConjugationForm.Past, ConjugationForm.Te, ConjugationForm.Polite];

    [Key(0)]
    public ConjugationForm[] Forms { get; set; } = [.. DefaultForms];
    [Key(1)]
    public bool Godan { get; set; } = true;
    [Key(2)]
    public bool Ichidan { get; set; } = true;
    [Key(3)]
    public bool Irregular { get; set; } = true;
    [Key(4)]
    public ConjugationPool Pool { get; set; } = ConjugationPool.Common;
    /// <summary>
    /// Only verbs within this frequency rank count as common.
    /// </summary>
    [Key(5)]
    public int CommonLimit { get; set; } = 5_000;
    [Key(6)]
    public ConjugationMode Mode { get; set; } = ConjugationMode.Mixed;
    /// <summary>
    /// Show the conjugated form and ask for the dictionary form.
    /// </summary>
    [Key(7)]
    public bool Reverse { get; set; }
    /// <summary>
    /// Ask each question either way round at random.
    /// </summary>
    [Key(8)]
    public bool Randomize { get; set; }
    [Key(9)]
    public bool ShowKanji { get; set; } = true;
    [Key(10)]
    public bool ShowFurigana { get; set; }
    [Key(11)]
    public bool HideType { get; set; }
    [Key(12)]
    public bool ShowEnglish { get; set; }

    public ConjugationSettingsView Copy() => (ConjugationSettingsView)MemberwiseClone();
}

/// <summary>
/// Rules for reading progress, shared by the bot and the page so both call the same things fine and troubled.
/// </summary>
public static class ConjugationProgress
{
    /// <summary>
    /// How many of the latest answers an item remembers.
    /// </summary>
    public const int RecentLength = 10;

    /// <summary>
    /// How many wrong answers an item remembers.
    /// </summary>
    public const int WrongAnswerLength = 5;

    public static string ItemKey(string verb, ConjugationForm form, bool reverse) => $"{verb}|{(int)form}|{(reverse ? 1 : 0)}";

    /// <summary>
    /// Adds a result to the latest results, dropping the oldest beyond <see cref="RecentLength"/>.
    /// </summary>
    public static string Push(string recent, bool correct)
    {
        string next = recent + (correct ? '1' : '0');
        return next.Length > RecentLength ? next[^RecentLength..] : next;
    }

    /// <summary>
    /// Trouble: the latest answer was wrong, or under three in four of the latest were right. Fine: three answers or
    /// more, the latest right and at least 85% of the latest right. Everything in between is still being learned.
    /// </summary>
    public static ConjugationStatus Status(string recent)
    {
        if (recent.Length == 0)
        {
            return ConjugationStatus.New;
        }
        double rate = Rate(recent);
        if (recent[^1] == '0' || (recent.Length >= 3 && rate < 0.75))
        {
            return ConjugationStatus.Trouble;
        }
        if (recent.Length >= 3 && rate >= 0.85)
        {
            return ConjugationStatus.Fine;
        }
        return ConjugationStatus.Learning;
    }

    /// <summary>
    /// The status of a group (a form, a rule, a verb) from its items' latest results. A group is in trouble when
    /// under three in four of its latest answers were right, or when a quarter of its practised items are.
    /// </summary>
    public static ConjugationStatus GroupStatus(IEnumerable<string> recents)
    {
        List<string> practised = [.. recents.Where(r => r.Length > 0)];
        if (practised.Count == 0)
        {
            return ConjugationStatus.New;
        }
        int answers = practised.Sum(r => r.Length);
        double rate = practised.Sum(r => r.Count(c => c == '1')) / (double)answers;
        int troubled = practised.Count(r => Status(r) == ConjugationStatus.Trouble);
        if ((answers >= 3 && rate < 0.75) || (troubled > 0 && troubled * 4 >= practised.Count))
        {
            return ConjugationStatus.Trouble;
        }
        if (answers >= 5 && rate >= 0.85 && troubled == 0)
        {
            return ConjugationStatus.Fine;
        }
        return ConjugationStatus.Learning;
    }

    public static double Rate(string recent) => recent.Length == 0 ? 0 : recent.Count(c => c == '1') / (double)recent.Length;
}
