using System.Text.RegularExpressions;

namespace Akagi.Life.Japanese;

/// <summary>
/// A JLPT N2 grammar pattern the user should be able to use in real conversation.
/// </summary>
internal record GrammarPattern(string Key, string Meaning, string Regex, string Group)
{
    public Regex Matcher { get; } = new(Regex, RegexOptions.Compiled | RegexOptions.CultureInvariant);
}

internal record PatternMatch(GrammarPattern Pattern, int Count, string Example);

/// <summary>
/// N2 patterns grouped by the nuance learners tend to mix up. Detection only proves a pattern was used, not that it
/// was used correctly; judging that is left to the tutor and the call analyzer, which get the groups as hints.
/// </summary>
internal static class GrammarPatterns
{
    public static readonly IReadOnlyList<GrammarPattern> N2 =
    [
        // Having no choice / cannot afford to
        new("～わけにはいかない", "cannot (for social or moral reasons)", "わけに(は|も)いか(ない|なかった|ず|ぬ|ないん)", "obligation"),
        new("～ざるを得ない", "have no choice but to (reluctantly)", "ざるを(得|え)(ない|なかった|ません|ず)", "obligation"),
        new("～しかない", "nothing to do but", "しか(ない|なかった|ありません)", "obligation"),
        new("～ずにはいられない", "cannot help doing", "ずには(いられ|おれ)(ない|ません|なかった)", "obligation"),
        // Impossibility
        new("～っこない", "no way that (casual)", "っこない", "impossibility"),
        new("～わけがない", "there is no way that", "わけ(が|は)(ない|ありません|なかった)", "impossibility"),
        new("～はずがない", "it cannot be that (logically)", "はず(が|は)(ない|ありません)", "impossibility"),
        // Risk of something bad
        new("～かねない", "might (something bad)", "かね(ない|ません)", "risk"),
        new("～おそれがある", "there is a risk that (formal)", "(恐れ|おそれ)が(ある|あります)", "risk"),
        new("～かねる", "cannot / find it hard to (polite refusal)", "かね(る|ます)(?!ない)", "refusal"),
        // Certainty
        new("～に相違ない", "without doubt (written)", "に相違(ない|ありません)", "certainty"),
        new("～に違いない", "must be, surely", "に違い(ない|ありません)", "certainty"),
        // Partial negation
        new("～わけではない", "it does not mean that", "わけ(では|じゃ)(ない|ありません|なかった)", "partial-negation"),
        new("～ないことはない", "it is not that ... not", "ないことは(ない|ありません)", "partial-negation"),
        new("～とは限らない", "not necessarily", "とは限(らない|りません)", "partial-negation"),
        // Concession
        new("～ものの", "although (but the result falls short)", "(た|る|い)ものの", "concession"),
        new("～にもかかわらず", "despite", "にもかかわらず|にも関わらず", "concession"),
        new("～くせに", "even though (critical)", "くせに", "concession"),
        // Scope
        new("～にかかわらず", "regardless of", "(に|を)(かかわらず|関わらず|問わず)", "scope"),
        new("～に限らず", "not only, not limited to", "に限らず", "scope"),
        new("～ばかりか", "not only ... but also", "ばかりか", "scope"),
        new("～どころか", "far from, let alone", "どころか", "scope"),
        // Change and tendency
        new("～つつある", "is in the process of", "つつ(ある|あります)", "tendency"),
        new("～一方だ", "keeps getting more", "一方(だ|です|で)", "tendency"),
        new("～がちだ", "tends to (bad habit)", "(?<![ガ])がち(だ|な|で|です)", "tendency"),
        new("～っぽい", "-ish, seems like", "っぽい", "tendency"),
        // Judging against expectation
        new("～にしては", "for a (given that)", "にしては", "expectation"),
        new("～わりに", "considering, for", "(わりに|割に)", "expectation"),
        // Condition and sequence
        new("～次第", "as soon as / depends on", "次第(で|だ|です)?", "sequence"),
        new("～上で", "after doing / in doing", "た上で|の上で", "sequence"),
        new("～からには", "now that, since", "からには|以上(は)?、", "sequence"),
        new("～をきっかけに", "triggered by", "をきっかけ(に|として)", "sequence"),
        new("～に伴って", "along with", "に伴(って|い|う)", "sequence"),
        // Necessity
        new("～に越したことはない", "it is best to", "に越したことは(ない|ありません)", "advice"),
        new("～ことはない", "there is no need to", "(る|う)ことは(ない|ありません)", "advice"),
        new("～までもない", "it goes without saying", "までも(ない|ありません)", "advice"),
        new("～ようがない", "there is no way to", "よう(が|も)(ない|ありません)", "advice")
    ];

    /// <summary>
    /// The nuance groups as one line each, given to analyzers as hints for what to watch.
    /// </summary>
    public static IEnumerable<string> DescribeGroups() =>
        N2.GroupBy(p => p.Group).Select(g => $"{g.Key}: {string.Join(", ", g.Select(p => $"{p.Key} ({p.Meaning})"))}");

    public static List<PatternMatch> Find(string text)
    {
        List<PatternMatch> matches = [];
        foreach (GrammarPattern pattern in N2)
        {
            MatchCollection found = pattern.Matcher.Matches(text);
            if (found.Count > 0)
            {
                matches.Add(new PatternMatch(pattern, found.Count, JapaneseText.Snippet(text, found[0].Index, found[0].Length)));
            }
        }
        return matches;
    }

    public static GrammarPattern? Lookup(string key)
    {
        string normalized = DiagnosisIssue.NormalizePattern(key);
        return N2.FirstOrDefault(p => DiagnosisIssue.NormalizePattern(p.Key) == normalized);
    }
}
