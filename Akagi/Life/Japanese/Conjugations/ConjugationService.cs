using Akagi.Bridge.Chat.Models;
using Akagi.Bridge.Japanese;
using Akagi.Life.Japanese.Vocabulary;

namespace Akagi.Life.Japanese.Conjugations;

/// <summary>
/// Everything the practice page starts with.
/// </summary>
internal sealed record ConjugationState(
    ConjugationSettingsView Settings,
    List<ConjugationVerbView> Verbs,
    List<ConjugationItemView> Items,
    List<ConjugationDayView> Days,
    List<ConjugationAttemptView> Mistakes,
    bool DictionaryLoading);

/// <summary>
/// The conjugation practice: which verbs can be asked, the settings, and the progress on every question.
/// </summary>
internal class ConjugationService
{
    /// <summary>
    /// The verbs sent along always reach this far down the frequency list; the page narrows them by its setting.
    /// </summary>
    public const int MaxCommonRank = WordInfo.Common;

    public const int DayCount = 30;
    public const int MistakeCount = 30;

    private readonly IVerbCatalog _catalog;
    private readonly IConjugationItemDatabase _items;
    private readonly IConjugationAttemptDatabase _attempts;
    private readonly IConjugationPreferencesDatabase _preferences;
    private readonly IVocabularyIndex? _vocabulary;

    public ConjugationService(IVerbCatalog catalog, IConjugationItemDatabase items, IConjugationAttemptDatabase attempts,
        IConjugationPreferencesDatabase preferences, IVocabularyIndex? vocabulary)
    {
        _catalog = catalog;
        _items = items;
        _attempts = attempts;
        _preferences = preferences;
        _vocabulary = vocabulary;
    }

    public async Task<ConjugationState> LoadAsync(string userId, int utcOffsetMinutes)
    {
        ConjugationSettingsView settings = View(await _preferences.GetForUser(userId));
        List<ConjugationItem> items = await _items.GetForUser(userId);

        Dictionary<string, ConjugationVerbView> verbs = new(StringComparer.Ordinal);
        foreach (DictionaryVerb verb in _catalog.Common.TakeWhile(v => v.Rank <= MaxCommonRank))
        {
            verbs.TryAdd(verb.Id, View(verb));
        }
        await AddCallVerbsAsync(userId, verbs);
        // Verbs practised before stay askable even when they left the list
        foreach (ConjugationItem item in items.Where(i => !verbs.ContainsKey(i.Verb)))
        {
            DictionaryVerb? known = _catalog.Find(item.Written);
            verbs[item.Verb] = new ConjugationVerbView
            {
                Id = item.Verb,
                Written = item.Written,
                Reading = item.Reading,
                Class = item.Class,
                Meaning = known?.Id == item.Verb ? known.Meaning : string.Empty,
                Rank = known?.Id == item.Verb ? known.Rank : int.MaxValue
            };
        }

        TimeSpan offset = TimeSpan.FromMinutes(utcOffsetMinutes);
        DateTime since = DateTime.UtcNow.Add(offset).Date.AddDays(-(DayCount - 1)).Subtract(offset);
        List<ConjugationAttempt> recent = await _attempts.GetSinceAsync(userId, since);
        List<ConjugationDayView> days = [.. recent
            .GroupBy(a => a.At.Add(offset).Date)
            .OrderBy(g => g.Key)
            .Select(g => new ConjugationDayView { Date = DateTime.SpecifyKind(g.Key, DateTimeKind.Unspecified), Attempts = g.Count(), Correct = g.Count(a => a.Correct) })];
        List<ConjugationAttempt> mistakes = await _attempts.GetMistakesAsync(userId, MistakeCount);

        return new ConjugationState(settings, [.. verbs.Values], [.. items.Select(View)], days, [.. mistakes.Select(View)], !_catalog.Ready);
    }

    /// <summary>
    /// Verbs the user or their partners said in calls, counted per side.
    /// </summary>
    private async Task AddCallVerbsAsync(string userId, Dictionary<string, ConjugationVerbView> verbs)
    {
        if (_vocabulary == null || !_catalog.Ready)
        {
            return;
        }
        (List<CallTokens> calls, _) = await _vocabulary.LoadAsync(userId);
        Dictionary<string, (int Learner, int Partner)> counts = new(StringComparer.Ordinal);
        foreach (VocabToken token in calls.SelectMany(c => c.Tokens))
        {
            if (token.Token.Class != WordClass.Verb || token.Token.Lemma.Length == 0)
            {
                continue;
            }
            (int learner, int partner) = counts.GetValueOrDefault(token.Token.Lemma);
            counts[token.Token.Lemma] = token.Learner ? (learner + 1, partner) : (learner, partner + 1);
        }
        foreach ((string lemma, (int learner, int partner)) in counts)
        {
            if (_catalog.Find(lemma) is not DictionaryVerb verb)
            {
                continue;
            }
            if (!verbs.TryGetValue(verb.Id, out ConjugationVerbView? view))
            {
                view = View(verb);
                verbs[verb.Id] = view;
            }
            view.LearnerUses += learner;
            view.PartnerUses += partner;
        }
    }

