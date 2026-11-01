using Akagi.Bridge.Chat.Models;
using Akagi.Web.Pages.Chat.Components;
using Akagi.Web.Pages.Learning.Components;
using Bunit;
using WebCharacter = Akagi.Web.Models.Chat.Character;

namespace Akagi.Web.Tests;

/// <summary>
/// The lesson screens: choosing what to learn, answering in both modes, and the results.
/// </summary>
public class LessonComponentsTests : BunitContext
{
    private static readonly WebCharacter Tutor = new() { Id = "mio", Name = "Mio", CardId = "card", IsTutor = true };

    private static readonly LearningStruggle[] Struggles =
    [
        new() { Id = "jp:a", Kind = StruggleKind.Japanese, Title = "に vs で", Detail = "Location of an action." },
        new() { Id = "vo:b", Kind = StruggleKind.Vocabulary, Title = "微妙" },
        new() { Id = "le:c", Kind = StruggleKind.Lesson, Title = "Traits" },
        new() { Id = "gr:d", Kind = StruggleKind.Grammar, Title = "ている" }
    ];

    private readonly List<LessonSetup.Choice> _started = [];
    private readonly List<LessonPlayer.Answer> _answers = [];
    private int _finished;
    private int _newLessons;

    public LessonComponentsTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    private IRenderedComponent<LessonSetup> RenderSetup(LearningStruggle[] struggles) =>
        Render<LessonSetup>(p => p
            .Add(c => c.Struggles, struggles)
            .Add(c => c.OnStart, choice => _started.Add(choice)));

    private static LearningLesson Lesson(LessonMode mode, params LearningQuestion[] questions) => new()
    {
        Id = "l1",
        Mode = mode,
        Status = LessonStatus.Active,
        Topic = "Rust",
        Intro = "Ready when you are.",
        Questions = questions
    };

    private static LearningQuestion Open(string prompt = "Which one?", bool options = true) =>
        new() { Prompt = prompt, Options = options ? ["one", "two", "three", "four"] : [] };

    private static LearningQuestion Answered(LessonVerdict verdict, int? chosen = null, int? correct = null, string text = "") => new()
    {
        Prompt = "Done?",
        Options = chosen == null ? [] : ["one", "two", "three", "four"],
        Verdict = verdict,
        ChosenOption = chosen,
        CorrectOption = correct,
        AnswerText = text,
        ExpectedAnswer = "the answer",
        Feedback = "Here is why."
    };

    private IRenderedComponent<LessonPlayer> RenderPlayer(LearningLesson lesson, bool busy = false) =>
        Render<LessonPlayer>(p => p
            .Add(c => c.Lesson, lesson)
            .Add(c => c.Tutor, Tutor)
            .Add(c => c.Busy, busy)
            .Add(c => c.OnAnswer, answer => _answers.Add(answer))
            .Add(c => c.OnFinish, () => _finished++));

    [Fact]
    public void Setup_PreselectsTheHeaviestWeakSpots()
    {
        IRenderedComponent<LessonSetup> cut = RenderSetup(Struggles);

        cut.Find("[data-lesson-start]").Click();

        LessonSetup.Choice choice = Assert.Single(_started);
        Assert.Equal(["jp:a", "vo:b", "le:c"], choice.FocusIds);
        Assert.Equal("", choice.Topic);
        Assert.Equal(LessonMode.MultipleChoice, choice.Mode);
        Assert.Equal(5, choice.QuestionCount);
    }

    [Fact]
    public void Setup_WeakSpotsCanBeChosen_AndAllOffDisablesStarting()
    {
        IRenderedComponent<LessonSetup> cut = RenderSetup(Struggles[..2]);

        for (int i = 0; i < 2; i++)
        {
            cut.FindAll("input[type=checkbox]")[i].Change(false);
        }
        Assert.True(cut.Find("[data-lesson-start]").HasAttribute("disabled"));

        cut.FindAll("input[type=checkbox]")[1].Change(true);
        cut.Find("[data-lesson-start]").Click();

        Assert.Equal(["vo:b"], Assert.Single(_started).FocusIds);
    }

