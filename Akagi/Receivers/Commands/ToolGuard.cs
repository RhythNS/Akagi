using Akagi.Characters.Conversations;
using Akagi.Receivers.Commands.Messages;

namespace Akagi.Receivers.Commands;

/// <summary>
/// Decides per turn which tools a model may use and refuses calls that would store invented or repeated data:
/// state changing tools only on turns the user started (or reflections), facts about the user only with a quote
/// of his words, a few changes per reply, a few tool calls per reply, and no identical call twice in a turn. Refusals
/// are returned to the model as the tool result so it can correct itself.
/// </summary>
internal sealed class ToolGuard
{
    /// <summary>Every refused call's output starts with this, so later checks can tell refusals from results.</summary>
    public const string RefusalPrefix = "Not done: ";

    /// <summary>
    /// Tool calls of one model reply that run; the rest are skipped. One prod reply of a weak model asked for 32 calls
    /// at once, mostly the same board read again.
    /// </summary>
    public const int MaxCallsPerStep = 8;

    public const int MaxChangesPerStep = 3;
    public const int MaxChangesPerTurn = 8;
    /// <summary>
    /// Work item changes (<see cref="IWorkItemCommand"/>) count separately, since planning touches many tickets; each
    /// ticket of a bulk create counts.
    /// </summary>
    public const int MaxWorkItemChangesPerStep = 20;
    public const int MaxWorkItemChangesPerTurn = 40;

    private readonly HashSet<string> _stepCalls = [];
    /// <summary>Every call that ran in this run, with how many changes had run up to it.</summary>
    private readonly Dictionary<string, int> _runCalls = [];
    /// <summary>What <see cref="IWorkItemCommand.OncePerTurn"/> calls have changed in this run.</summary>
    private readonly HashSet<string> _runOnce = [];
    private int _runChanges;
    private int _stepChanges;
    private int _turnChanges;
    private int _stepWorkItemChanges;
    private int _turnWorkItemChanges;

    /// <summary>
    /// Starts a model run: one behavior's loop of steps, the model's own turn. Repeats are only refused within a run,
    /// since the reflectors of one turn share a guard but never see each other's results. Budgets stay per turn.
    /// </summary>
    public void BeginRun()
    {
        _runCalls.Clear();
        _runOnce.Clear();
        _runChanges = 0;
    }

    /// <summary>
    /// Starts a new model step (one model reply with its tool calls).
    /// </summary>
    public void BeginStep()
    {
        _stepCalls.Clear();
        _stepChanges = 0;
        _stepWorkItemChanges = 0;
    }

    /// <summary>
    /// Splits one model reply into the calls that run and those skipped: repeats within the reply and every tool call
    /// past <see cref="MaxCallsPerStep"/>. Messages always run. Skipped calls get no result of their own; the caller
    /// reports them once with <see cref="SkippedNote"/>.
    /// </summary>
    public static (Command[] Run, Command[] Skipped) Limit(IEnumerable<Command> commands, TurnSource turn)
    {
        if (turn == TurnSource.Unknown)
        {
            return ([.. commands], []);
        }
        List<Command> run = [];
        List<Command> skipped = [];
        HashSet<string> seen = [];
        int calls = 0;
        foreach (Command command in commands)
        {
            if (command is MessageCommand)
            {
                run.Add(command);
            }
            else if (!seen.Add(CallKey(command)) || calls >= MaxCallsPerStep)
            {
                skipped.Add(command);
            }
            else
            {
                calls++;
                run.Add(command);
            }
        }
        return ([.. run], [.. skipped]);
    }

    /// <summary>The tool output telling the model that <paramref name="skipped"/> calls of its reply did not run.</summary>
    public static string SkippedNote(int skipped) =>
        RefusalPrefix + $"{skipped} more tool call(s) of this reply were skipped: repeats of another call, or past the " +
        $"limit of {MaxCallsPerStep} calls per reply. Read the results above before calling again, and never repeat a call.";

    /// <summary>
    /// Whether the tool may be offered to the model at all on this kind of turn. <paramref name="consultedOn"/> is
    /// what started the persona's turn when this is a consultation.
    /// </summary>
    public static bool IsOffered(Command command, TurnSource turn, TurnSource consultedOn = TurnSource.Unknown)
    {
        if (command is not IStateCommand)
        {
            return true;
        }
        if (turn == TurnSource.Consultation)
        {
            return command is IWorkItemCommand && IsUserDriven(consultedOn);
        }
        return turn != TurnSource.System;
    }

