namespace Akagi.Bridge.Japanese;

/// <summary>
/// How a verb conjugates. The special classes are the verbs whose forms break the pattern of their group.
/// Stored as numbers, so new classes go at the end.
/// </summary>
public enum VerbClass
{
    Godan = 0,
    Ichidan = 1,
    /// <summary>する and verbs ending in it (勉強する).</summary>
    Suru = 2,
    /// <summary>来る and verbs ending in it (持って来る).</summary>
    Kuru = 3,
    /// <summary>行く and its compounds: 行って, 行った.</summary>
    GodanIku = 4,
    /// <summary>ある: its negative is ない.</summary>
    GodanAru = 5,
    /// <summary>The honorific -aru verbs (いらっしゃる, おっしゃる, くださる, なさる, ござる): います, imperative い.</summary>
    GodanHonorific = 6,
    /// <summary>くれる: imperative くれ.</summary>
    IchidanKureru = 7
}

/// <summary>
/// The three groups learners are taught.
/// </summary>
public enum VerbGroup
{
    Godan,
    Ichidan,
    Irregular
}

/// <summary>
/// The forms that can be practised. Stored as numbers, so new forms go at the end.
/// </summary>
public enum ConjugationForm
{
    Negative = 0,
    Past = 1,
    Te = 2,
    Polite = 3,
    Potential = 4,
    Passive = 5,
    Causative = 6,
    Volitional = 7,
    Imperative = 8,
    /// <summary>The たら conditional.</summary>
    Conditional = 9,
    /// <summary>The ば conditional.</summary>
    Provisional = 10,
    NegativePast = 11,
    PolitePast = 12,
    PoliteNegative = 13,
    PolitePastNegative = 14,
    PoliteVolitional = 15,
    /// <summary>The たい form.</summary>
    Desire = 16,
    NegativeTe = 17,
    CausativePassive = 18
}

/// <summary>
/// A verb in its dictionary form. <see cref="Written"/> is how it is usually written (kanji or kana), <see cref="Reading"/>
/// is hiragana.
/// </summary>
public sealed record Verb(string Written, string Reading, VerbClass Class);

/// <summary>
/// One spelling of a conjugated verb.
/// </summary>
public readonly record struct Spelling(string Written, string Reading);

/// <summary>
/// A conjugated verb: the textbook answer first, then other answers that are also right (食べれる next to 食べられる).
/// </summary>
public sealed record Conjugation(IReadOnlyList<Spelling> Answers)
{
    public Spelling Main => Answers[0];
}

/// <summary>
/// The rule a conjugation follows, the unit learners struggle with: "て form of godan く verbs" rather than one verb.
/// </summary>
/// <param name="Key">Stable key, e.g. <c>Te:godan-く</c>.</param>
/// <param name="Label">What to show, e.g. <c>く → いて</c>.</param>
public readonly record struct ConjugationRule(string Key, string Label);

/// <summary>
/// Conjugates Japanese verbs and checks answers. Works on the kana ending, which is the same in the written form and in
/// the reading for every verb with okurigana; only 来る changes its reading under the kanji.
/// </summary>
public static class Conjugator
{
    public static readonly ConjugationForm[] AllForms = Enum.GetValues<ConjugationForm>();

    /// <summary>
    /// The forms in the order the settings list them: the plain forms first, then polite, then the rest.
    /// </summary>
    public static readonly ConjugationForm[] DisplayOrder =
    [
        ConjugationForm.Negative, ConjugationForm.Past, ConjugationForm.NegativePast, ConjugationForm.Te, ConjugationForm.NegativeTe,
        ConjugationForm.Polite, ConjugationForm.PoliteNegative, ConjugationForm.PolitePast, ConjugationForm.PolitePastNegative,
        ConjugationForm.PoliteVolitional, ConjugationForm.Volitional, ConjugationForm.Desire, ConjugationForm.Potential,
        ConjugationForm.Passive, ConjugationForm.Causative, ConjugationForm.CausativePassive, ConjugationForm.Imperative,
        ConjugationForm.Conditional, ConjugationForm.Provisional
    ];