    [Fact]
    public void Setup_CustomTopicInTypedMode()
    {
        IRenderedComponent<LessonSetup> cut = RenderSetup(Struggles);

        cut.Find("[data-source=custom]").Click();
        Assert.True(cut.Find("[data-lesson-start]").HasAttribute("disabled"));

        cut.Find("#lesson-topic").Input("  Keigo at work ");
        cut.Find("[data-mode=typed]").Click();
        cut.Find("#lesson-count").Change("10");
        cut.Find("[data-lesson-start]").Click();

        LessonSetup.Choice choice = Assert.Single(_started);
        Assert.Equal("Keigo at work", choice.Topic);
        Assert.Empty(choice.FocusIds);
        Assert.Equal(LessonMode.Typed, choice.Mode);
        Assert.Equal(10, choice.QuestionCount);
    }

    [Fact]
    public void Setup_WithoutWeakSpots_StartsOnCustomTopics()
    {
        IRenderedComponent<LessonSetup> cut = RenderSetup([]);

        Assert.True(cut.Find("[data-source=struggles]").HasAttribute("disabled"));
        Assert.NotNull(cut.Find("#lesson-topic"));
    }

    [Fact]
    public void Player_FourAnswers_OnePicked()
    {
        IRenderedComponent<LessonPlayer> cut = RenderPlayer(Lesson(LessonMode.MultipleChoice, Open(), Open("Next?")));

        Assert.Contains("Ready when you are.", cut.Markup);
        Assert.Equal(4, cut.FindAll("[data-option]").Count);
        Assert.Contains("Question 1 of 2", cut.Markup);
        Assert.Empty(cut.FindAll("textarea"));

        cut.Find("[data-option='2']").Click();

        Assert.Equal(new LessonPlayer.Answer(0, 2, null), Assert.Single(_answers));
    }

    [Fact]
    public void Player_AfterAPick_ShowsRightAndWrongAndFeedback_ThenMovesOn()
    {
        LearningLesson lesson = Lesson(LessonMode.MultipleChoice, Answered(LessonVerdict.Wrong, chosen: 1, correct: 3), Open("Next?"));
        lesson.Questions[0].Prompt = "Which one?";
        IRenderedComponent<LessonPlayer> cut = Render<LessonPlayer>(p => p.Add(c => c.Lesson, Lesson(LessonMode.MultipleChoice, Open(), Open("Next?"))).Add(c => c.Tutor, Tutor));

        cut.Render(p => p.Add(c => c.Lesson, new LearningLesson { Id = "l1", Mode = lesson.Mode, Topic = "Rust", Questions = lesson.Questions }));

        Assert.Contains("btn-danger", cut.Find("[data-option='1']").ClassName);
        Assert.Contains("btn-success", cut.Find("[data-option='3']").ClassName);
        Assert.All(cut.FindAll("[data-option]"), o => Assert.True(o.HasAttribute("disabled")));
        Assert.Contains("Not quite", cut.Markup);
        Assert.Contains("Here is why.", cut.Markup);

        cut.Find("[data-lesson-next]").Click();

        Assert.Contains("Question 2 of 2", cut.Markup);
        Assert.Contains("Next?", cut.Markup);
    }

    [Fact]
    public void Player_Typed_SendsTheAnswerAndClearsForTheNextQuestion()
    {
        IRenderedComponent<LessonPlayer> cut = RenderPlayer(Lesson(LessonMode.Typed, Open(options: false)));

        Assert.Empty(cut.FindAll("[data-option]"));
        Assert.True(cut.Find("[data-answer-submit]").HasAttribute("disabled"));

        cut.Find("textarea").Input("公園で遊ぶ");
        cut.Find("form").Submit();

        Assert.Equal(new LessonPlayer.Answer(0, null, "公園で遊ぶ"), Assert.Single(_answers));
    }

