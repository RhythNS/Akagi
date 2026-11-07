using Akagi.Bridge.Chat.Models;
using Character = Akagi.Characters.Character;
using Command = Akagi.Receivers.Commands.Command;
using Conversation = Akagi.Characters.Conversation;
using TextMessage = Akagi.Characters.Conversations.TextMessage;
using Akagi.Characters;
using Akagi.Characters.Cards;
using Akagi.Characters.CharacterBehaviors.MessageCompilers;
using Akagi.Characters.CharacterBehaviors.SystemProcessors;
using Akagi.Characters.Conversations;
using Akagi.Communication;
using Akagi.Data;
using Akagi.LLMs;
using Akagi.Receivers;
using Akagi.Receivers.Commands;
using Akagi.Receivers.Commands.Messages;
using Akagi.Users;
using System.Globalization;
using System.Text;

namespace Akagi.Life.Japanese.Calls;

/// <summary>
/// Writes a short study plan from what the calls show, with one request to the user's Learning model.
/// </summary>
internal interface IStudyAdvisor
{
    public Task<string> WriteAsync(User user, CallInsights insights, CancellationToken cancellationToken);
}

internal class StudyAdvisor : IStudyAdvisor
{
    public const int MaxStruggles = 10;
    public const int MaxCalls = 8;

    internal const string Instruction =
        "You are a Japanese conversation coach. The learner speaks casual Japanese in voice calls with Japanese friends and " +
        "is working towards natural, N2-level everyday Japanese. Below is what the analysis of their recorded calls found. " +
        "Write a short, concrete study plan in English:\n" +
        "1. The two or three struggles to focus on now and why, each with a natural example (what they said and a better way).\n" +
        "2. Words or expressions to use less, and what natives say instead.\n" +
        "3. Words to start using, each with one natural example sentence.\n" +
        "4. One concrete thing to try in the next call.\n" +
        "Use short headings and bullet points, stay under 250 words, and only use what the data shows: do not invent mistakes " +
        "or habits. Struggles marked [focus] are the ones the learner already chose; say how they are going.";

    private readonly ILLMFactory _llms;
    private readonly IDatabaseFactory _databaseFactory;

    public StudyAdvisor(ILLMFactory llms, IDatabaseFactory databaseFactory)
    {
        _llms = llms;
        _databaseFactory = databaseFactory;
    }

    public async Task<string> WriteAsync(User user, CallInsights insights, CancellationToken cancellationToken)
    {
        Conversation scratch = new() { Id = -1, Time = DateTime.UtcNow };
        scratch.AddMessage(new TextMessage
        {
            From = Message.Type.System,
            Time = DateTime.UtcNow,
            Text = Describe(insights),
            CharacterId = null
        });

        SystemProcessor processor = new()
        {
            Name = "Study advice",
            Description = "Study advice",
            RunMode = ILLM.RunMode.TextOnly,
            Usage = ILLM.LLMUsage.Learning,
            ReadableMessages = Message.Type.System,
            Output = Message.Type.System,
            MessageCompiler = new ContextOnlyConversationCompiler(),
            SystemInstruction = Instruction,
            CommandNames = []
        };
        await processor.Init([], _databaseFactory);

        Character coach = new() { Name = "Coach" };
        coach.Init(new Card { Name = "Coach" });
        // Not disposed: nothing of this one-off request is saved
        Context context = new()
        {
            Character = coach,
            Conversation = scratch,
            User = user,
            Communicator = new SilentCommunicator(),
            LLMFactory = _llms,
            DatabaseFactory = _databaseFactory
        };

        ILLM llm = await _llms.Create(user, null, ILLM.LLMUsage.Learning);
        Command[] steps = await llm.GetNextSteps(processor, context);
        string text = string.Join(Environment.NewLine, steps.OfType<TextMessageCommand>().Select(s => s.Arguments.FirstOrDefault()?.Value ?? string.Empty)).Trim();
        return text.Length > 0 ? text : throw new InvalidOperationException("The model wrote no advice.");
    }

    /// <summary>
    /// The data the plan is written from: picked and recurring struggles, word habits, and the trends of the latest calls.
    /// </summary>
    internal static string Describe(CallInsights insights)
    {
        StringBuilder text = new();
        text.AppendLine($"Calls in view: {insights.Calls.Length}.");

        if (insights.FocusStruggles.Length > 0)
        {
            text.AppendLine();
            text.AppendLine("Struggles the learner chose to focus on:");
            foreach (InsightStruggle struggle in insights.FocusStruggles)
            {
                string rates = struggle.RateBefore is double before
                    ? $", {Number(before)} per 1,000 characters before focusing" + (struggle.RateSince is double since ? $", {Number(since)} since" : string.Empty)
                    : string.Empty;
                text.AppendLine($"- [focus] {struggle.Pattern} ({struggle.Category}){rates}");
            }
        }

        text.AppendLine();
        text.AppendLine("Recurring struggles (most serious and frequent first):");
        foreach (InsightStruggle struggle in insights.Struggles.Take(MaxStruggles))
        {
            string example = struggle.Example is CallFindingView view && view.Said.Length > 0
                ? $" e.g. 「{view.Said}」" + (view.Better.Length > 0 ? $" -> 「{view.Better}」" : string.Empty)
                : string.Empty;
            text.AppendLine($"- {(struggle.Focus ? "[focus] " : string.Empty)}{struggle.Pattern} ({struggle.Category}, severity {struggle.Severity}): {struggle.Times}x in {struggle.Calls} call(s){example}");
        }

        List<InsightAdvice> words = [.. insights.Advice.Where(a => a.Kind is "use-less" or "use-more")];
        if (words.Count > 0)
        {
            text.AppendLine();
            text.AppendLine("Word habits measured in the transcripts:");
            foreach (InsightAdvice advice in words)
            {
                text.AppendLine($"- {advice.Title}: {advice.Detail}");
            }
        }

        if (insights.HeardNotSaid.Length > 0)
        {
            text.AppendLine();
            text.AppendLine("Words partners used that the learner never said: " +
                string.Join(", ", insights.HeardNotSaid.Take(15).Select(w => $"{w.Lemma} ({w.Partner}x)")));
        }

        text.AppendLine();
        text.AppendLine("Latest calls (date: speaking share, fillers per 100 characters, questions asked, mistakes):");
        foreach (CallTrendPoint call in insights.Calls.TakeLast(MaxCalls))
        {
            string share = call.LearnerShare is double value ? (value * 100).ToString("0", CultureInfo.InvariantCulture) + "%" : "-";
            string fillers = call.FillersPer100 is double filler ? Number(filler) : "-";
            text.AppendLine($"- {call.Date:yyyy-MM-dd} {call.Name}: {share}, {fillers}, {(call.Separated ? call.LearnerQuestions.ToString(CultureInfo.InvariantCulture) : "-")}, {call.IssueCount}");
        }
        return text.ToString();
    }

    private static string Number(double value) => value.ToString("0.0", CultureInfo.InvariantCulture);
}
