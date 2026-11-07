using Akagi.Characters.Conversations;
using System.Text;

namespace Akagi.Characters.Routines;

/// <summary>
/// Picks at most one routine to fire for a character and phrases the outreach request for it.
/// </summary>
internal static class RoutineEngine
{
    public const string ProactivePrefix = "[Proactive] ";

    public static (Routine Routine, RoutineDecision Decision)? SelectDue(IEnumerable<Routine> routines, RoutineState state)
    {
        foreach (Routine routine in routines.OrderBy(r => r.LastFiredUtc ?? DateTime.MinValue))
        {
            RoutineDecision decision = routine.Evaluate(state);
            if (decision.Fire)
            {
                return (routine, decision);
            }
        }
        return null;
    }

    /// <summary>
    /// The system message that asks the character to write first.
    /// </summary>
    public static string BuildInstruction(Routine routine, RoutineDecision decision)
    {
        StringBuilder builder = new();
        builder.Append(TextMessage.SystemMessagePrefix).Append(ProactivePrefix);
        builder.AppendLine($"Time to reach out to the user first ({(string.IsNullOrWhiteSpace(routine.Name) ? routine.GetType().Name : routine.Name)}: {decision.Reason}).");
        builder.AppendLine(routine.EffectiveInstruction);
        if (!string.IsNullOrWhiteSpace(decision.Details))
        {
            builder.AppendLine("Relevant details:");
            builder.AppendLine(decision.Details);
        }
        // Nothing he said comes with this message, so the model must not invent what his day holds
        builder.AppendLine("Only mention things he actually told you or that your context lists. If you know nothing specific, keep it " +
                           "small and true (how are you, what are you up to) instead of guessing about his day, plans, health or work.");
        builder.Append("Write naturally in your own voice and keep it short. Do not mention schedules, routines or system messages.");
        return builder.ToString();
    }

    /// <summary>
    /// Latest user and character message times across all of a character's conversations.
    /// </summary>
    public static (DateTime? LastUser, DateTime? LastCharacter) LastMessageTimes(Character character)
    {
        DateTime? lastUser = null;
        DateTime? lastCharacter = null;
        foreach (Conversation conversation in character.Conversations.List)
        {
            foreach (Message message in conversation.Messages)
            {
                if (message.From == Message.Type.User && (lastUser == null || message.Time > lastUser))
                {
                    lastUser = message.Time;
                }
                else if (message.From == Message.Type.Character && (lastCharacter == null || message.Time > lastCharacter))
                {
                    lastCharacter = message.Time;
                }
            }
        }
        return (lastUser, lastCharacter);
    }
}
