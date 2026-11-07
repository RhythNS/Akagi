using Akagi.Characters.Conversations;

namespace Akagi.Receivers.Commands;

/// <summary>
/// Decides per turn which tools a model may use and refuses calls that would store invented or repeated data:
/// state changing tools only on turns the user started (or reflections), facts about the user only with a quote
/// of his words, a few changes per reply, and no identical call twice. Refusals are returned to the model as the
/// tool result so it can correct itself.
/// </summary>
internal sealed class ToolGuard
{
    /// <summary>Every refused call's output starts with this, so later checks can tell refusals from results.</summary>
    public const string RefusalPrefix = "Not done: ";

    public const int MaxChangesPerStep = 3;
    public const int MaxChangesPerTurn = 8;

    private readonly HashSet<string> _stepCalls = [];
    private int _stepChanges;
    private int _turnChanges;

    /// <summary>
    /// Starts a new model step (one model reply with its tool calls).
    /// </summary>
    public void BeginStep()
    {
        _stepCalls.Clear();
        _stepChanges = 0;
    }

    /// <summary>
    /// Whether the tool may be offered to the model at all on this kind of turn.
    /// </summary>
    public static bool IsOffered(Command command, TurnSource turn)
    {
        return command is not IStateCommand || turn is not (TurnSource.System or TurnSource.Consultation);
    }

    public static Command[] Offered(IEnumerable<Command> commands, TurnSource turn) => [.. commands.Where(c => IsOffered(c, turn))];

    /// <summary>
    /// Null when the call may run, otherwise the tool output explaining why it did not.
    /// </summary>
    public string? Check(Command command, Context context)
    {
        if (context.Turn == TurnSource.Unknown || command is not IStateCommand)
        {
            return null;
        }

        if (context.Turn == TurnSource.System)
        {
            return RefusalPrefix + $"{command.Name} is not available while you reach out on your own. Only record or change " +
                   "things when he tells you about them; right now just write to him.";
        }
        if (context.Turn == TurnSource.Consultation)
        {
            return RefusalPrefix + $"{command.Name} is not available during a consultation. Only answer the question; the " +
                   "persona records things herself when he tells her.";
        }

        if (!_stepCalls.Add(CallKey(command)))
        {
            return RefusalPrefix + "identical to another call in this reply.";
        }
        if (_stepChanges >= MaxChangesPerStep)
        {
            return RefusalPrefix + $"at most {MaxChangesPerStep} changes per reply. Pick only what matters most.";
        }
        if (_turnChanges >= MaxChangesPerTurn)
        {
            return RefusalPrefix + $"at most {MaxChangesPerTurn} changes per turn. Stop recording and answer him.";
        }

        if (command is IUserFactCommand)
        {
            string? refusal = CheckEvidence(command, context);
            if (refusal != null)
            {
                return refusal;
            }
        }

        _stepChanges++;
        _turnChanges++;
        return null;
    }

    private static string? CheckEvidence(Command command, Context context)
    {
        string? evidence = command.Arguments
            .FirstOrDefault(a => string.Equals(a.Name, Grounding.EvidenceName, StringComparison.OrdinalIgnoreCase))?.Value;
        if (string.IsNullOrWhiteSpace(evidence))
        {
            return RefusalPrefix + $"put his own words into {Grounding.EvidenceName}, copied from one of his messages. If he did " +
                   "not say it, do not record it.";
        }

        IReadOnlyList<Message> messages = context.Conversation.Messages;
        IReadOnlyList<string> userTexts = Grounding.RecentUserTexts(messages, DateTime.UtcNow);
        if (userTexts.Count == 0)
        {
            return RefusalPrefix + "he has not written anything recently, so there is nothing to record. Never record " +
                   "things he did not tell you.";
        }
        if (!Grounding.IsQuoted(evidence, userTexts))
        {
            return RefusalPrefix + $"'{Shorten(evidence)}' is not in any of his recent messages. {Grounding.EvidenceName} must " +
                   "be his exact words; only record what he actually told you.";
        }

        string normalized = Grounding.Normalize(evidence);
        CommandMessage? earlier = messages
            .OfType<CommandMessage>()
            .LastOrDefault(m => string.Equals(m.Command.Name, command.Name, StringComparison.OrdinalIgnoreCase)
                && !m.Output.StartsWith(RefusalPrefix, StringComparison.Ordinal)
                && Grounding.Normalize(EvidenceOf(m.Command)) == normalized);
        if (earlier != null)
        {
            return RefusalPrefix + $"already done for these words: {Shorten(earlier.Output)}";
        }
        return null;
    }

    private static string? EvidenceOf(Command command)
    {
        return command.Arguments
            .FirstOrDefault(a => string.Equals(a.Name, Grounding.EvidenceName, StringComparison.OrdinalIgnoreCase))?.Value;
    }

    private static string CallKey(Command command)
    {
        IEnumerable<string> arguments = command.Arguments
            .Where(a => !string.IsNullOrWhiteSpace(a.Value))
            .OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
            .Select(a => $"{a.Name.ToLowerInvariant()}={Grounding.Normalize(a.Value)}");
        return command.Name.ToLowerInvariant() + "(" + string.Join(";", arguments) + ")";
    }

    private static string Shorten(string text)
    {
        string single = text.ReplaceLineEndings(" ").Trim();
        return single.Length <= 160 ? single : single[..157] + "...";
    }
}
