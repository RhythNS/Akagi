using Akagi.Characters.Conversations;
using System.Text;

namespace Akagi.Receivers.Commands;

/// <summary>
/// What started the turn a tool runs in. Tools that change stored state are only offered when the user said
/// something (or a reflection reads what the user said); proactive and consultation turns only read.
/// </summary>
internal enum TurnSource
{
    /// <summary>Not set by the caller (pipelines, tests); no restrictions apply.</summary>
    Unknown,
    /// <summary>The user sent a message.</summary>
    User,
    /// <summary>A routine, reminder, call report or other system event; the character reaches out on its own.</summary>
    System,
    /// <summary>An advisor answering a group persona's Consult call.</summary>
    Consultation,
    /// <summary>A reflector reading the conversation after a turn.</summary>
    Reflection
}

/// <summary>
/// Marker for tools that change stored state (the user's life data, routines, the companion's own state).
/// They are not offered on <see cref="TurnSource.System"/> and <see cref="TurnSource.Consultation"/> turns.
/// </summary>
internal interface IStateCommand
{
}

/// <summary>
/// Marker for tools that record something about the user. They take an <see cref="Grounding.EvidenceName"/>
/// argument that must quote the user's own words from a recent message, which is checked in code.
/// </summary>
internal interface IUserFactCommand : IStateCommand
{
}

/// <summary>
/// Marker for tools that change work items the user manages explicitly, such as Kanban tickets. Unlike other state
/// tools, a consulted module may use them on the persona's behalf when the user started the turn, and they have a
/// larger budget per reply, since planning touches many tickets at once.
/// </summary>
internal interface IWorkItemCommand : IStateCommand
{
    /// <summary>How many items this call changes, counted against the budget; a bulk create counts each ticket.</summary>
    int Changes => 1;

    /// <summary>
    /// What this call changes that should change at most once per turn, e.g. the ticket an update goes to; null when
    /// it may run several times. A repeating model rewords each call, so identical-call checks miss it.
    /// </summary>
    string? OncePerTurn => null;
}

/// <summary>
/// Checks that what a tool records is grounded in what the user actually wrote, and compares short texts for
/// near-duplicates.
/// </summary>
internal static class Grounding
{
    public const string EvidenceName = "Evidence";

    /// <summary>How many of the user's latest messages a quote may come from.</summary>
    public const int EvidenceMessages = 12;

    /// <summary>How old a quoted message may be.</summary>
    public static readonly TimeSpan EvidenceAge = TimeSpan.FromDays(3);

    private static readonly string[] FragmentSeparators = ["...", "…", "[...]"];

    public static Argument EvidenceArgument() => new()
    {
        Name = EvidenceName,
        Description = "The user's own words this is based on, copied exactly from one of his messages (not your words, " +
                      "not a summary). Without a real quote nothing is recorded.",
        IsRequired = true,
        ArgumentType = Argument.Type.String
    };

