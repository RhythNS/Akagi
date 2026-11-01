using Akagi.Bridge.Chat.Models;
using Akagi.Learning;
using Akagi.Life;
using Akagi.Life.Japanese;
using Akagi.Life.Study;

namespace Akagi.Tests.Learning;

public class LearningServiceTests
{
    private readonly LearningFixture _fixture = new();

    private async Task<DiagnosisIssue> SeedIssue(string pattern = "に vs で")
    {
        JapaneseDiagnosis diagnosis = await _fixture.Diagnoses.GetOrCreate(_fixture.UserId);
        DiagnosisIssue issue = diagnosis.RecordIssue(IssueCategory.Particle, pattern, "Location of an action.",
            new IssueExample { Said = "公園に遊ぶ", Better = "公園で遊ぶ" }, 2, DateTime.UtcNow).Issue;
        await _fixture.Diagnoses.SaveDocumentAsync(diagnosis);
        return issue;
    }

    [Theory]
    [InlineData(LessonMode.MultipleChoice)]
    [InlineData(LessonMode.Typed)]
    public async Task Start_WithCustomTopic_PreparesAnActiveLesson(LessonMode mode)
    {
        Lesson lesson = await _fixture.StartCustom(mode, 3);

        Assert.Equal(LessonStatus.Active, lesson.Status);
        Assert.Equal(mode, lesson.Mode);
        Assert.Equal("Rust lifetimes", lesson.Topic);
        Assert.Equal("Let's go.", lesson.Intro);
        Assert.Equal(3, lesson.Questions.Count);
        Assert.Equal(_fixture.Tutor.Id, lesson.CharacterId);
        Assert.Empty(_fixture.Author.LastBrief!.Focus);
    }

    [Fact]
    public async Task Start_WithoutTopic_WorksOnTheWeakSpots()
    {
        DiagnosisIssue issue = await SeedIssue();

        Lesson lesson = await _fixture.Service.StartAsync(_fixture.UserId, new LessonRequest(LessonMode.MultipleChoice, "  ", [], 2));

        Assert.Equal("", lesson.CustomTopic);
        Assert.Equal(issue.Pattern, lesson.Topic);
        Assert.Equal(Struggle.JapanesePrefix + issue.Id, Assert.Single(_fixture.Author.LastBrief!.Focus).Id);
    }

    [Fact]
    public async Task Start_WithChosenWeakSpots_UsesOnlyThose()
    {
        await SeedIssue("first");
        DiagnosisIssue chosen = await SeedIssue("second");

        await _fixture.Service.StartAsync(_fixture.UserId, new LessonRequest(LessonMode.Typed, "", [Struggle.JapanesePrefix + chosen.Id], 2));

        Assert.Equal("second", Assert.Single(_fixture.Author.LastBrief!.Focus).Title);
    }

    [Fact]
    public async Task Start_WithNothingToWorkOn_AsksForATopic()
    {
        LearningException error = await Assert.ThrowsAsync<LearningException>(() =>
            _fixture.Service.StartAsync(_fixture.UserId, new LessonRequest(LessonMode.Typed, "", [], 0)));

        Assert.Contains("Pick a topic", error.Message);
        Assert.Empty(_fixture.Lessons.Items);
    }

    [Theory]
    [InlineData(0, LearningService.DefaultQuestionCount)]
    [InlineData(99, LearningService.MaxQuestionCount)]
    public async Task Start_ClampsTheQuestionCount(int asked, int expected)
    {
        Lesson lesson = await _fixture.StartCustom(count: asked);

        Assert.Equal(expected, lesson.Questions.Count);
    }

    [Fact]
    public async Task Start_WhenWritingFails_LeavesAFailedLessonAndAReadableError()
    {
        _fixture.Author.WriteFailure = new HttpRequestException("500 from the provider");

        LearningException error = await Assert.ThrowsAsync<LearningException>(() => _fixture.StartCustom());

        Assert.DoesNotContain("provider", error.Message);
        Assert.Equal(LessonStatus.Failed, Assert.Single(_fixture.Lessons.Items).Status);
        Assert.Null((await _fixture.Service.GetStateAsync(_fixture.UserId)).Current);
    }

    [Fact]
    public async Task Start_WhilePreparing_IsRefusedUntilThePreparationIsStale()
    {
        Lesson stuck = new() { UserId = _fixture.UserId, Status = LessonStatus.Preparing };
        await _fixture.Lessons.SaveDocumentAsync(stuck);

        await Assert.ThrowsAsync<LearningException>(() => _fixture.StartCustom());

        stuck.CreatedAt = DateTime.UtcNow - LearningService.PrepareTimeout - TimeSpan.FromMinutes(1);
        Lesson lesson = await _fixture.StartCustom();

        Assert.Equal(LessonStatus.Active, lesson.Status);
        Assert.Equal(LessonStatus.Abandoned, stuck.Status);
    }

