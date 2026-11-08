using Akagi.Bridge.Chat.Models;
using Akagi.Bridge.Japanese;

namespace Akagi.Web.Pages.Learning.Conjugations;

/// <summary>
/// One question: a verb, a form, and which way round it is asked.
/// </summary>
public sealed record DrillQuestion(ConjugationVerbView Verb, ConjugationForm Form, bool Reverse, Conjugation Answer, ConjugationRule Rule)
{
    public Verb Dictionary => new(Verb.Written, Verb.Reading, Verb.Class);
    public string Key => ConjugationProgress.ItemKey(Verb.Id, Form, Reverse);
}

/// <summary>
/// What a practice is narrowed to for now, from a Drill button in the progress tables; not kept in the settings.
/// </summary>
public enum DrillFocusKind
{
    Form,
    Rule,
    Verb
}

public sealed record DrillFocus(DrillFocusKind Kind, string Key, string Label);

/// <summary>
/// How a form, a rule or a verb is going, summed over its questions.
/// </summary>
public sealed class GroupProgress
{
    public required string Key { get; init; }
    public required string Label { get; init; }
    public string Detail { get; init; } = string.Empty;
    public ConjugationForm? Form { get; init; }
    public ConjugationVerbView? Verb { get; init; }
    public List<ConjugationItemView> Items { get; } = [];

    public int Attempts => Items.Sum(i => i.Attempts);
    public int Correct => Items.Sum(i => i.Correct);
    public int RecentAnswers => Items.Sum(i => i.Recent.Length);
    public double RecentRate => RecentAnswers == 0 ? 0 : Items.Sum(i => i.Recent.Count(c => c == '1')) / (double)RecentAnswers;
    public ConjugationStatus Status => ConjugationProgress.GroupStatus(Items.Select(i => i.Recent));
    public IEnumerable<ConjugationItemView> Troubled => Items.Where(i => ConjugationProgress.Status(i.Recent) == ConjugationStatus.Trouble);
    public DateTime LastSeen => Items.Count == 0 ? DateTime.MinValue : Items.Max(i => i.LastSeen);
}

/// <summary>
/// The practice itself: which questions can be asked with the settings, picking the next one so that what goes wrong
/// comes back more often, checking answers, and reading progress per form, rule and verb.
/// </summary>
public class ConjugationDrill
{
    /// <summary>
    /// A missed question comes back after this many others.
    /// </summary>
    public const int RetryAfter = 3;

    private readonly Random _random;
    private readonly Dictionary<string, ConjugationItemView> _items = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ConjugationVerbView> _verbs = new(StringComparer.Ordinal);
    private readonly List<(string Key, int Due)> _retry = [];
    private readonly Queue<string> _recentVerbs = new();
    private List<(ConjugationVerbView Verb, ConjugationForm Form, ConjugationRule Rule)>? _candidates;
    private Dictionary<string, ConjugationStatus>? _ruleStatus;
    private int _asked;

    public ConjugationDrill(IEnumerable<ConjugationVerbView> verbs, IEnumerable<ConjugationItemView> items, ConjugationSettingsView settings, Random? random = null)
    {
        _random = random ?? Random.Shared;
        foreach (ConjugationVerbView verb in verbs)
        {
            _verbs[verb.Id] = verb;
        }
        foreach (ConjugationItemView item in items)
        {
            _items[item.Key] = item;
        }
        Settings = settings;
    }

    public ConjugationSettingsView Settings { get; private set; }

    public DrillFocus? Focus { get; private set; }

    public IReadOnlyCollection<ConjugationItemView> Items => _items.Values;

    public IReadOnlyCollection<ConjugationVerbView> AllVerbs => _verbs.Values;

    /// <summary>
    /// Only items and rules in trouble were asked for, but nothing is in trouble, so everything is asked.
    /// </summary>
    public bool NothingInTrouble { get; private set; }

    public void Apply(ConjugationSettingsView settings)
    {
        Settings = settings;
        _candidates = null;
        _retry.Clear();
    }