    /// <summary>
    /// The texts of the user's latest messages in the conversation, newest first.
    /// </summary>
    public static IReadOnlyList<string> RecentUserTexts(IEnumerable<Message> messages, DateTime nowUtc, DateTime? afterUtc = null)
    {
        List<string> texts = [];
        foreach (Message message in messages.Reverse())
        {
            if (message.From != Message.Type.User || nowUtc - message.Time > EvidenceAge || message.Time <= afterUtc)
            {
                continue;
            }
            string? text = message switch
            {
                TextMessage textMessage => textMessage.Text,
                VoiceMessage voiceMessage => voiceMessage.Content,
                _ => null
            };
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }
            texts.Add(text);
            if (texts.Count >= EvidenceMessages)
            {
                break;
            }
        }
        return texts;
    }

    /// <summary>
    /// True when the quote appears in one of the user's texts. Case, spacing, punctuation and full/half width are
    /// ignored; "..." may join fragments of the same message.
    /// </summary>
    public static bool IsQuoted(string? evidence, IEnumerable<string> userTexts)
    {
        if (string.IsNullOrWhiteSpace(evidence))
        {
            return false;
        }

        string[] fragments = [.. evidence.Split(FragmentSeparators, StringSplitOptions.RemoveEmptyEntries)
            .Select(Normalize)
            .Where(f => f.Length > 0)];
        if (fragments.Length == 0 || fragments.Sum(f => f.Length) < MinimumLength(fragments))
        {
            return false;
        }

        foreach (string text in userTexts)
        {
            string normalized = Normalize(text);
            if (fragments.All(normalized.Contains))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Lower case letters and digits only, after Unicode compatibility folding (full width to half width).
    /// </summary>
    public static string Normalize(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }
        string folded = text.Normalize(NormalizationForm.FormKC).ToLowerInvariant();
        StringBuilder builder = new(folded.Length);
        foreach (char c in folded)
        {
            if (char.IsLetterOrDigit(c))
            {
                builder.Append(c);
            }
        }
        return builder.ToString();
    }

    /// <summary>
    /// Dice coefficient over character bigrams of the normalized texts: 1 for the same text, 0 for nothing shared.
    /// </summary>
    public static double Similarity(string? a, string? b)
    {
        string left = Normalize(a);
        string right = Normalize(b);
        if (left.Length == 0 || right.Length == 0)
        {
            return 0;
        }
        if (left == right)
        {
            return 1;
        }
        if (left.Length < 2 || right.Length < 2)
        {
            return 0;
        }

        Dictionary<string, int> bigrams = [];
        for (int i = 0; i < left.Length - 1; i++)
        {
            string bigram = left.Substring(i, 2);
            bigrams[bigram] = bigrams.GetValueOrDefault(bigram) + 1;
        }

        int shared = 0;
        for (int i = 0; i < right.Length - 1; i++)
        {
            string bigram = right.Substring(i, 2);
            if (bigrams.TryGetValue(bigram, out int count) && count > 0)
            {
                bigrams[bigram] = count - 1;
                shared++;
            }
        }
        return 2.0 * shared / (left.Length - 1 + right.Length - 1);
    }

    /// <summary>
    /// Dice coefficient over the content words of two short texts such as titles: word order, small words ("the",
    /// "for", "und") and a plural s do not count. Unlike <see cref="Similarity"/> it sees "2FA audit of accounts" and
    /// "Audit accounts for 2FA" as the same, and "Write tests" and "Write docs" as different.
    /// </summary>
    public static double WordSimilarity(string? a, string? b)
    {
        HashSet<string> left = ContentWords(a);
        HashSet<string> right = ContentWords(b);
        if (left.Count == 0 || right.Count == 0)
        {
            return 0;
        }
        int shared = left.Count(right.Contains);
        return 2.0 * shared / (left.Count + right.Count);
    }

    private static readonly HashSet<string> SmallWords =
    [
        "a", "an", "the", "and", "or", "of", "to", "for", "in", "on", "at", "by", "from", "with", "about", "into", "all",
        "any", "before", "after", "it", "its", "his", "her", "my", "your", "our", "their", "this", "that", "is", "are", "be",
        "der", "die", "das", "den", "dem", "des", "ein", "eine", "einen", "und", "oder", "für", "von", "mit", "zu", "zum",
        "zur", "im", "am", "auf", "aus", "bei", "nach", "vor"
    ];

    private static HashSet<string> ContentWords(string? text)
    {
        HashSet<string> words = [];
        if (string.IsNullOrWhiteSpace(text))
        {
            return words;
        }
        StringBuilder word = new();
        foreach (char c in text.Normalize(NormalizationForm.FormKC).ToLowerInvariant().Append(' '))
        {
            if (char.IsLetterOrDigit(c))
            {
                word.Append(c);
                continue;
            }
            if (word.Length > 0)
            {
                string w = word.ToString();
                word.Clear();
                if (SmallWords.Contains(w))
                {
                    continue;
                }
                // "accounts" and "account" are the same word; "glass" keeps its s
                if (w.Length > 3 && w.EndsWith('s') && !w.EndsWith("ss", StringComparison.Ordinal))
                {
                    w = w[..^1];
                }
                words.Add(w);
            }
        }
        return words;
    }

    /// <summary>
    /// Quotes in Japanese or Chinese can be two characters ("眠い"); Latin quotes need four ("so tired").
    /// </summary>
    private static int MinimumLength(IEnumerable<string> fragments)
    {
        return fragments.Any(f => f.Any(c => c >= '⺀')) ? 2 : 4;
    }
}
