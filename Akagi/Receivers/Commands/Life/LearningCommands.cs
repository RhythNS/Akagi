using Akagi.Bridge.Chat.Models;

namespace Akagi.Receivers.Commands.Life;

/// <summary>
/// Tools the lesson author offers the Learning LLM. They only carry the arguments; the author validates and stores them.
/// </summary>
internal abstract class LessonToolCommand : LifeCommand
{
    public override Task<Command[]> Execute(Context context) => Task.FromResult(Reply(context, "Recorded."));
}

internal class AddLessonQuestionCommand : LessonToolCommand
{
    public override string Name => "AddLessonQuestion";

    public override string Description => "Add one question to the lesson. Call it once per question.";

    public override Argument[] GetDefaultArguments() =>
    [
        Text("Concept", "What this question tests, in a few words.", required: true),
        Text("Question", "The question as the student reads it.", required: true),
        Text("Answer", "The correct answer.", required: true),
        Text("Wrong1", "A plausible wrong answer. Required for four answer lessons."),
        Text("Wrong2", "A second plausible wrong answer. Required for four answer lessons."),
        Text("Wrong3", "A third plausible wrong answer. Required for four answer lessons."),
        Text("Explanation", "One to three sentences in your own voice on why the answer is right and what the trap is.", required: true),
        Text("FocusId", "The id of the weak spot this question works on, if the lesson lists any.")
    ];

    public string Concept => GetArgument("Concept") ?? string.Empty;
    public string Question => GetArgument("Question") ?? string.Empty;
    public string Answer => GetArgument("Answer") ?? string.Empty;
    public string Explanation => GetArgument("Explanation") ?? string.Empty;
    public string FocusId => GetArgument("FocusId") ?? string.Empty;
    public string[] WrongAnswers => [.. new[] { "Wrong1", "Wrong2", "Wrong3" }.Select(GetArgument).OfType<string>()];
}

internal class SetLessonIntroCommand : LessonToolCommand
{
    public override string Name => "SetLessonIntro";

    public override string Description => "Set the one or two sentences you open the lesson with.";

    public override Argument[] GetDefaultArguments() =>
    [
        Text("Intro", "What you say to the student before the first question.", required: true)
    ];

    public string Intro => GetArgument("Intro") ?? string.Empty;
}

internal class GradeLessonAnswerCommand : LessonToolCommand
{
    public override string Name => "GradeLessonAnswer";

    public override string Description => "Grade the student's typed answer.";

    public override Argument[] GetDefaultArguments() =>
    [
        Text("Verdict", "Correct, Partial or Wrong.", required: true),
        Text("Feedback", "Two or three sentences in your own voice to the student: what was right, what was off, and how to remember it.", required: true)
    ];

    public LessonVerdict? Verdict => GetArgument("Verdict")?.ToLowerInvariant() switch
    {
        "correct" => LessonVerdict.Correct,
        "partial" => LessonVerdict.Partial,
        "wrong" => LessonVerdict.Wrong,
        _ => null
    };

    public string Feedback => GetArgument("Feedback") ?? string.Empty;
}

internal class CloseLessonCommand : LessonToolCommand
{
    public override string Name => "CloseLesson";

    public override string Description => "Say your closing words to the student.";

    public override Argument[] GetDefaultArguments() =>
    [
        Text("Message", "Two or three sentences in your own voice: how it went, and the one thing to take away.", required: true)
    ];

    public string Message => GetArgument("Message") ?? string.Empty;
}