    public void SetFocus(DrillFocus? focus)
    {
        Focus = focus;
        _candidates = null;
        _retry.Clear();
    }

    public ConjugationItemView? Item(string key) => _items.GetValueOrDefault(key);

    public ConjugationVerbView? FindVerb(string id) => _verbs.GetValueOrDefault(id);

    /// <summary>
    /// The verbs the settings draw from, ignoring the verb types.
    /// </summary>
    public IEnumerable<ConjugationVerbView> PoolVerbs() => _verbs.Values.Where(v => Settings.Pool switch
    {
        ConjugationPool.Calls => v.LearnerUses + v.PartnerUses > 0,
        ConjugationPool.Both => v.Rank <= Settings.CommonLimit || v.LearnerUses + v.PartnerUses > 0,
        _ => v.Rank <= Settings.CommonLimit
    });

    /// <summary>
    /// The verbs questions are drawn from: the pool, of the chosen types.
    /// </summary>
    public IEnumerable<ConjugationVerbView> Verbs() => PoolVerbs().Where(v => Conjugator.Group(v.Class) switch
    {
        VerbGroup.Godan => Settings.Godan,
        VerbGroup.Ichidan => Settings.Ichidan,
        _ => Settings.Irregular
    });

    private List<(ConjugationVerbView Verb, ConjugationForm Form, ConjugationRule Rule)> Candidates()
    {
        if (_candidates != null)
        {
            return _candidates;
        }
        List<(ConjugationVerbView, ConjugationForm, ConjugationRule)> candidates = [];
        IEnumerable<ConjugationVerbView> verbs = Focus?.Kind == DrillFocusKind.Verb
            ? _verbs.Values.Where(v => v.Id == Focus.Key)
            : Focus?.Kind == DrillFocusKind.Rule ? PoolVerbs() : Verbs();
        IEnumerable<ConjugationForm> forms = Focus?.Kind switch
        {
            DrillFocusKind.Form => [Enum.Parse<ConjugationForm>(Focus.Key)],
            DrillFocusKind.Rule => [Enum.Parse<ConjugationForm>(Focus.Key[..Focus.Key.IndexOf(':')])],
            _ => Settings.Forms.Distinct()
        };
        List<ConjugationForm> formList = [.. forms];
        foreach (ConjugationVerbView verb in verbs)
        {
            Verb dictionary = new(verb.Written, verb.Reading, verb.Class);
            foreach (ConjugationForm form in formList)
            {
                if (Conjugator.Conjugate(dictionary, form) == null)
                {
                    continue;
                }
                ConjugationRule rule = Conjugator.Rule(dictionary, form);
                if (Focus?.Kind == DrillFocusKind.Rule && rule.Key != Focus.Key)
                {
                    continue;
                }
                candidates.Add((verb, form, rule));
            }
        }
        _candidates = candidates;
        return candidates;
    }

    /// <summary>
    /// How many questions the settings allow, counting each verb and form once.
    /// </summary>
    public int QuestionCount => Candidates().Count;

    private Dictionary<string, ConjugationStatus> RuleStatus()
    {
        if (_ruleStatus != null)
        {
            return _ruleStatus;
        }
        _ruleStatus = Rules().ToDictionary(g => g.Key, g => g.Status, StringComparer.Ordinal);
        return _ruleStatus;
    }

