using System.Text;
using System.Text.RegularExpressions;

namespace Akagi.Characters.Conversations;

/// <summary>
/// The time stamps a message compiler puts in front of messages ("[Sun 14:43] ..."). Models copy them into their
/// replies when they see them on their own earlier messages, and every copy is stamped again the next time; so they
/// are taken off replies and off stored messages before stamping.
/// </summary>
internal static partial class MessageStamps
{
    /// <summary>
    /// One or more bracketed stamps holding a time (H:mm) at the very start, with the space around them.
    /// </summary>
    [GeneratedRegex(@"^\s*(?:\[(?=[^\]\n]{0,40}\d{1,2}:\d{2})[^\]\n]{1,40}\]\s*)+")]
    private static partial Regex Leading();

    public static string StripLeading(string text)
    {
        Match match = Leading().Match(text);
        return match.Success ? text[match.Length..] : text;
    }

    /// <summary>
    /// The text so far could still turn into a stamp: only space, or an opening bracket not yet closed.
    /// </summary>
    internal static bool CouldBecomeStamp(string text)
    {
        string start = text.TrimStart();
        return start.Length == 0 || (start[0] == '[' && start.Length <= 42 && !start.Contains(']') && !start.Contains('\n'));
    }
}

/// <summary>
/// Holds back the first characters of a streamed reply until it is clear whether they are a copied time stamp, so
/// the stamp is never shown; everything after that passes straight through.
/// </summary>
internal sealed class LeadingStampFilter
{
    private readonly StringBuilder _held = new();
    private bool _released;

    /// <summary>
    /// The part of the chunk to show now; empty while the start is still undecided.
    /// </summary>
    public string Push(string chunk)
    {
        if (_released)
        {
            return chunk;
        }
        _held.Append(chunk);
        string stripped = MessageStamps.StripLeading(_held.ToString());
        if (MessageStamps.CouldBecomeStamp(stripped))
        {
            return string.Empty;
        }
        return Release(stripped);
    }

    /// <summary>
    /// What is still held when the stream ends.
    /// </summary>
    public string Flush() => _released ? string.Empty : Release(MessageStamps.StripLeading(_held.ToString()));

    private string Release(string text)
    {
        _released = true;
        _held.Clear();
        return text;
    }
}
