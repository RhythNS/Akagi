using Akagi.Bridge.Chat.Models;
using Akagi.Learning;
using System.Text;

namespace Akagi.Communication.Commands.Life;

internal class LearnCommand : TextCommand
{
    public override string Name => "/learn";

    public override string Description =>
        "Lessons with your tutor on your weak spots or a custom topic, with four answers to pick from or typed answers. " +
        "Usage: /learn start [choice|typed] [topic...] | /learn answer <answer...> | /learn stop | /learn (status)";

    private readonly ILearningService _learning;

    public LearnCommand(ILearningService learning)
    {
        _learning = learning;
    }

    public override async Task<CommandResult> ExecuteAsync(Context context, string[] args)
    {
        string action = args.Length > 0 ? args[0].ToLowerInvariant() : "status";
        string[] rest = [.. args.Skip(1)];
        try
        {
            switch (action)
            {
                case "start":
                    await StartAsync(context, rest);
                    return CommandResult.Ok;

                case "answer":
                    await AnswerAsync(context, rest);
                    return CommandResult.Ok;

                case "stop":
                    await StopAsync(context);
                    return CommandResult.Ok;

                case "status":
                    await StatusAsync(context);
                    return CommandResult.Ok;

                default:
                    await Communicator.SendMessage(context.User, Description);
                    return CommandResult.Fail("Unknown action.");
            }
        }
        catch (LearningException ex)
        {
            await Communicator.SendMessage(context.User, ex.Message);
            return CommandResult.Fail(ex.Message);
        }
    }

    private async Task StartAsync(Context context, string[] args)
    {
        LessonMode mode = LessonMode.MultipleChoice;
        if (args.Length > 0 && ParseMode(args[0]) is LessonMode chosen)
        {
            mode = chosen;
            args = args[1..];
        }

        LearningState state = await _learning.GetStateAsync(context.User.Id!);
        await Communicator.SendMessage(context.User, $"{state.Tutor.Name} is preparing your lesson...");
        Lesson lesson = await _learning.StartAsync(context.User.Id!, new LessonRequest(mode, string.Join(' ', args), [], 0));

        StringBuilder text = new();
        if (lesson.Intro.Length > 0)
        {
            text.AppendLine($"{state.Tutor.Name}: {lesson.Intro}");
            text.AppendLine();
        }
        text.Append(DescribeQuestion(lesson, lesson.Questions[0]));
        await Communicator.SendMessage(context.User, text.ToString());
    }

    private async Task AnswerAsync(Context context, string[] args)
    {
        LearningState state = await _learning.GetStateAsync(context.User.Id!);
        Lesson? lesson = state.Current;
        LessonQuestion? question = lesson?.NextQuestion;
        if (lesson == null || lesson.Status != LessonStatus.Active || question == null)
        {
            await Communicator.SendMessage(context.User, "There is no question waiting. Start a lesson with /learn start.");
            return;
        }

        string answer = string.Join(' ', args);
        int? option = lesson.Mode == LessonMode.MultipleChoice ? ParseOption(answer) : null;
        int index = lesson.Questions.IndexOf(question);
        lesson = await _learning.AnswerAsync(context.User.Id!, lesson.Id!, index, option, answer);

        StringBuilder text = new();
        text.AppendLine(DescribeResult(lesson.Questions[index]));
        text.AppendLine($"{state.Tutor.Name}: {lesson.Questions[index].Feedback}");
        text.AppendLine();

        if (lesson.NextQuestion is LessonQuestion next)
        {
            text.Append(DescribeQuestion(lesson, next));
        }
        else
        {
            lesson = await _learning.FinishAsync(context.User.Id!, lesson.Id!);
            text.Append(DescribeEnd(lesson, state.Tutor.Name));
        }
        await Communicator.SendMessage(context.User, text.ToString().TrimEnd());
    }

    private async Task StopAsync(Context context)
    {
        LearningState state = await _learning.GetStateAsync(context.User.Id!);
        if (state.Current == null)
        {
            await Communicator.SendMessage(context.User, "No lesson is running.");
            return;
        }
        Lesson lesson = await _learning.FinishAsync(context.User.Id!, state.Current.Id!);
        await Communicator.SendMessage(context.User, lesson.Status == LessonStatus.Finished ? DescribeEnd(lesson, state.Tutor.Name) : "Lesson stopped.");
    }

    private async Task StatusAsync(Context context)
    {
        LearningState state = await _learning.GetStateAsync(context.User.Id!);
        if (state.Current?.NextQuestion is LessonQuestion question)
        {
            await Communicator.SendMessage(context.User, DescribeQuestion(state.Current, question));
            return;
        }

        StringBuilder text = new();
        text.AppendLine($"Your tutor is {state.Tutor.Name}.");
        if (state.Struggles.Count > 0)
        {
            text.AppendLine("Current weak spots:");
            foreach (Struggle struggle in state.Struggles.Take(5))
            {
                text.AppendLine($"- {struggle.Title}");
            }
        }
        text.Append("Start with /learn start, optionally with choice or typed and a topic.");
        await Communicator.SendMessage(context.User, text.ToString());
    }

    internal static LessonMode? ParseMode(string text) => text.ToLowerInvariant() switch
    {
        "choice" or "choices" or "multiple" => LessonMode.MultipleChoice,
        "typed" or "type" or "write" => LessonMode.Typed,
        _ => null
    };

    /// <summary>
    /// Reads A to D or 1 to 4 into an option index.
    /// </summary>
    internal static int? ParseOption(string text)
    {
        string answer = text.Trim();
        if (answer.Length != 1)
        {
            return null;
        }
        char letter = char.ToUpperInvariant(answer[0]);
        return letter switch
        {
            >= 'A' and <= 'D' => letter - 'A',
            >= '1' and <= '4' => letter - '1',
            _ => null
        };
    }

    internal static string DescribeQuestion(Lesson lesson, LessonQuestion question)
    {
        StringBuilder text = new();
        text.AppendLine($"Question {lesson.Questions.IndexOf(question) + 1} of {lesson.Questions.Count}");
        text.AppendLine(question.Prompt);
        if (lesson.Mode == LessonMode.MultipleChoice)
        {
            for (int i = 0; i < question.Options.Length; i++)
            {
                text.AppendLine($"{(char)('A' + i)}) {question.Options[i]}");
            }
            text.Append("Reply with /learn answer A, B, C or D.");
        }
        else
        {
            text.Append("Reply with /learn answer followed by your answer.");
        }
        return text.ToString();
    }

    private static string DescribeResult(LessonQuestion question)
    {
        string verdict = question.Verdict switch
        {
            LessonVerdict.Correct => "Correct.",
            LessonVerdict.Partial => "Partly right.",
            _ => "Not quite."
        };
        return question.Verdict == LessonVerdict.Correct ? verdict : $"{verdict} Answer: {question.Answer}";
    }

    private static string DescribeEnd(Lesson lesson, string tutorName)
    {
        string score = $"Lesson finished: {lesson.Score:0.#} of {lesson.AnsweredCount}.";
        return lesson.Closing.Length > 0 ? $"{score}{Environment.NewLine}{tutorName}: {lesson.Closing}" : score;
    }
}