    /// <summary>
    /// The next question, or null when the settings leave nothing to ask.
    /// </summary>
    public DrillQuestion? Next()
    {
        List<(ConjugationVerbView Verb, ConjugationForm Form, ConjugationRule Rule)> candidates = Candidates();
        NothingInTrouble = false;
        if (candidates.Count == 0)
        {
            return null;
        }
        _asked++;

        // A question missed a moment ago comes back first
        int due = _retry.FindIndex(r => r.Due <= _asked);
        if (due >= 0)
        {
            string key = _retry[due].Key;
            _retry.RemoveAt(due);
            if (Retry(key, candidates) is DrillQuestion again)
            {
                return Remember(again);
            }
        }

        bool reverse = Settings.Randomize ? _random.Next(2) == 1 : Settings.Reverse;
        Dictionary<string, ConjugationStatus> rules = RuleStatus();
        ConjugationMode mode = Focus != null && Settings.Mode == ConjugationMode.TroubleOnly ? ConjugationMode.Focus : Settings.Mode;

        double[] weights = new double[candidates.Count];
        double total = 0;
        for (int i = 0; i < candidates.Count; i++)
        {
            (ConjugationVerbView verb, ConjugationForm form, ConjugationRule rule) = candidates[i];
            ConjugationStatus item = ConjugationProgress.Status(_items.GetValueOrDefault(ConjugationProgress.ItemKey(verb.Id, form, reverse))?.Recent ?? string.Empty);
            bool ruleTrouble = rules.GetValueOrDefault(rule.Key) == ConjugationStatus.Trouble;
            double weight = Weight(mode, item, ruleTrouble);
            if (_recentVerbs.Contains(verb.Id) && candidates.Count > 1)
            {
                weight *= 0.02;
            }
            weights[i] = weight;
            total += weight;
        }
        if (total <= 0)
        {
            // Nothing is in trouble: ask everything rather than nothing
            NothingInTrouble = mode == ConjugationMode.TroubleOnly;
            for (int i = 0; i < weights.Length; i++)
            {
                weights[i] = 1;
            }
            total = weights.Length;
        }

        double roll = _random.NextDouble() * total;
        int picked = weights.Length - 1;
        for (int i = 0; i < weights.Length; i++)
        {
            roll -= weights[i];
            if (roll < 0)
            {
                picked = i;
                break;
            }
        }
        (ConjugationVerbView chosen, ConjugationForm chosenForm, ConjugationRule chosenRule) = candidates[picked];
        return Remember(Make(chosen, chosenForm, reverse, chosenRule));
    }

    /// <summary>
    /// How likely a question is to be picked. Mixed asks everything and brings back what goes wrong; Focus mostly
    /// asks what goes wrong, and new verbs for rules that go wrong; TroubleOnly asks nothing else.
    /// </summary>
    public static double Weight(ConjugationMode mode, ConjugationStatus item, bool ruleTrouble) => mode switch
    {
        ConjugationMode.Focus => item switch
        {
            ConjugationStatus.Trouble => 10,
            ConjugationStatus.Learning => 2,
            ConjugationStatus.New => 0.3,
            _ => 0.1
        } * (ruleTrouble ? 4 : 1),
        ConjugationMode.TroubleOnly => item == ConjugationStatus.Trouble ? 3 : ruleTrouble ? 1 : 0,
        _ => item switch
        {
            ConjugationStatus.Trouble => 4,
            ConjugationStatus.Learning => 1.5,
            ConjugationStatus.New => 1,
            _ => 0.35
        } * (ruleTrouble ? 2 : 1)
    };

    private DrillQuestion? Retry(string key, List<(ConjugationVerbView Verb, ConjugationForm Form, ConjugationRule Rule)> candidates)
    {
        string[] parts = key.Split('|');
        if (parts.Length != 3 || !int.TryParse(parts[1], out int form))
        {
            return null;
        }
        foreach ((ConjugationVerbView verb, ConjugationForm candidateForm, ConjugationRule rule) in candidates)
        {
            if (verb.Id == parts[0] && (int)candidateForm == form)
            {
                return Make(verb, candidateForm, parts[2] == "1", rule);
            }
        }
        return null;
    }

    private static DrillQuestion Make(ConjugationVerbView verb, ConjugationForm form, bool reverse, ConjugationRule rule) =>
        new(verb, form, reverse, Conjugator.Conjugate(new Verb(verb.Written, verb.Reading, verb.Class), form)!, rule);

    private DrillQuestion Remember(DrillQuestion question)
    {
        _recentVerbs.Enqueue(question.Verb.Id);
        while (_recentVerbs.Count > 3)
        {
            _recentVerbs.Dequeue();
        }
        return question;
    }

    public static bool Check(DrillQuestion question, string answer) => question.Reverse
        ? Conjugator.IsDictionaryFormOf(question.Dictionary, question.Form, answer)
        : Conjugator.IsCorrect(question.Answer, answer);