    public static string FormName(ConjugationForm form) => form switch
    {
        ConjugationForm.Negative => "Negative",
        ConjugationForm.Past => "Past",
        ConjugationForm.Te => "て Form",
        ConjugationForm.Polite => "Polite",
        ConjugationForm.Potential => "Potential",
        ConjugationForm.Passive => "Passive",
        ConjugationForm.Causative => "Causative",
        ConjugationForm.Volitional => "Volitional",
        ConjugationForm.Imperative => "Imperative",
        ConjugationForm.Conditional => "Conditional (たら)",
        ConjugationForm.Provisional => "Provisional (ば)",
        ConjugationForm.NegativePast => "Negative Past",
        ConjugationForm.PolitePast => "Polite Past",
        ConjugationForm.PoliteNegative => "Polite Negative",
        ConjugationForm.PolitePastNegative => "Polite Past Negative",
        ConjugationForm.PoliteVolitional => "Polite Volitional",
        ConjugationForm.Desire => "たい Form",
        ConjugationForm.NegativeTe => "Negative て Form",
        ConjugationForm.CausativePassive => "Causative Passive",
        _ => form.ToString()
    };

    /// <summary>
    /// A model answer with 書く, shown next to the form's name so the name never has to be guessed.
    /// </summary>
    public static string FormExample(ConjugationForm form) =>
        Conjugate(new Verb("書く", "かく", VerbClass.Godan), form)?.Main.Reading ?? string.Empty;

    public static VerbGroup Group(VerbClass verbClass) => verbClass switch
    {
        VerbClass.Ichidan or VerbClass.IchidanKureru => VerbGroup.Ichidan,
        VerbClass.Suru or VerbClass.Kuru => VerbGroup.Irregular,
        _ => VerbGroup.Godan
    };

    public static string GroupName(VerbGroup group) => group switch
    {
        VerbGroup.Godan => "う-Verb (Godan)",
        VerbGroup.Ichidan => "る-Verb (Ichidan)",
        _ => "Irregular"
    };

    /// <summary>
    /// The type shown under a question; the special classes say what is special about them.
    /// </summary>
    public static string TypeLabel(VerbClass verbClass) => verbClass switch
    {
        VerbClass.Godan => "う-Verb (Godan)",
        VerbClass.Ichidan => "る-Verb (Ichidan)",
        VerbClass.Suru => "Irregular (する)",
        VerbClass.Kuru => "Irregular (来る)",
        VerbClass.GodanIku => "う-Verb (Godan, 行く exception)",
        VerbClass.GodanAru => "う-Verb (Godan, ある exception)",
        VerbClass.GodanHonorific => "う-Verb (Godan, honorific -aru)",
        VerbClass.IchidanKureru => "る-Verb (Ichidan, くれる exception)",
        _ => verbClass.ToString()
    };

    private const string URow = "うくぐすつぬぶむる";
    private const string ARow = "わかがさたなばまら";
    private const string IRow = "いきぎしちにびみり";
    private const string ERow = "えけげせてねべめれ";
    private const string ORow = "おこごそとのぼもろ";

    /// <summary>
    /// The verb in the given form, or null when the verb has no such form (ある has no potential) or the verb cannot
    /// be read (an ending that is not a verb ending).
    /// </summary>
    public static Conjugation? Conjugate(Verb verb, ConjugationForm form)
    {
        if (verb.Reading.Length == 0)
        {
            return null;
        }
        return verb.Class switch
        {
            VerbClass.Ichidan or VerbClass.IchidanKureru => Ichidan(verb, form),
            VerbClass.Suru => Suru(verb, form),
            VerbClass.Kuru => Kuru(verb, form),
            _ => Godan(verb, form)
        };
    }

