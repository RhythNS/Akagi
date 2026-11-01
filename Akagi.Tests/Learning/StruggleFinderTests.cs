using Akagi.Bridge.Chat.Models;
using Akagi.Learning;
using Akagi.Life;
using Akagi.Life.Japanese;
using Akagi.Life.Study;

namespace Akagi.Tests.Learning;

public class StruggleFinderTests
{
    private static readonly DateTime Now = new(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);

    private static Lesson Finished(DateTime created, params (string Concept, LessonVerdict Verdict, string FocusId)[] questions) => new()
    {
        Status = LessonStatus.Finished,
        Topic = "Topic",
        CreatedAt = created,
        Questions = [.. questions.Select(q => new LessonQuestion { Concept = q.Concept, Verdict = q.Verdict, FocusId = q.FocusId, AnsweredAt = created })]
    };

    [Fact]
    public void Find_MergesEverySourceHeaviestFirst()
    {
        JapaneseDiagnosis diagnosis = new();
        for (int i = 0; i < 5; i++)
        {
            diagnosis.RecordIssue(IssueCategory.Particle, "に vs で", "Location.", null, 3, Now);
        }
        LifeProfile profile = new();
        profile.Study.RecordMistake("ている", "Progressive.", "", "", Now.AddDays(-30));
        profile.Study.AddVocab("微妙", "びみょう", "subtle", Now, out VocabItem word);
        word.Seen = 1;
        word.NextReviewAt = Now.AddDays(-1);

        List<Struggle> struggles = StruggleFinder.Find(diagnosis, profile, [Finished(Now, ("async", LessonVerdict.Wrong, ""))], Now);

        Assert.Equal(4, struggles.Count);
        Assert.Equal("に vs で", struggles[0].Title);
        Assert.Contains(struggles, s => s.Kind == StruggleKind.Grammar && s.Title == "ている");
        Assert.Contains(struggles, s => s.Kind == StruggleKind.Vocabulary && s.Detail == "びみょう - subtle");
        Assert.Contains(struggles, s => s.Kind == StruggleKind.Lesson && s.Title == "async");
    }

    [Fact]
    public void Vocabulary_CountsOnlyWhenItIsShakyAndDue()
    {
        LifeProfile profile = new();
        profile.Study.AddVocab("unseen", "", "", Now, out VocabItem unseen);
        unseen.NextReviewAt = Now.AddDays(-1);
        profile.Study.AddVocab("known", "", "", Now, out VocabItem known);
        known.Seen = 9;
        known.Box = 4;
        known.NextReviewAt = Now.AddDays(-1);
        profile.Study.AddVocab("later", "", "", Now, out VocabItem later);
        later.Seen = 1;
        later.NextReviewAt = Now.AddDays(1);

        Assert.Empty(StruggleFinder.Find(null, profile, [], Now));
    }

    [Fact]
    public void Lessons_AConceptStopsBeingWeakOnceAnsweredCorrectly()
    {
        Lesson missed = Finished(Now.AddDays(-2), ("borrowing", LessonVerdict.Wrong, ""), ("traits", LessonVerdict.Partial, ""));
        Lesson fixedIt = Finished(Now.AddDays(-1), ("Borrowing ", LessonVerdict.Correct, ""));

        Struggle struggle = Assert.Single(StruggleFinder.Find(null, new LifeProfile(), [fixedIt, missed], Now));

        Assert.Equal("traits", struggle.Title);
    }

    [Fact]
    public void Lessons_ARepeatedMissIsCountedAndAFollowUpLessonAnswersIt()
    {
        Lesson first = Finished(Now.AddDays(-3), ("traits", LessonVerdict.Wrong, ""));
        Lesson second = Finished(Now.AddDays(-2), ("traits", LessonVerdict.Wrong, ""));

        Struggle struggle = Assert.Single(StruggleFinder.Find(null, new LifeProfile(), [second, first], Now));
        Assert.Contains("Missed 2x", struggle.Detail);

        // Practice on the weak spot may phrase the concept differently, the focus id ties them together
        Lesson practice = Finished(Now.AddDays(-1), ("Trait bounds in generics", LessonVerdict.Correct, struggle.Id));
        Assert.Empty(StruggleFinder.Find(null, new LifeProfile(), [practice, second, first], Now));
    }

    [Fact]
    public void Lessons_QuestionsOnDiagnosedIssuesAreTrackedThereInstead()
    {
        Lesson lesson = Finished(Now, ("particles", LessonVerdict.Wrong, Struggle.JapanesePrefix + "abc123"));

        Assert.Empty(StruggleFinder.Find(null, new LifeProfile(), [lesson], Now));
    }

    [Fact]
    public void Lessons_UnfinishedOnesDoNotCount()
    {
        Lesson lesson = Finished(Now, ("async", LessonVerdict.Wrong, ""));
        lesson.Status = LessonStatus.Active;

        Assert.Empty(StruggleFinder.Find(null, new LifeProfile(), [lesson], Now));
    }

    [Fact]
    public void Select_ChosenOnesOrTheHeaviest()
    {
        List<Struggle> struggles =
        [
            new("a", StruggleKind.Lesson, "A", "", 3),
            new("b", StruggleKind.Lesson, "B", "", 2),
            new("c", StruggleKind.Lesson, "C", "", 1)
        ];

        Assert.Equal(["a", "b"], StruggleFinder.Select(struggles, [], 2).Select(s => s.Id));
        Assert.Equal(["c"], StruggleFinder.Select(struggles, ["C"], 2).Select(s => s.Id));
        Assert.Empty(StruggleFinder.Select(struggles, ["gone"], 2));
    }
}