    [Fact]
    public async Task Start_ConcludesARunningLessonSoItsAnswersCount()
    {
        Lesson first = await _fixture.StartCustom();
        await _fixture.Service.AnswerAsync(_fixture.UserId, first.Id!, 0, 0, null);

        await _fixture.StartCustom();

        Assert.Equal(LessonStatus.Finished, first.Status);
        Assert.Equal(1, _fixture.Profiles.Items.Single().Study.Days.Single().Reviews);
    }

    [Fact]
    public async Task MultipleChoice_IsGradedWithoutTheLLM()
    {
        Lesson lesson = await _fixture.StartCustom();

        await _fixture.Service.AnswerAsync(_fixture.UserId, lesson.Id!, 0, 0, null);
        await _fixture.Service.AnswerAsync(_fixture.UserId, lesson.Id!, 1, 2, null);

        Assert.Equal(LessonVerdict.Correct, lesson.Questions[0].Verdict);
        Assert.Equal(LessonVerdict.Wrong, lesson.Questions[1].Verdict);
        Assert.Equal("Because 1.", lesson.Questions[1].Feedback);
        Assert.Equal(1, lesson.Score);
        Assert.Equal(0, _fixture.Author.GradeCalls);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(-1)]
    [InlineData(4)]
    public async Task MultipleChoice_RejectsOptionsThatDoNotExist(int? option)
    {
        Lesson lesson = await _fixture.StartCustom();

        await Assert.ThrowsAsync<LearningException>(() => _fixture.Service.AnswerAsync(_fixture.UserId, lesson.Id!, 0, option, null));

        Assert.False(lesson.Questions[0].IsAnswered);
    }

    [Fact]
    public async Task Typed_ModelAnswerIsCorrectWithoutTheLLM_OthersAreGraded()
    {
        Lesson lesson = await _fixture.StartCustom(LessonMode.Typed);

        await _fixture.Service.AnswerAsync(_fixture.UserId, lesson.Id!, 0, null, "  ANSWER 0 ");
        await _fixture.Service.AnswerAsync(_fixture.UserId, lesson.Id!, 1, null, "something else");

        Assert.Equal(LessonVerdict.Correct, lesson.Questions[0].Verdict);
        Assert.Equal(LessonVerdict.Partial, lesson.Questions[1].Verdict);
        Assert.Equal("Close.", lesson.Questions[1].Feedback);
        Assert.Equal("something else", lesson.Questions[1].AnswerText);
        Assert.Equal(1.5, lesson.Score);
        Assert.Equal(1, _fixture.Author.GradeCalls);
    }