    private static Conjugation? Godan(Verb verb, ConjugationForm form)
    {
        char last = verb.Reading[^1];
        int row = URow.IndexOf(last);
        if (row < 0 || (verb.Written.Length > 0 && verb.Written[^1] != last))
        {
            return null;
        }
        string a = ARow[row].ToString(), i = IRow[row].ToString(), e = ERow[row].ToString(), o = ORow[row].ToString();
        (string te, string ta) = last switch
        {
            'く' when verb.Class == VerbClass.GodanIku => ("って", "った"),
            'く' => ("いて", "いた"),
            'ぐ' => ("いで", "いだ"),
            'す' => ("して", "した"),
            'う' or 'つ' or 'る' => ("って", "った"),
            _ => ("んで", "んだ")
        };
        bool aru = verb.Class == VerbClass.GodanAru;
        bool honorific = verb.Class == VerbClass.GodanHonorific;
        if (honorific)
        {
            // いらっしゃいます, not いらっしゃります
            i = "い";
        }

        string[]? endings = form switch
        {
            ConjugationForm.Negative => aru ? null : [a + "ない"],
            ConjugationForm.NegativePast => aru ? null : [a + "なかった"],
            ConjugationForm.NegativeTe => aru ? null : [a + "なくて", a + "ないで"],
            ConjugationForm.Past => [ta],
            ConjugationForm.Te => [te],
            ConjugationForm.Conditional => [ta + "ら"],
            ConjugationForm.Polite => [i + "ます"],
            ConjugationForm.PolitePast => [i + "ました"],
            ConjugationForm.PoliteNegative => [i + "ません"],
            ConjugationForm.PolitePastNegative => [i + "ませんでした"],
            ConjugationForm.PoliteVolitional => [i + "ましょう"],
            ConjugationForm.Desire => aru ? [] : [i + "たい"],
            ConjugationForm.Potential => aru || honorific ? [] : [e + "る"],
            ConjugationForm.Passive => aru || honorific ? [] : [a + "れる"],
            ConjugationForm.Causative => aru || honorific ? [] : [a + "せる"],
            ConjugationForm.CausativePassive => aru || honorific ? [] : last == 'す' ? [a + "せられる"] : [a + "せられる", a + "される"],
            ConjugationForm.Volitional => [o + "う"],
            ConjugationForm.Imperative => aru ? [] : honorific ? [i] : [e],
            ConjugationForm.Provisional => [e + "ば"],
            _ => []
        };
        if (endings == null)
        {
            // ある: ない replaces the whole verb, with or without a prefix
            string whole = form switch
            {
                ConjugationForm.Negative => "ない",
                ConjugationForm.NegativePast => "なかった",
                _ => "なくて"
            };
            if (verb.Reading.Length < 2 || verb.Written.Length < 2)
            {
                return null;
            }
            // ある and 有る both end in two characters that ない replaces
            string writtenStem = verb.Written[..^2];
            string readingStem = verb.Reading[..^2];
            return new Conjugation([new Spelling(writtenStem + whole, readingStem + whole)]);
        }
        return Build(verb, 1, endings);
    }

    private static Conjugation? Ichidan(Verb verb, ConjugationForm form)
    {
        if (verb.Reading[^1] != 'る' || verb.Reading.Length < 2 || (verb.Written.Length > 0 && verb.Written[^1] != 'る'))
        {
            return null;
        }
        string[] endings = form switch
        {
            ConjugationForm.Negative => ["ない"],
            ConjugationForm.NegativePast => ["なかった"],
            ConjugationForm.NegativeTe => ["なくて", "ないで"],
            ConjugationForm.Past => ["た"],
            ConjugationForm.Te => ["て"],
            ConjugationForm.Conditional => ["たら"],
            ConjugationForm.Polite => ["ます"],
            ConjugationForm.PolitePast => ["ました"],
            ConjugationForm.PoliteNegative => ["ません"],
            ConjugationForm.PolitePastNegative => ["ませんでした"],
            ConjugationForm.PoliteVolitional => ["ましょう"],
            ConjugationForm.Desire => ["たい"],
            // 食べれる is everyday speech, so it counts
            ConjugationForm.Potential => ["られる", "れる"],
            ConjugationForm.Passive => ["られる"],
            ConjugationForm.Causative => ["させる"],
            ConjugationForm.CausativePassive => ["させられる"],
            ConjugationForm.Volitional => ["よう"],
            ConjugationForm.Imperative => verb.Class == VerbClass.IchidanKureru ? [string.Empty] : ["ろ", "よ"],
            ConjugationForm.Provisional => ["れば"],
            _ => []
        };
        return Build(verb, 1, endings);
    }