    /// <summary>
    /// Counts an answer here at once, so the next pick already knows; the stored item replaces it when it comes back.
    /// </summary>
    public ConjugationItemView Record(DrillQuestion question, string answer, bool correct)
    {
        ConjugationItemView item = _items.GetValueOrDefault(question.Key) ?? new ConjugationItemView
        {
            Verb = question.Verb.Id,
            Written = question.Verb.Written,
            Reading = question.Verb.Reading,
            Class = question.Verb.Class,
            Form = question.Form,
            Reverse = question.Reverse
        };
        item.Attempts++;
        item.Recent = ConjugationProgress.Push(item.Recent, correct);
        item.LastSeen = DateTime.UtcNow;
        if (correct)
        {
            item.Correct++;
        }
        else
        {
            item.LastWrong = DateTime.UtcNow;
            item.WrongAnswers = [.. item.WrongAnswers.Append(answer.Trim().Length == 0 ? "(blank)" : answer.Trim()).TakeLast(ConjugationProgress.WrongAnswerLength)];
            _retry.Add((question.Key, _asked + RetryAfter));
        }
        _items[question.Key] = item;
        _ruleStatus = null;
        return item;
    }

    public void Stored(ConjugationItemView item)
    {
        _items[item.Key] = item;
        _ruleStatus = null;
    }

    public void Forget()
    {
        _items.Clear();
        _retry.Clear();
        _ruleStatus = null;
    }

    private Verb DictionaryOf(ConjugationItemView item) =>
        _verbs.GetValueOrDefault(item.Verb) is ConjugationVerbView verb
            ? new Verb(verb.Written, verb.Reading, verb.Class)
            : new Verb(item.Written, item.Reading, item.Class);

    public List<GroupProgress> Forms()
    {
        Dictionary<ConjugationForm, GroupProgress> groups = [];
        foreach (ConjugationItemView item in _items.Values)
        {
            if (!groups.TryGetValue(item.Form, out GroupProgress? group))
            {
                group = new GroupProgress { Key = item.Form.ToString(), Label = Conjugator.FormName(item.Form), Form = item.Form };
                groups[item.Form] = group;
            }
            group.Items.Add(item);
        }
        return [.. groups.Values.OrderBy(g => Array.IndexOf(Conjugator.DisplayOrder, g.Form!.Value))];
    }

    public List<GroupProgress> Rules()
    {
        Dictionary<string, GroupProgress> groups = new(StringComparer.Ordinal);
        foreach (ConjugationItemView item in _items.Values)
        {
            ConjugationRule rule = Conjugator.Rule(DictionaryOf(item), item.Form);
            if (!groups.TryGetValue(rule.Key, out GroupProgress? group))
            {
                group = new GroupProgress { Key = rule.Key, Label = Conjugator.FormName(item.Form), Detail = rule.Label, Form = item.Form };
                groups[rule.Key] = group;
            }
            group.Items.Add(item);
        }
        return [.. groups.Values.OrderBy(g => Array.IndexOf(Conjugator.DisplayOrder, g.Form!.Value)).ThenBy(g => g.Detail, StringComparer.Ordinal)];
    }

    public List<GroupProgress> VerbGroups()
    {
        Dictionary<string, GroupProgress> groups = new(StringComparer.Ordinal);
        foreach (ConjugationItemView item in _items.Values)
        {
            if (!groups.TryGetValue(item.Verb, out GroupProgress? group))
            {
                ConjugationVerbView verb = _verbs.GetValueOrDefault(item.Verb) ?? new ConjugationVerbView
                {
                    Id = item.Verb, Written = item.Written, Reading = item.Reading, Class = item.Class
                };
                group = new GroupProgress { Key = item.Verb, Label = verb.Written, Detail = verb.Reading, Verb = verb };
                groups[item.Verb] = group;
            }
            group.Items.Add(item);
        }
        return [.. groups.Values.OrderByDescending(g => g.LastSeen)];
    }
}