    public static Command[] Offered(IEnumerable<Command> commands, TurnSource turn, TurnSource consultedOn = TurnSource.Unknown) =>
        [.. commands.Where(c => IsOffered(c, turn, consultedOn))];

    /// <summary>A consultation the persona made while answering the user (or with no checks at all).</summary>
    private static bool IsUserDriven(TurnSource consultedOn) => consultedOn is TurnSource.User or TurnSource.Unknown;

    /// <summary>
    /// Null when the call may run, otherwise the tool output explaining why it did not.
    /// </summary>
    public string? Check(Command command, Context context)
    {
        if (context.Turn == TurnSource.Unknown)
        {
            return null;
        }
        string key = CallKey(command);
        if (command is not IStateCommand)
        {
            // Reading the same thing again only helps after something changed
            if (_runCalls.TryGetValue(key, out int changesBefore) && changesBefore == _runChanges)
            {
                return RefusalPrefix + "nothing changed since you made this exact call; its result is above.";
            }
            _runCalls[key] = _runChanges;
            return null;
        }

        if (context.Turn == TurnSource.System)
        {
            return RefusalPrefix + $"{command.Name} is not available while you reach out on your own. Only record or change " +
                   "things when he tells you about them; right now just write to him.";
        }
        if (context.Turn == TurnSource.Consultation && !(command is IWorkItemCommand && IsUserDriven(context.ConsultedOn)))
        {
            return RefusalPrefix + $"{command.Name} is not available during a consultation. Only answer the question; the " +
                   "persona records things herself when he tells her.";
        }

        if (!_stepCalls.Add(key))
        {
            return RefusalPrefix + "identical to another call in this reply.";
        }
        if (_runCalls.ContainsKey(key))
        {
            // Includes calls that failed, e.g. a move to a column the board does not have
            return RefusalPrefix + "you made this exact call earlier in this turn; its result is above. Do not repeat it.";
        }
        if (command is IWorkItemCommand workItem)
        {
            if (workItem.OncePerTurn is string once && _runOnce.Contains(once))
            {
                return RefusalPrefix + $"{command.Name} already ran for this in this turn. Put everything into that one call.";
            }
            int changes = Math.Max(1, workItem.Changes);
            if (_stepWorkItemChanges + changes > MaxWorkItemChangesPerStep)
            {
                int left = MaxWorkItemChangesPerStep - _stepWorkItemChanges;
                return RefusalPrefix + $"at most {MaxWorkItemChangesPerStep} board changes per reply" + (left > 0
                    ? $"; this call makes {changes} and only {left} more fit. Do at most {left} now and the rest in the next reply."
                    : ". Continue in the next reply.");
            }
            if (_turnWorkItemChanges + changes > MaxWorkItemChangesPerTurn)
            {
                int left = MaxWorkItemChangesPerTurn - _turnWorkItemChanges;
                return RefusalPrefix + $"at most {MaxWorkItemChangesPerTurn} board changes per turn" + (left > 0
                    ? $"; this call makes {changes} and only {left} more fit. Do at most {left} now, then tell him what is left."
                    : ". Tell him what is done and what is left.");
            }
            _stepWorkItemChanges += changes;
            _turnWorkItemChanges += changes;
            if (workItem.OncePerTurn is string ran)
            {
                _runOnce.Add(ran);
            }
            Passed(key);
            return null;
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
        Passed(key);
        return null;
    }

    private void Passed(string key)
    {
        _runChanges++;
        _runCalls[key] = _runChanges;
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
        DateTime now = DateTime.UtcNow;
        IReadOnlyList<string> userTexts = Grounding.RecentUserTexts(messages, now, context.EvidenceAfterUtc);
        if (!Grounding.IsQuoted(evidence, userTexts))
        {
            // A reflection sees older messages for context, but those were reflected on before
            if (context.EvidenceAfterUtc != null && Grounding.IsQuoted(evidence, Grounding.RecentUserTexts(messages, now)))
            {
                return RefusalPrefix + $"'{Shorten(evidence)}' is from a message you already reflected on, so it is recorded " +
                       "already. Only record what he wrote after that; if there is nothing new, stop.";
            }
            if (userTexts.Count == 0)
            {
                return RefusalPrefix + "he has not written anything recently, so there is nothing to record. Never record " +
                       "things he did not tell you.";
            }
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