    private static Conjugation? Suru(Verb verb, ConjugationForm form)
    {
        if (!verb.Reading.EndsWith("する", StringComparison.Ordinal) || !verb.Written.EndsWith("する", StringComparison.Ordinal))
        {
            return null;
        }
        string[] endings = form switch
        {
            ConjugationForm.Negative => ["しない"],
            ConjugationForm.NegativePast => ["しなかった"],
            ConjugationForm.NegativeTe => ["しなくて", "しないで"],
            ConjugationForm.Past => ["した"],
            ConjugationForm.Te => ["して"],
            ConjugationForm.Conditional => ["したら"],
            ConjugationForm.Polite => ["します"],
            ConjugationForm.PolitePast => ["しました"],
            ConjugationForm.PoliteNegative => ["しません"],
            ConjugationForm.PolitePastNegative => ["しませんでした"],
            ConjugationForm.PoliteVolitional => ["しましょう"],
            ConjugationForm.Desire => ["したい"],
            ConjugationForm.Potential => ["できる"],
            ConjugationForm.Passive => ["される"],
            ConjugationForm.Causative => ["させる"],
            ConjugationForm.CausativePassive => ["させられる"],
            ConjugationForm.Volitional => ["しよう"],
            ConjugationForm.Imperative => ["しろ", "せよ"],
            ConjugationForm.Provisional => ["すれば"],
            _ => []
        };
        return Build(verb, 2, endings);
    }

    private static Conjugation? Kuru(Verb verb, ConjugationForm form)
    {
        if (!verb.Reading.EndsWith("くる", StringComparison.Ordinal))
        {
            return null;
        }
        // The kana that 来 is read as, then what follows it
        (string Stem, string Tail)[] endings = form switch
        {
            ConjugationForm.Negative => [("こ", "ない")],
            ConjugationForm.NegativePast => [("こ", "なかった")],
            ConjugationForm.NegativeTe => [("こ", "なくて"), ("こ", "ないで")],
            ConjugationForm.Past => [("き", "た")],
            ConjugationForm.Te => [("き", "て")],
            ConjugationForm.Conditional => [("き", "たら")],
            ConjugationForm.Polite => [("き", "ます")],
            ConjugationForm.PolitePast => [("き", "ました")],
            ConjugationForm.PoliteNegative => [("き", "ません")],
            ConjugationForm.PolitePastNegative => [("き", "ませんでした")],
            ConjugationForm.PoliteVolitional => [("き", "ましょう")],
            ConjugationForm.Desire => [("き", "たい")],
            ConjugationForm.Potential => [("こ", "られる"), ("こ", "れる")],
            ConjugationForm.Passive => [("こ", "られる")],
            ConjugationForm.Causative => [("こ", "させる")],
            ConjugationForm.CausativePassive => [("こ", "させられる")],
            ConjugationForm.Volitional => [("こ", "よう")],
            ConjugationForm.Imperative => [("こ", "い")],
            ConjugationForm.Provisional => [("く", "れば")],
            _ => []
        };
        if (endings.Length == 0)
        {
            return null;
        }
        bool kanji = verb.Written.EndsWith("来る", StringComparison.Ordinal);
        string writtenPrefix = verb.Written[..^2];
        string readingPrefix = verb.Reading[..^2];
        List<Spelling> answers = [];
        foreach ((string stem, string tail) in endings)
        {
            string reading = readingPrefix + stem + tail;
            string written = kanji ? writtenPrefix + "来" + tail : writtenPrefix + stem + tail;
            answers.Add(new Spelling(written, reading));
        }
        return new Conjugation(answers);
    }

    private static Conjugation? Build(Verb verb, int drop, string[] endings)
    {
        if (endings.Length == 0 || verb.Reading.Length < drop || verb.Written.Length < drop)
        {
            return null;
        }
        string written = verb.Written[..^drop];
        string reading = verb.Reading[..^drop];
        return new Conjugation([.. endings.Select(e => new Spelling(written + e, reading + e))]);
    }