    public async Task<ConjugationItemView> RecordAsync(string userId, string verb, string written, string reading, VerbClass verbClass,
        ConjugationForm form, bool reverse, string answer, bool correct, int millis)
    {
        DateTime now = DateTime.UtcNow;
        string given = answer.Trim();
        if (given.Length > 60)
        {
            given = given[..60];
        }
        ConjugationItem item = await _items.FindAsync(userId, verb, form, reverse) ?? new ConjugationItem
        {
            UserId = userId,
            Verb = verb,
            Form = form,
            Reverse = reverse
        };
        item.Written = written;
        item.Reading = reading;
        item.Class = verbClass;
        item.Attempts++;
        item.Recent = ConjugationProgress.Push(item.Recent, correct);
        item.LastSeen = now;
        if (correct)
        {
            item.Correct++;
        }
        else
        {
            item.LastWrong = now;
            item.WrongAnswers.Add(given.Length == 0 ? "(blank)" : given);
            if (item.WrongAnswers.Count > ConjugationProgress.WrongAnswerLength)
            {
                item.WrongAnswers.RemoveRange(0, item.WrongAnswers.Count - ConjugationProgress.WrongAnswerLength);
            }
        }
        await _items.SaveDocumentAsync(item);
        await _attempts.SaveDocumentAsync(new ConjugationAttempt
        {
            UserId = userId,
            Verb = verb,
            Written = written,
            Reading = reading,
            Class = verbClass,
            Form = form,
            Reverse = reverse,
            Answer = given,
            Correct = correct,
            Millis = Math.Max(0, millis),
            At = now
        });
        return View(item);
    }

    public async Task<ConjugationSettingsView> SaveSettingsAsync(string userId, ConjugationSettingsView settings)
    {
        ConjugationPreferences preferences = await _preferences.GetForUser(userId) ?? new ConjugationPreferences { UserId = userId };
        preferences.Forms = [.. settings.Forms.Distinct().Where(Enum.IsDefined)];
        preferences.Godan = settings.Godan;
        preferences.Ichidan = settings.Ichidan;
        preferences.Irregular = settings.Irregular;
        preferences.Pool = (int)settings.Pool;
        preferences.CommonLimit = Math.Clamp(settings.CommonLimit, 500, MaxCommonRank);
        preferences.Mode = (int)settings.Mode;
        preferences.Reverse = settings.Reverse;
        preferences.Randomize = settings.Randomize;
        preferences.ShowKanji = settings.ShowKanji;
        preferences.ShowFurigana = settings.ShowFurigana;
        preferences.HideType = settings.HideType;
        preferences.ShowEnglish = settings.ShowEnglish;
        await _preferences.SaveDocumentAsync(preferences);
        return View(preferences);
    }

    public async Task ResetAsync(string userId)
    {
        await _items.DeleteForUserAsync(userId);
        await _attempts.DeleteForUserAsync(userId);
    }

    internal static ConjugationSettingsView View(ConjugationPreferences? preferences) => preferences == null ? new ConjugationSettingsView() : new ConjugationSettingsView
    {
        Forms = [.. preferences.Forms],
        Godan = preferences.Godan,
        Ichidan = preferences.Ichidan,
        Irregular = preferences.Irregular,
        Pool = Enum.IsDefined((ConjugationPool)preferences.Pool) ? (ConjugationPool)preferences.Pool : ConjugationPool.Common,
        CommonLimit = preferences.CommonLimit,
        Mode = Enum.IsDefined((ConjugationMode)preferences.Mode) ? (ConjugationMode)preferences.Mode : ConjugationMode.Mixed,
        Reverse = preferences.Reverse,
        Randomize = preferences.Randomize,
        ShowKanji = preferences.ShowKanji,
        ShowFurigana = preferences.ShowFurigana,
        HideType = preferences.HideType,
        ShowEnglish = preferences.ShowEnglish
    };

    internal static ConjugationVerbView View(DictionaryVerb verb) => new()
    {
        Id = verb.Id,
        Written = verb.Written,
        Reading = verb.Reading,
        Class = verb.Class,
        Meaning = verb.Meaning,
        Rank = verb.Rank
    };

    internal static ConjugationItemView View(ConjugationItem item) => new()
    {
        Verb = item.Verb,
        Written = item.Written,
        Reading = item.Reading,
        Class = item.Class,
        Form = item.Form,
        Reverse = item.Reverse,
        Attempts = item.Attempts,
        Correct = item.Correct,
        Recent = item.Recent,
        LastSeen = item.LastSeen,
        LastWrong = item.LastWrong,
        WrongAnswers = [.. item.WrongAnswers]
    };

    internal static ConjugationAttemptView View(ConjugationAttempt attempt) => new()
    {
        Verb = attempt.Verb,
        Written = attempt.Written,
        Reading = attempt.Reading,
        Class = attempt.Class,
        Form = attempt.Form,
        Reverse = attempt.Reverse,
        Answer = attempt.Answer,
        Correct = attempt.Correct,
        At = attempt.At,
        Millis = attempt.Millis
    };
}