    [Fact]
    public async Task Typed_WhenGradingFails_TheQuestionStaysOpenForARetry()
    {
        Lesson lesson = await _fixture.StartCustom(LessonMode.Typed);
        _fixture.Author.GradeFailure = new HttpRequestException("timeout");

        await Assert.ThrowsAsync<LearningException>(() => _fixture.Service.AnswerAsync(_fixture.UserId, lesson.Id!, 0, null, "try"));
        Assert.False(lesson.Questions[0].IsAnswered);

        _fixture.Author.GradeFailure = null;
        await _fixture.Service.AnswerAsync(_fixture.UserId, lesson.Id!, 0, null, "try");
        Assert.True(lesson.Questions[0].IsAnswered);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Typed_RequiresAnAnswer(string text)
    {
        Lesson lesson = await _fixture.StartCustom(LessonMode.Typed);

        await Assert.ThrowsAsync<LearningException>(() => _fixture.Service.AnswerAsync(_fixture.UserId, lesson.Id!, 0, null, text));
    }

    [Fact]
    public async Task Answer_KeepsTheFirstAnswer()
    {
        Lesson lesson = await _fixture.StartCustom();

        await _fixture.Service.AnswerAsync(_fixture.UserId, lesson.Id!, 0, 0, null);
        await _fixture.Service.AnswerAsync(_fixture.UserId, lesson.Id!, 0, 1, null);

        Assert.Equal(0, lesson.Questions[0].ChosenOption);
    }

    [Fact]
    public async Task Answer_RefusesOtherUsersAndFinishedLessons()
    {
        Lesson lesson = await _fixture.StartCustom();

        await Assert.ThrowsAsync<LearningException>(() => _fixture.Service.AnswerAsync("someone-else", lesson.Id!, 0, 0, null));

        await _fixture.Service.AnswerAsync(_fixture.UserId, lesson.Id!, 0, 0, null);
        await _fixture.Service.FinishAsync(_fixture.UserId, lesson.Id!);
        await Assert.ThrowsAsync<LearningException>(() => _fixture.Service.AnswerAsync(_fixture.UserId, lesson.Id!, 1, 0, null));
    }

    [Fact]
    public async Task Finish_WithoutAnswers_LeavesNoTrace()
    {
        Lesson lesson = await _fixture.StartCustom();

        await _fixture.Service.FinishAsync(_fixture.UserId, lesson.Id!);

        Assert.Equal(LessonStatus.Abandoned, lesson.Status);
        Assert.Empty(_fixture.Profiles.Items);
    }

    [Fact]
    public async Task Finish_UpdatesTheDiagnosis()
    {
        DiagnosisIssue issue = await SeedIssue();
        Lesson lesson = await _fixture.Service.StartAsync(_fixture.UserId, new LessonRequest(LessonMode.MultipleChoice, "", [], 2));
        int count = issue.Count;

        await _fixture.Service.AnswerAsync(_fixture.UserId, lesson.Id!, 0, 0, null);
        await _fixture.Service.AnswerAsync(_fixture.UserId, lesson.Id!, 1, 1, null);
        Assert.Equal(count, issue.Count);

        await _fixture.Service.FinishAsync(_fixture.UserId, lesson.Id!);

        Assert.Equal(1, issue.TotalCorrect);
        Assert.Equal(count + 1, issue.Count);
    }

    [Fact]
    public async Task Finish_ReviewsVocabularyAndLogsTheStudyDay()
    {
        DateTime now = DateTime.UtcNow;
        LifeProfile profile = await _fixture.Profiles.GetOrCreate(_fixture.UserId);
        profile.Study.AddVocab("微妙", "びみょう", "subtle", now.AddDays(-5), out VocabItem word);
        word.Seen = 2;
        word.NextReviewAt = now.AddDays(-1);

        Lesson lesson = await _fixture.Service.StartAsync(_fixture.UserId, new LessonRequest(LessonMode.MultipleChoice, "", [], 1));
        await _fixture.Service.AnswerAsync(_fixture.UserId, lesson.Id!, 0, 0, null);
        await _fixture.Service.FinishAsync(_fixture.UserId, lesson.Id!);

        Assert.Equal(2, word.Box);
        StudyDay day = profile.Study.Days.Single();
        Assert.Equal(1, day.Reviews);
        Assert.True(day.Minutes >= 1);
        StudySummary summary = StudySummary.Create(profile, DateTime.UtcNow);
        Assert.StartsWith("微妙 (1/1)", summary.LastLesson);
        Assert.Contains("Last lesson with their tutor: 微妙", summary.Describe());
    }

    [Fact]
    public async Task Finish_AddsClosingWords_ButSurvivesWithoutThem()
    {
        Lesson first = await _fixture.StartCustom();
        await _fixture.Service.AnswerAsync(_fixture.UserId, first.Id!, 0, 0, null);
        await _fixture.Service.FinishAsync(_fixture.UserId, first.Id!);
        Assert.Equal("Well done.", first.Closing);

        _fixture.Author.CloseFailure = new HttpRequestException("down");
        Lesson second = await _fixture.StartCustom();
        await _fixture.Service.AnswerAsync(_fixture.UserId, second.Id!, 0, 0, null);
        await _fixture.Service.FinishAsync(_fixture.UserId, second.Id!);

        Assert.Equal(LessonStatus.Finished, second.Status);
        Assert.Equal("", second.Closing);
    }

    [Fact]
    public async Task Finish_Twice_RecordsOnce()
    {
        Lesson lesson = await _fixture.StartCustom();
        await _fixture.Service.AnswerAsync(_fixture.UserId, lesson.Id!, 0, 0, null);

        await _fixture.Service.FinishAsync(_fixture.UserId, lesson.Id!);
        await _fixture.Service.FinishAsync(_fixture.UserId, lesson.Id!);

        Assert.Equal(1, _fixture.Profiles.Items.Single().Study.Days.Single().Reviews);
    }

    [Fact]
    public async Task State_OffersTheRunningLessonAndRecentResults()
    {
        Lesson finished = await _fixture.StartCustom();
        await _fixture.Service.AnswerAsync(_fixture.UserId, finished.Id!, 0, 0, null);
        await _fixture.Service.FinishAsync(_fixture.UserId, finished.Id!);
        Lesson running = await _fixture.StartCustom();

        LearningState state = await _fixture.Service.GetStateAsync(_fixture.UserId);

        Assert.Equal(running.Id, state.Current!.Id);
        Assert.Equal(finished.Id, Assert.Single(state.Recent).Id);
        Assert.Equal("Mio", state.Tutor.Name);
    }

    [Fact]
    public async Task MissedConcepts_BecomeWeakSpots_EvenOnCustomTopics()
    {
        Lesson lesson = await _fixture.StartCustom(count: 1);
        await _fixture.Service.AnswerAsync(_fixture.UserId, lesson.Id!, 0, 1, null);
        await _fixture.Service.FinishAsync(_fixture.UserId, lesson.Id!);

        LearningState state = await _fixture.Service.GetStateAsync(_fixture.UserId);

        Struggle struggle = Assert.Single(state.Struggles);
        Assert.Equal(StruggleKind.Lesson, struggle.Kind);
        Assert.Equal("concept 0", struggle.Title);
        Assert.Contains("Rust lifetimes", struggle.Detail);
    }
}
