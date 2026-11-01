using Akagi.Bridge.Attributes;
using Akagi.Bridge.Chat.Models;
using Akagi.Characters.Conversations;
using Akagi.Learning;
using Akagi.Life;
using Akagi.Life.Japanese;
using Akagi.Receivers;
using System.Text;
using TextMessage = Akagi.Characters.Conversations.TextMessage;

namespace Akagi.Characters.CharacterBehaviors.MessageCompilers.Injections;

/// <summary>
/// Injects the user's lessons, so a question about one reaches a tutor who knows what happened in it.
/// </summary>
[NodeDocu("Injects the latest lesson with its questions, plus the user's current weak spots.")]
internal class LearningInjectionCompiler : InjectionCompiler
{
    private int _struggleCount = 6;
    private string _header = "### {{user}}'s lessons";

    [NodeDocu("How many weak spots to show.")]
    public int StruggleCount
    {
        get => _struggleCount;
        set => SetProperty(ref _struggleCount, value);
    }
    [NodeDocu("Heading above the injected block. {{user}} and {{character}} are replaced with names.")]
    public string Header
    {
        get => _header;
        set => SetProperty(ref _header, value);
    }

    protected override async Task<Message[]> GetInjectionMessagesAsync(Context context)
    {
        if (string.IsNullOrEmpty(context.User.Id))
        {
            return [];
        }

        DateTime now = DateTime.UtcNow;
        List<Lesson> lessons = await context.DatabaseFactory.GetDatabase<ILessonDatabase>().GetForUser(context.User.Id);
        JapaneseDiagnosis? diagnosis = await context.DatabaseFactory.GetDatabase<IJapaneseDiagnosisDatabase>().GetForUser(context.User.Id);
        LifeProfile profile = await context.DatabaseFactory.GetDatabase<ILifeProfileDatabase>().GetOrCreate(context.User.Id);

        string body = Render(lessons, StruggleFinder.Find(diagnosis, profile, lessons, now), now, StruggleCount);
        if (string.IsNullOrWhiteSpace(body))
        {
            return [];
        }

        string header = Header
            .Replace("{{user}}", context.User.Name)
            .Replace("{{character}}", context.Character.Name);
        return
        [
            new TextMessage
            {
                From = MessageType,
                Time = DateTime.UtcNow,
                Text = string.IsNullOrWhiteSpace(header) ? body : header + Environment.NewLine + body,
                CharacterId = context.Character.Id
            }
        ];
    }

    internal static string Render(IEnumerable<Lesson> newestFirst, IReadOnlyList<Struggle> struggles, DateTime nowUtc, int struggleCount)
    {
        StringBuilder text = new();
        Lesson? latest = newestFirst.FirstOrDefault(l => l.Status is LessonStatus.Finished or LessonStatus.Active);
        if (latest != null)
        {
            string state = latest.Status == LessonStatus.Active ? "in progress, " : string.Empty;
            text.AppendLine($"Latest lesson: {latest.Topic} ({state}{latest.Score:0.#}/{latest.AnsweredCount}, {LifeClock.FormatAge(nowUtc - latest.CreatedAt)})");
            foreach (LessonQuestion question in latest.Questions.Where(q => q.IsAnswered))
            {
                string answer = question.ChosenOption is int chosen && chosen < question.Options.Length ? question.Options[chosen] : question.AnswerText;
                text.AppendLine($"- {question.Verdict}: {question.Prompt} They answered: {answer}. Correct: {question.Answer}");
            }
        }

        if (struggles.Count > 0)
        {
            text.AppendLine("Current weak spots:");
            foreach (Struggle struggle in struggles.Take(Math.Max(1, struggleCount)))
            {
                text.AppendLine($"- {struggle.Title}: {struggle.Detail}");
            }
        }
        return text.ToString().TrimEnd();
    }
}
