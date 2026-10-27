using System.Globalization;
using System.Text.RegularExpressions;

namespace Akagi.Life.Japanese;

/// <summary>
/// The politeness the situation calls for.
/// </summary>
internal enum ExpectedRegister
{
    /// <summary>Friends, partner, people the user is close to: plain form.</summary>
    Casual,
    /// <summary>Acquaintances, colleagues, shop staff: です/ます.</summary>
    Polite,
    /// <summary>Customers, interviews, superiors: keigo.</summary>
    Formal
}

internal class RegisterReport
{
    public required int Sentences { get; init; }
    public required int Polite { get; init; }
    public required int Keigo { get; init; }
    /// <summary>How often consecutive sentences switch between polite and plain.</summary>
    public required int Switches { get; init; }
    public required IReadOnlyList<string> PoliteExamples { get; init; }
    public required IReadOnlyList<string> PlainExamples { get; init; }

    public int Plain => Sentences - Polite;
    public double PoliteRatio => Sentences == 0 ? 0 : (double)Polite / Sentences;

    /// <summary>
    /// A mismatch between the register used and the one expected, or null when it fits.
    /// Needs a handful of sentences so a single set phrase does not count.
    /// </summary>
    public string? Mismatch(ExpectedRegister expected)
    {
        if (Sentences < 5)
        {
            return null;
        }

        string percent = PoliteRatio.ToString("P0", CultureInfo.InvariantCulture);
        return expected switch
        {
            ExpectedRegister.Casual when PoliteRatio > 0.4 =>
                $"Too stiff for friends: {percent} of sentences end in です/ます (e.g. '{PoliteExamples.FirstOrDefault()}').",
            ExpectedRegister.Polite when PoliteRatio < 0.6 =>
                $"Too casual for the situation: only {percent} of sentences are in です/ます (e.g. '{PlainExamples.FirstOrDefault()}').",
            ExpectedRegister.Formal when Keigo == 0 =>
                "No honorific or humble forms (いらっしゃる, 申す, いたす, ...) in a situation that calls for keigo.",
            _ when Switches > Sentences / 3 && PoliteRatio is > 0.25 and < 0.75 =>
                $"Register flips between polite and plain {Switches} times in {Sentences} sentences.",
            _ => null
        };
    }
}

/// <summary>
/// Classifies sentence endings as polite (です/ます) or plain and spots keigo vocabulary.
/// </summary>
internal static partial class RegisterAnalyzer
{
    public static RegisterReport Analyze(string text)
    {
        List<string> sentences = JapaneseText.SplitSentences(text);
        int polite = 0;
        int switches = 0;
        bool? previous = null;
        List<string> politeExamples = [];
        List<string> plainExamples = [];

        foreach (string sentence in sentences)
        {
            bool isPolite = IsPolite(sentence);
            if (isPolite)
            {
                polite++;
                if (politeExamples.Count < 3)
                {
                    politeExamples.Add(sentence);
                }
            }
            else if (plainExamples.Count < 3)
            {
                plainExamples.Add(sentence);
            }
            if (previous.HasValue && previous.Value != isPolite)
            {
                switches++;
            }
            previous = isPolite;
        }

        return new RegisterReport
        {
            Sentences = sentences.Count,
            Polite = polite,
            Keigo = Keigo().Matches(text).Count,
            Switches = switches,
            PoliteExamples = politeExamples,
            PlainExamples = plainExamples
        };
    }

    public static bool IsPolite(string sentence) => PoliteEnding().IsMatch(JapaneseText.TrimEnding(sentence));

    [GeneratedRegex(@"(です|ます|ました|ません|でした|ましょう|ませんでした|でしょう|ください|ございます|ましたっけ|ですっけ)(か|ね|よ|よね|けど|けれど|が|から|ので|し)*$")]
    private static partial Regex PoliteEnding();

    [GeneratedRegex(@"いらっしゃ|おっしゃ|召し上が|ご覧に|存じ|申し|申す|参り|参る|伺い|伺う|伺っ|いたし|いたす|致し|拝見|おりま|させていただ|お待ちくださ")]
    private static partial Regex Keigo();
}