    [Fact]
    public void Player_Typed_ShowsTheModelAnswerUnlessCorrect()
    {
        IRenderedComponent<LessonPlayer> partial = RenderPlayer(Lesson(LessonMode.Typed, Answered(LessonVerdict.Partial, text: "mine")));
        Assert.Contains("Model answer:", partial.Markup);
        Assert.Contains("mine", partial.Markup);
        Assert.Contains("Partly right", partial.Markup);

        IRenderedComponent<LessonPlayer> correct = RenderPlayer(Lesson(LessonMode.Typed, Answered(LessonVerdict.Correct, text: "mine")));
        Assert.DoesNotContain("Model answer:", correct.Markup);
    }

    [Fact]
    public void Player_WhileBusy_NothingCanBeSentTwice()
    {
        IRenderedComponent<LessonPlayer> cut = RenderPlayer(Lesson(LessonMode.Typed, Open(options: false)), busy: true);

        Assert.True(cut.Find("textarea").HasAttribute("disabled"));
        Assert.Contains("Mio is reading...", cut.Find("[data-answer-submit]").TextContent);
        cut.Find("form").Submit();

        Assert.Empty(_answers);
    }

    [Fact]
    public void Player_ResumesAtTheFirstOpenQuestion_AndOffersResultsAtTheEnd()
    {
        IRenderedComponent<LessonPlayer> resumed = RenderPlayer(Lesson(LessonMode.Typed, Answered(LessonVerdict.Correct, text: "x"), Open("Second?", options: false)));
        Assert.Contains("Question 2 of 2", resumed.Markup);
        Assert.Contains("Second?", resumed.Markup);

        IRenderedComponent<LessonPlayer> done = RenderPlayer(Lesson(LessonMode.Typed, Answered(LessonVerdict.Correct, text: "x")));
        done.Find("[data-lesson-results]").Click();

        Assert.Equal(1, _finished);
    }

    [Fact]
    public void Player_CanEndALessonEarly()
    {
        RenderPlayer(Lesson(LessonMode.MultipleChoice, Open())).Find("[data-lesson-end]").Click();

        Assert.Equal(1, _finished);
    }

    [Fact]
    public void Results_ShowScoreClosingAndWhatWentWrong()
    {
        LearningLesson lesson = Lesson(LessonMode.MultipleChoice,
            Answered(LessonVerdict.Correct, chosen: 0, correct: 0),
            Answered(LessonVerdict.Wrong, chosen: 2, correct: 0),
            Open("Skipped?"));
        lesson.Status = LessonStatus.Finished;
        lesson.Score = 1;
        lesson.Closing = "Solid work.";

        IRenderedComponent<LessonResults> cut = Render<LessonResults>(p => p
            .Add(c => c.Lesson, lesson)
            .Add(c => c.Tutor, Tutor)
            .Add(c => c.OnNewLesson, () => _newLessons++));

        Assert.Equal("1 / 2", cut.Find("[data-lesson-score]").TextContent.Trim());
        Assert.Contains("Solid work.", cut.Markup);
        Assert.Equal(2, cut.FindAll("[data-review]").Count);
        Assert.DoesNotContain("Skipped?", cut.Markup);
        Assert.Contains("three", cut.Find("[data-review=Wrong]").TextContent);
        Assert.Contains("the answer", cut.Find("[data-review=Wrong]").TextContent);
        Assert.Contains("Ask Mio about it", cut.Markup);
        Assert.Contains("/Chat/mio/", cut.Markup);

        cut.Find("[data-lesson-new]").Click();
        Assert.Equal(1, _newLessons);
    }

    [Fact]
    public void Tutors_AreMarkedInTheDirectory()
    {
        IRenderedComponent<CharacterAvatar> avatar = Render<CharacterAvatar>(p => p.Add(c => c.Character, Tutor));
        Assert.NotNull(avatar);

        Akagi.Bridge.Chat.Models.Character bridge = new() { Id = "x", IsTutor = true };
        Assert.True(WebCharacter.FromBridge(bridge).IsTutor);
    }
}