    /// <summary>
    /// The rule behind a conjugation. Forms built on the て/た stem have one rule per ending; the others are the same
    /// for every godan verb except the う ones, whose あ-stem is わ.
    /// </summary>
    public static ConjugationRule Rule(Verb verb, ConjugationForm form)
    {
        string formKey = form.ToString();
        string pattern;
        string label;
        switch (verb.Class)
        {
            case VerbClass.Ichidan:
                (pattern, label) = ("ichidan", "る-verbs");
                break;
            case VerbClass.Suru:
                (pattern, label) = ("suru", "する");
                break;
            case VerbClass.Kuru:
                (pattern, label) = ("kuru", "来る");
                break;
            case VerbClass.IchidanKureru:
                (pattern, label) = ("kureru", "くれる");
                break;
            case VerbClass.GodanIku:
                (pattern, label) = ("iku", "行く");
                break;
            case VerbClass.GodanAru:
                (pattern, label) = ("aru", "ある");
                break;
            case VerbClass.GodanHonorific:
                (pattern, label) = ("honorific", "いらっしゃる, なさる...");
                break;
            default:
                char last = verb.Reading.Length > 0 ? verb.Reading[^1] : '?';
                bool teStem = form is ConjugationForm.Te or ConjugationForm.Past or ConjugationForm.Conditional;
                bool aStem = form is ConjugationForm.Negative or ConjugationForm.NegativePast or ConjugationForm.NegativeTe
                    or ConjugationForm.Passive or ConjugationForm.Causative or ConjugationForm.CausativePassive;
                if (teStem)
                {
                    Conjugation? sample = Conjugate(new Verb(last.ToString(), last.ToString(), VerbClass.Godan), ConjugationForm.Te);
                    string ending = sample?.Main.Reading ?? "?";
                    string group = last is 'う' or 'つ' or 'る' ? "うつる" : last is 'ぬ' or 'ぶ' or 'む' ? "ぬぶむ" : last.ToString();
                    pattern = "godan-" + group;
                    label = group.Length > 1 ? $"{string.Join("/", group.ToCharArray())} → {ending}" : $"{last} → {ending}";
                }
                else if (aStem && last == 'う')
                {
                    (pattern, label) = ("godan-う", "う-verbs (う → わ)");
                }
                else
                {
                    (pattern, label) = ("godan", "う-verbs");
                }
                break;
        }
        return new ConjugationRule($"{formKey}:{pattern}", label);
    }

    /// <summary>
    /// Whether an answer spells one of the conjugation's answers, in kana, katakana, romaji or the written form.
    /// </summary>
    public static bool IsCorrect(Conjugation conjugation, string answer)
    {
        string typed = Kana.Normalize(answer);
        if (typed.Length == 0)
        {
            return false;
        }
        string raw = answer.Trim();
        return conjugation.Answers.Any(a => a.Reading == typed || a.Written == raw);
    }

    /// <summary>
    /// For a reverse question: whether the answer is a dictionary form that gives the shown conjugation. Accepts the
    /// verb itself and any other verb that conjugates to the same kana (かった is 買う, 勝つ or 刈る).
    /// </summary>
    public static bool IsDictionaryFormOf(Verb verb, ConjugationForm form, string answer)
    {
        string raw = answer.Trim();
        string typed = Kana.Normalize(answer);
        if (typed.Length == 0)
        {
            return false;
        }
        if (typed == verb.Reading || raw == verb.Written)
        {
            return true;
        }
        Conjugation? shown = Conjugate(verb, form);
        if (shown == null)
        {
            return false;
        }
        HashSet<string> readings = [.. shown.Answers.Select(a => a.Reading)];
        foreach (VerbClass candidate in PossibleClasses(typed))
        {
            Conjugation? other = Conjugate(new Verb(typed, typed, candidate), form);
            if (other != null && other.Answers.Any(a => readings.Contains(a.Reading)))
            {
                return true;
            }
        }
        return false;
    }

    private static readonly string[] HonorificVerbs = ["いらっしゃる", "おっしゃる", "くださる", "なさる", "ござる"];

    /// <summary>
    /// The classes a dictionary form could belong to, judged by its kana alone; the exceptions only for the verbs they
    /// are exceptions for.
    /// </summary>
    public static IEnumerable<VerbClass> PossibleClasses(string reading)
    {
        yield return VerbClass.Godan;
        if (reading.EndsWith('る'))
        {
            yield return VerbClass.Ichidan;
        }
        if (reading.EndsWith("する", StringComparison.Ordinal))
        {
            yield return VerbClass.Suru;
        }
        if (reading.EndsWith("くる", StringComparison.Ordinal))
        {
            yield return VerbClass.Kuru;
        }
        if (reading.EndsWith("いく", StringComparison.Ordinal) || reading.EndsWith("ゆく", StringComparison.Ordinal))
        {
            yield return VerbClass.GodanIku;
        }
        if (reading == "ある")
        {
            yield return VerbClass.GodanAru;
        }
        if (HonorificVerbs.Any(h => reading.EndsWith(h, StringComparison.Ordinal)))
        {
            yield return VerbClass.GodanHonorific;
        }
        if (reading.EndsWith("くれる", StringComparison.Ordinal))
        {
            yield return VerbClass.IchidanKureru;
        }
    }
}
