using Akagi.Bridge.Chat.Models;
using Akagi.Web.Pages.Learning.Components;
using System.Globalization;
using System.Text;

namespace Akagi.Web.Pages.Learning.Context;

/// <summary>
/// Writes what the Learn pages show as plain text another language model can read: Markdown headings, lists and tables,
/// first person for the learner.
/// </summary>
public static class ContextWriter
{
    /// <summary>
    /// The note a new context starts with, so the other model knows what it is reading.
    /// </summary>
    public const string IntroTitle = "About me and this file";

    public const string IntroText =
        "I am learning Japanese. Everything below was exported from Akagi, my own learning tool. It records my voice calls " +
        "with Japanese speakers, transcribes them, and has a tutor model review them; it also runs lessons with me.\n\n" +
        "\"I\" and \"me\" always mean me, the learner. \"Partner\" is the person I talked to. Quotes in Japanese are what " +
        "the transcription heard, so they can contain transcription errors. Severity goes from \"polish\" (minor) over " +
        "\"noticeable\" to \"serious\" (gets in the way of being understood or sounds rude).";

    /// <summary>
    /// The whole export: every included block under its own heading.
    /// </summary>
    public static string Compose(IEnumerable<(string Title, string Text)> blocks, DateTime exportedAt)
    {
        StringBuilder text = new();
        text.Append("# Context from Akagi, exported ").Append(Day(exportedAt)).Append('\n');
        foreach ((string title, string body) in blocks)
        {
            text.Append("\n## ").Append(title.Trim()).Append("\n\n");
            text.Append(body.Trim()).Append('\n');
        }
        return text.ToString();
    }

    /// <summary>
    /// A rough token count: about four characters per token in English, about one per character in Japanese.
    /// </summary>
    public static int EstimateTokens(string text)
    {
        int ascii = 0;
        int other = 0;
        foreach (char c in text)
        {
            if (c < 128)
            {
                ascii++;
            }
            else
            {
                other++;
            }
        }
        return (int)Math.Ceiling(ascii / 4.0 + other);
    }

    public static string CallTitle(CallAnalysisSummary call) =>
        $"{CallText.Name(call)} on {CallDay(call)}";

    public static string CallDay(CallAnalysisSummary call) => Day(CallDate(call));

    /// <param name="calls">The loaded call with this id, for transcript lines around examples; null when it is not loaded.</param>
    public static string Insights(CallInsights insights, ContextBlock block, Func<string, CallAnalysisDetail?>? calls = null)
    {
        Text text = new();
        if (block.Has("overview"))
        {
            text.Heading("Overview");
            double total = insights.Calls.Sum(c => c.DurationSeconds);
            text.Line($"Calls analyzed: {insights.Calls.Length} ({CallText.Duration(total)} in all)");
            text.Line($"Different words I used: {insights.LearnerDistinct} ({insights.LearnerWords} words in all)");
            List<CallTrendPoint> measured = [.. insights.Calls.Where(c => c.LearnerShare is not null && c.DurationSeconds > 0)];
            double measuredSeconds = measured.Sum(c => c.DurationSeconds);
            if (measuredSeconds > 0)
            {
                double share = measured.Sum(c => c.LearnerShare!.Value * c.DurationSeconds) / measuredSeconds;
                text.Line($"My share of the speaking time: {CallText.Percent(share)} (weighted by call length)");
            }
            text.Line($"Words my partners used often that I never said: {insights.HeardNotSaid.Length}");
            if (insights.MixedCalls > 0)
            {
                text.Line($"{insights.MixedCalls} call(s) were recorded without separate voices; they have no speaking share and are left out of the vocabulary.");
            }
        }

        if (block.Has("calls") && insights.Calls.Length > 0)
        {
            text.Heading("Per call, oldest first");
            text.Table(
                ["Date", "Call", "Length", "My share", "Different words", "New words", "Fillers per 100 chars", "Median answer delay", "Questions I asked", "Mistakes"],
                insights.Calls.Select(c => new[]
                {
                    Day(c.Date),
                    c.Name,
                    CallText.Duration(c.DurationSeconds),
                    c.LearnerShare is double share ? CallText.Percent(share) : "-",
                    c.Separated ? c.LearnerDistinct.ToString(CultureInfo.InvariantCulture) : "-",
                    c.Separated ? c.NewWords.ToString(CultureInfo.InvariantCulture) : "-",
                    c.FillersPer100 is double fillers ? CallText.Number(fillers) : "-",
                    c.MedianResponseSeconds is double seconds ? CallText.Seconds(seconds) : "-",
                    c.Separated ? c.LearnerQuestions.ToString(CultureInfo.InvariantCulture) : "-",
                    c.IssueCount.ToString(CultureInfo.InvariantCulture)
                }));
        }

        List<InsightStruggle> struggles = [.. insights.Struggles.Where(s => block.IsPicked(ContextItems.Issue(s.IssueId, s.Pattern)))];
        if (block.Has("struggles") && struggles.Count > 0)
        {
            text.Heading("Struggles across calls, most serious and frequent first");
            int number = 0;
            foreach (InsightStruggle struggle in struggles)
            {
                number++;
                List<string> facts = [$"{struggle.Times}x in {struggle.Calls} call(s)"];
                if (struggle.CorrectUses > 0)
                {
                    facts.Add($"used correctly {struggle.CorrectUses}x");
                }
                facts.Add($"last {Day(struggle.LastSeen)}");
                if (!string.IsNullOrWhiteSpace(struggle.Status))
                {
                    facts.Add($"status {struggle.Status}");
                }
                text.Line($"{number}. [{struggle.Category}, {Severity(struggle.Severity)}] {struggle.Pattern} ({string.Join(", ", facts)})");
                List<InsightOccurrence> examples = [.. ContextItems.Examples(struggle)
                    .Where(o => block.IsPicked(ContextItems.Finding(o.Finding), ContextItems.ExampleDefault(block, struggle, o)))];
                bool single = examples.Count == 1 && !block.Has("occurrences");
                foreach (InsightOccurrence occurrence in examples)
                {
                    text.Line(single || occurrence.CallId.Length == 0
                        ? $"   Example: {Mistake(occurrence.Finding)}"
                        : $"   - {Day(occurrence.Date)}, {occurrence.CallName}: {Mistake(occurrence.Finding)}");
                    if (calls?.Invoke(occurrence.CallId) is CallAnalysisDetail call)
                    {
                        Quote(text, call.Transcript, occurrence.Finding.Time, occurrence.Finding.Said,
                            block.AroundOf(ContextItems.Finding(occurrence.Finding)), PartnerOf(call.Summary), "     ");
                    }
                }
            }
        }

        if (block.Has("words"))
        {
            Words(text, "Words I use most (nouns, verbs, adjectives, adverbs)", insights.Words, block.Limit, true);
        }
        if (block.Has("heard"))
        {
            Words(text, "Words my partners used often that I never said (understood, not yet my own)", insights.HeardNotSaid, block.Limit, false);
        }

        if (block.Has("phrases") && insights.Phrases.Length > 0)
        {
            text.Heading("Phrase book: natural expressions my partners used");
            foreach (InsightPhrase phrase in Take(insights.Phrases, block.Limit))
            {
                text.Line($"- {phrase.Phrase}{Suffix(" - ", phrase.Meaning)} ({phrase.CallName}, {Day(phrase.Date)})");
            }
            text.More(insights.Phrases.Length, block.Limit);
        }
        return text.ToString();
    }

    public static string Lessons(LearningOverview overview, ContextBlock block)
    {
        Text text = new();
        List<LearningStruggle> weak = [.. overview.Struggles.Where(s => block.IsPicked(ContextItems.Weak(s)))];
        if (block.Has("weak") && (weak.Count > 0 || overview.Struggles.Length == 0))
        {
            text.Heading("Weak spots my tutor works on");
            if (weak.Count == 0)
            {
                text.Line("None recorded.");
            }
            foreach (LearningStruggle struggle in weak)
            {
                text.Line($"- [{struggle.Kind}] {struggle.Title}{Suffix(": ", struggle.Detail)}");
            }
        }

        List<LearningLesson> lessons = [.. overview.Recent.Where(l => block.IsPicked(ContextItems.Lesson(l)))];
        if (block.Has("lessons") && (lessons.Count > 0 || overview.Recent.Length == 0))
        {
            text.Heading(lessons.Count < overview.Recent.Length ? "Some of my recent lessons" : "Recent lessons");
            if (lessons.Count == 0)
            {
                text.Line("No lessons yet.");
            }
            foreach (LearningLesson lesson in lessons)
            {
                string mode = lesson.Mode == LessonMode.Typed ? "typed answers" : "multiple choice";
                text.Line($"#### {Day(lesson.CreatedAt)}: {lesson.Topic}");
                text.Line($"{mode}, score {lesson.Score.ToString("0.#", CultureInfo.InvariantCulture)} of {lesson.AnsweredCount} answered ({lesson.Questions.Length} questions), {lesson.Status.ToString().ToLowerInvariant()}");
                if (!block.Has("questions"))
                {
                    text.Blank();
                    continue;
                }
                int number = 0;
                foreach (LearningQuestion question in lesson.Questions)
                {
                    number++;
                    text.Line($"{number}. {Suffix("(", question.Concept, ") ")}{question.Prompt}");
                    if (question.Options.Length > 0)
                    {
                        text.Line("   Options: " + string.Join("; ", question.Options.Select((o, i) => $"{(char)('a' + i)}) {o}")));
                    }
                    if (!question.IsAnswered)
                    {
                        text.Line("   Not answered.");
                        continue;
                    }
                    string answer = question.ChosenOption is int chosen && chosen < question.Options.Length
                        ? $"{(char)('a' + chosen)}) {question.Options[chosen]}"
                        : question.AnswerText;
                    text.Line($"   My answer: {answer} ({Verdict(question.Verdict)})");
                    if (question.CorrectOption is int correct && correct < question.Options.Length && question.Verdict != LessonVerdict.Correct)
                    {
                        text.Line($"   Right answer: {(char)('a' + correct)}) {question.Options[correct]}");
                    }
                    else if (!string.IsNullOrWhiteSpace(question.ExpectedAnswer) && question.Verdict != LessonVerdict.Correct)
                    {
                        text.Line($"   Expected: {question.ExpectedAnswer}");
                    }
                    if (!string.IsNullOrWhiteSpace(question.Feedback))
                    {
                        text.Line($"   Tutor: {question.Feedback}");
                    }
                }
                if (!string.IsNullOrWhiteSpace(lesson.Closing))
                {
                    text.Line($"Tutor's closing words: {lesson.Closing}");
                }
                text.Blank();
            }
        }
        return text.ToString();
    }

    public static string Call(CallAnalysisDetail detail, ContextBlock block)
    {
        CallAnalysisSummary call = detail.Summary;
        string partner = PartnerOf(call);
        Text text = new();

        if (block.Has("overview"))
        {
            text.Heading("Overview");
            text.Line(call.DateKnown ? $"Date: {Day(CallDate(call))}" : $"Date: unknown (uploaded {Day(call.CreatedAt)})");
            if (!string.IsNullOrWhiteSpace(call.Partner))
            {
                text.Line($"Partner: {call.Partner}");
            }
            if (!string.IsNullOrWhiteSpace(call.Title))
            {
                text.Line($"Title: {call.Title}");
            }
            if (call.DurationSeconds > 0)
            {
                text.Line($"Length: {CallText.Duration(call.DurationSeconds)}");
            }
            if (!string.IsNullOrWhiteSpace(detail.Register))
            {
                text.Line($"Politeness the situation called for: {detail.Register}");
            }
            if (detail.Flow?.LearnerShare is double share)
            {
                text.Line($"My share of the speaking time: {CallText.Percent(share)}");
            }
            if (detail.Vocabulary is { Separated: true } vocabulary)
            {
                text.Line($"Different words I used: {vocabulary.LearnerDistinct}");
            }
            text.Line($"Mistakes noted: {call.IssueCount}");
            if (call.State != CallAnalysisState.Completed)
            {
                text.Line($"Analysis state: {CallText.StateLabel(call.State)}{Suffix(" - ", call.Error)}");
            }
            if (detail.Legacy)
            {
                text.Line("This call was analyzed before findings were kept with each call, so some of its findings may be missing.");
            }
        }

        if (block.Has("review") && (!string.IsNullOrWhiteSpace(detail.ReviewSummary) || detail.Priorities.Length > 0))
        {
            text.Heading("Tutor's review");
            if (!string.IsNullOrWhiteSpace(detail.ReviewSummary))
            {
                text.Line(detail.ReviewSummary.Trim());
            }
            if (detail.Priorities.Length > 0)
            {
                text.Line("Work on next:");
                text.Numbered(detail.Priorities);
            }
        }

        if (block.Has("struggles"))
        {
            List<CallStruggle> struggles = [.. detail.Struggles.Where(s => s.TimesInCall > 0 && block.IsPicked(ContextItems.Issue(s.IssueId, s.Pattern)))];
            if (struggles.Count > 0)
            {
                text.Heading("Mistakes in this call");
                foreach (CallStruggle struggle in struggles)
                {
                    List<string> facts = [$"{struggle.TimesInCall}x in this call"];
                    if (struggle.TimesOverall is int overall)
                    {
                        facts.Add($"{overall}x overall");
                    }
                    if (!string.IsNullOrWhiteSpace(struggle.Status))
                    {
                        facts.Add($"status {struggle.Status}");
                    }
                    text.Line($"- [{struggle.Category}, {Severity(struggle.Severity)}] {struggle.Pattern} ({string.Join(", ", facts)})");
                    if (!string.IsNullOrWhiteSpace(struggle.Explanation))
                    {
                        text.Line($"  {struggle.Explanation.Trim()}");
                    }
                    foreach (CallFindingView finding in Picked(Counted(struggle.Findings), block))
                    {
                        text.Line($"  - {Mistake(finding)}");
                        Quote(text, detail, finding, block, "    ");
                    }
                }
            }
        }

        if (block.Has("correct"))
        {
            Findings(text, "Things I got right that I used to get wrong", Counted(detail.CorrectUses), f =>
                $"{Time(f)}{f.Said}{Suffix(" (right on: ", f.IssuePattern, ")")}{Marks(f)}", block, detail);
        }
        if (block.Has("strengths"))
        {
            Findings(text, "What went well", Counted(detail.Strengths), f =>
                $"{Time(f)}{f.Pattern}{Suffix(": ", f.Said)}{Suffix(" - ", f.Explanation)}{Marks(f)}", block, detail);
        }
        if (block.Has("phrases"))
        {
            Findings(text, $"Phrases worth adopting that {partner} used", Counted(detail.Phrases), f =>
                $"{Time(f)}{f.Said}{Suffix(" - ", f.Better)}{Suffix(" (", f.Explanation, ")")}{Marks(f)}", block, detail);
        }

        if (block.Has("coaching") && (detail.Conversation.Length > 0 || !string.IsNullOrWhiteSpace(detail.CoachingSummary) || detail.CoachingTips.Length > 0))
        {
            text.Heading("Conversation coaching");
            if (!string.IsNullOrWhiteSpace(detail.CoachingSummary))
            {
                text.Line(detail.CoachingSummary.Trim());
            }
            if (detail.CoachingTips.Length > 0)
            {
                text.Line("Tips:");
                foreach (string tip in detail.CoachingTips)
                {
                    text.Line($"- {tip}");
                }
            }
            foreach ((string title, bool wentWell) in new[] { ("Could be better", false), ("Went well", true) })
            {
                List<CallFindingView> moments = [.. Picked(Counted(detail.Conversation), block).Where(c => c.WentWell == wentWell)];
                if (moments.Count == 0)
                {
                    continue;
                }
                text.Line($"{title}:");
                foreach (CallFindingView moment in moments)
                {
                    string scope = moment.Japanese ? "Japanese" : "general";
                    text.Line($"- {Time(moment)}[{Join(", ", moment.Aspect, scope)}]{Marks(moment)}");
                    if (!string.IsNullOrWhiteSpace(moment.Context))
                    {
                        text.Line($"  {partner}: {moment.Context}");
                    }
                    text.Line($"  Me: {moment.Said}");
                    if (!string.IsNullOrWhiteSpace(moment.Better))
                    {
                        text.Line($"  Try: {moment.Better}");
                    }
                    if (!string.IsNullOrWhiteSpace(moment.Explanation))
                    {
                        text.Line($"  {moment.Explanation.Trim()}");
                    }
                    Quote(text, detail, moment, block, "  ");
                }
            }
        }

        if (block.Has("facts"))
        {
            Findings(text, $"What I learned about {partner}", Counted(detail.PartnerFacts).OrderByDescending(f => f.FollowUp), Fact, block, detail);
            Findings(text, "What came up about me", Counted(detail.LearnerFacts), Fact, block, detail);
        }

        if (block.Has("speech") && detail.Speech is CallSpeech speech)
        {
            text.Heading(speech.LearnerOnly ? "Speech measurements (my speech only)" : "Speech measurements (all speakers, rough)");
            text.Line($"{speech.JapaneseCharacters} Japanese characters in {speech.Sentences} sentences, {CallText.Number(speech.FillersPer100)} fillers per 100 characters.");
            if (speech.Fillers.Length > 0)
            {
                text.Line("Fillers and hedges: " + string.Join(", ", speech.Fillers.Select(f => $"{f.Form} {f.Count}x{(f.IsHabit ? " (habit)" : "")}")));
            }
            text.Line($"Polite endings: {CallText.Percent(speech.PoliteRatio)}; keigo forms: {speech.Keigo}; expected politeness: {speech.ExpectedRegister}.");
            if (!string.IsNullOrWhiteSpace(speech.RegisterMismatch))
            {
                text.Line($"Politeness mismatch: {speech.RegisterMismatch}");
            }
            if (speech.RecurringStructures.Length > 0)
            {
                text.Line("Recurring sentence frames: " + string.Join("; ", speech.RecurringStructures));
            }
            if (speech.Grammar.Length > 0)
            {
                text.Line("N2 grammar I used: " + string.Join("; ", speech.Grammar.Select(g => $"{g.Pattern} {g.Count}x{Suffix(" (", g.Meaning, ")")}")));
            }
        }

        if (block.Has("flow") && detail.Flow is { Separated: true } flow)
        {
            text.Heading("Conversation flow");
            if (flow.HasPartner)
            {
                text.Line($"Speaking time: me {CallText.Duration(flow.LearnerSeconds)}, {partner} {CallText.Duration(flow.PartnerSeconds)}");
                text.Line($"Turns: me {flow.LearnerTurns}, {partner} {flow.PartnerTurns}; characters per turn: me {CallText.Number(flow.LearnerCharactersPerTurn)}, {partner} {CallText.Number(flow.PartnerCharactersPerTurn)}");
                text.Line($"Questions asked: me {flow.LearnerQuestions}, {partner} {flow.PartnerQuestions}");
            }
            else
            {
                text.Line($"My speaking time: {CallText.Duration(flow.LearnerSeconds)} in {flow.LearnerTurns} turns, {CallText.Number(flow.LearnerCharactersPerTurn)} characters per turn");
            }
            text.Line($"Longest turn of mine: {CallText.Seconds(flow.LongestLearnerTurnSeconds)}");
            if (flow.MedianResponseSeconds is double response)
            {
                text.Line($"Median time until I answered: {CallText.Seconds(response)}");
            }
            text.Line($"Times I talked over {partner}: {flow.LearnerOverlaps}; long pauses: {flow.LongPauses}");
        }

        if (block.Has("vocabulary") && detail.Vocabulary is { Separated: true } words && words.Words.Length > 0)
        {
            text.Heading("Vocabulary");
            text.Line($"I said {words.LearnerWords} words ({words.LearnerDistinct} different), {partner} {words.PartnerWords} ({words.PartnerDistinct} different).");
            List<CallWordView> mine = [.. words.Words.Where(w => w.Learner > 0).OrderByDescending(w => w.Learner)];
            text.Line(Limited("Words I used most", mine.Count, block.Limit) + ":");
            foreach (CallWordView word in Take(mine, block.Limit))
            {
                text.Line($"- {Word(word.Lemma, word.Reading, word.Class)}: me {word.Learner}x, {partner} {word.Partner}x{(word.NewForLearner ? ", first time I used it" : "")}");
            }
        }

        List<CallFindingView> filtered = [.. Picked(detail.FilteredOut, block)];
        if (block.Has("filtered") && filtered.Count > 0)
        {
            text.Heading("Findings that were filtered out (not counted)");
            foreach (CallFindingView finding in filtered)
            {
                text.Line($"- [{finding.State}{Suffix(": ", finding.Note)}] {Mistake(finding)}");
                Quote(text, detail, finding, block, "  ");
            }
        }

        if (block.Has("report") && !string.IsNullOrWhiteSpace(detail.Report))
        {
            text.Heading("Report sent to my companion");
            text.Line(detail.Report.Trim());
        }

        if (block.Has("transcript") && detail.Transcript.Length > 0)
        {
            text.Heading("Transcript");
            foreach (CallTranscriptLine line in detail.Transcript)
            {
                text.Line(TranscriptLine(line, partner));
            }
        }
        return text.ToString();
    }

    /// <param name="calls">The loaded call with this id, for transcript lines around facts; null when it is not loaded.</param>
    public static string Person(PersonDetail person, ContextBlock block, Func<string, CallAnalysisDetail?>? calls = null)
    {
        PersonSummary summary = person.Summary;
        PersonFactView[] facts = [.. person.Facts.Where(f => block.IsPicked(ContextItems.Fact(f)))];
        Text text = new();
        if (block.Has("summary"))
        {
            text.Heading("Summary");
            text.Line($"{summary.Calls} call(s) with {summary.Name}, {CallText.Duration(summary.TotalSeconds)} together.");
            if (summary.FirstTalked is DateTime first)
            {
                text.Line($"First talked: {Day(first)}");
            }
            if (summary.LastTalked is DateTime last)
            {
                text.Line($"Last talked: {Day(last)}");
            }
        }

        List<PersonFactView> open = [.. facts.Where(f => f.FollowUp)];
        if (block.Has("threads") && open.Count > 0)
        {
            text.Heading("Worth asking about next time");
            foreach (PersonFactView fact in open)
            {
                text.Line($"- {fact.Text} ({Day(fact.Date)})");
            }
        }

        if (block.Has("facts") && facts.Length > 0)
        {
            text.Heading(facts.Length < person.Facts.Length ? $"What I know about {summary.Name}, newest first" : $"Everything I know about {summary.Name}, newest first");
            foreach (IGrouping<string, PersonFactView> topic in facts.GroupBy(f => string.IsNullOrWhiteSpace(f.Topic) ? "other" : f.Topic))
            {
                text.Line($"{topic.Key}:");
                foreach (PersonFactView fact in topic)
                {
                    text.Line($"- {fact.Text}{Suffix(" (\"", fact.Quote, "\")")} [{Day(fact.Date)}]");
                    if (calls?.Invoke(fact.CallId) is CallAnalysisDetail call)
                    {
                        Quote(text, call.Transcript, fact.Time, fact.Quote, block.AroundOf(ContextItems.Fact(fact)), PartnerOf(call.Summary), "  ");
                    }
                }
            }
        }

        if (block.Has("calls") && person.Calls.Length > 0)
        {
            text.Heading("Calls");
            foreach (PersonCallView call in person.Calls)
            {
                string share = call.LearnerShare is double value ? $", I spoke {CallText.Percent(value)}" : string.Empty;
                text.Line($"- {Day(call.Date)}: {CallText.Duration(call.DurationSeconds)}, {call.IssueCount} mistakes, {call.Facts} facts{share}");
            }
        }

        if (block.Has("phrases") && person.Phrases.Length > 0)
        {
            text.Heading($"Phrases {summary.Name} used");
            foreach (InsightPhrase phrase in person.Phrases)
            {
                text.Line($"- {phrase.Phrase}{Suffix(" - ", phrase.Meaning)} ({Day(phrase.Date)})");
            }
        }
        return text.ToString();
    }

    public static string Vocabulary(VocabReportInfo? info, VocabReportResult result, ContextBlock block)
    {
        Text text = new();
        if (!string.IsNullOrWhiteSpace(info?.Description))
        {
            text.Line(info.Description.Trim());
        }
        text.Line(string.IsNullOrWhiteSpace(block.Partner)
            ? "Covers all my calls with separate voices; content words only (nouns, verbs, adjectives, adverbs), names left out."
            : $"Covers only my calls with {block.Partner}; content words only (nouns, verbs, adjectives, adverbs), names left out.");
        if (!string.IsNullOrWhiteSpace(result.Note))
        {
            text.Line(result.Note.Trim());
        }

        if (block.Has("stats") && result.Stats.Length > 0)
        {
            text.Blank();
            foreach (VocabStat stat in result.Stats)
            {
                text.Line($"- {stat.Label}: {stat.Value}{Suffix(" (", stat.Hint, ")")}");
            }
        }

        if (block.Has("charts"))
        {
            foreach (VocabSeries series in result.Series.Where(s => s.Points.Length > 0))
            {
                text.Heading(series.Title);
                if (!string.IsNullOrWhiteSpace(series.Hint))
                {
                    text.Line(series.Hint.Trim());
                }
                text.Table(
                    ["", .. series.Names],
                    series.Points.Select(p => new[] { p.Label }.Concat(series.Names.Select((_, i) => VocabText.Value(p.Values.ElementAtOrDefault(i), series.Unit)))));
            }
        }

        if (block.Has("tables"))
        {
            foreach (VocabTable table in result.Tables)
            {
                text.Heading(Limited(table.Title, table.Rows.Length, block.Limit));
                if (!string.IsNullOrWhiteSpace(table.Description))
                {
                    text.Line(table.Description.Trim());
                }
                if (table.Rows.Length == 0)
                {
                    text.Line("Nothing to show.");
                    continue;
                }
                text.Table(
                    table.Columns.Select(c => c.Label),
                    Take(Sorted(table), block.Limit).Select(r => table.Columns.Select((_, i) => r.Cells.ElementAtOrDefault(i)?.Text ?? string.Empty)));
            }
        }
        return text.ToString();
    }

    /// <summary>
    /// The rows in the order the report chose, as the page first shows them.
    /// </summary>
    private static IEnumerable<VocabRow> Sorted(VocabTable table)
    {
        int column = Math.Clamp(table.SortColumn, 0, Math.Max(0, table.Columns.Length - 1));
        Func<VocabRow, VocabCell?> cell = r => r.Cells.ElementAtOrDefault(column);
        bool byNumber = table.Rows.Any(r => cell(r)?.Sort != null);
        return byNumber
            ? (table.Descending ? table.Rows.OrderByDescending(r => cell(r)?.Sort ?? double.MinValue) : table.Rows.OrderBy(r => cell(r)?.Sort ?? double.MaxValue))
            : (table.Descending ? table.Rows.OrderByDescending(r => cell(r)?.Text, StringComparer.Ordinal) : table.Rows.OrderBy(r => cell(r)?.Text, StringComparer.Ordinal));
    }

    private static void Words(Text text, string title, InsightWord[] all, int limit, bool mine)
    {
        List<InsightWord> words = [.. all.Where(w => CallText.MatchesClass(CallText.ContentFilter, w.Class))];
        if (words.Count == 0)
        {
            return;
        }
        text.Heading(Limited(title, words.Count, limit));
        foreach (InsightWord word in Take(words, limit))
        {
            text.Line(mine
                ? $"- {Word(word.Lemma, word.Reading, word.Class)}: I said it {word.Learner}x in {word.Calls} call(s), partners {word.Partner}x{(word.FirstUsed is DateTime first ? $", first used {Day(first)}" : "")}"
                : $"- {Word(word.Lemma, word.Reading, word.Class)}: partners said it {word.Partner}x");
        }
    }

    private static void Findings(Text text, string title, IEnumerable<CallFindingView> findings, Func<CallFindingView, string> line, ContextBlock block, CallAnalysisDetail detail)
    {
        List<CallFindingView> list = [.. Picked(findings, block)];
        if (list.Count == 0)
        {
            return;
        }
        text.Heading(title);
        foreach (CallFindingView finding in list)
        {
            text.Line($"- {line(finding)}");
            Quote(text, detail, finding, block, "  ");
        }
    }

    private static IEnumerable<CallFindingView> Counted(IEnumerable<CallFindingView> findings) => ContextItems.Counted(findings);

    private static IEnumerable<CallFindingView> Picked(IEnumerable<CallFindingView> findings, ContextBlock block) =>
        findings.Where(f => block.IsPicked(ContextItems.Finding(f)));

    private static void Quote(Text text, CallAnalysisDetail detail, CallFindingView finding, ContextBlock block, string indent) =>
        Quote(text, detail.Transcript, finding.Time, finding.Said, block.AroundOf(ContextItems.Finding(finding)), PartnerOf(detail.Summary), indent);

    /// <summary>
    /// The transcript lines before and after the one an example is about, that line marked.
    /// </summary>
    private static void Quote(Text text, CallTranscriptLine[] transcript, double? time, string said, int around, string partner, string indent)
    {
        if (around <= 0 || time is not double at || transcript.Length == 0)
        {
            return;
        }
        int index = ContextItems.LineAt(transcript, at, said);
        text.Line($"{indent}Transcript around it:");
        for (int i = Math.Max(0, index - around); i <= Math.Min(transcript.Length - 1, index + around); i++)
        {
            text.Line($"{indent}> {TranscriptLine(transcript[i], partner)}{(i == index ? "   <- here" : "")}");
        }
    }

    private static string TranscriptLine(CallTranscriptLine line, string partner)
    {
        string speaker = string.IsNullOrWhiteSpace(line.Speaker) ? (line.IsLearner ? "Me" : partner) : line.Speaker;
        return $"[{CallText.Timestamp(line.Start)}] {speaker}: {line.Text}{Suffix(" (second hearing: ", line.Disputed, ")")}";
    }

    private static string PartnerOf(CallAnalysisSummary call) => string.IsNullOrWhiteSpace(call.Partner) ? "Partner" : call.Partner;

    private static string Mistake(CallFindingView finding)
    {
        StringBuilder line = new(Time(finding));
        line.Append(string.IsNullOrWhiteSpace(finding.Said) ? finding.Pattern : $"said \"{finding.Said}\"");
        if (!string.IsNullOrWhiteSpace(finding.Better))
        {
            line.Append($" -> better \"{finding.Better}\"");
        }
        if (!string.IsNullOrWhiteSpace(finding.Explanation))
        {
            line.Append(". ").Append(finding.Explanation.Trim());
        }
        line.Append(Marks(finding));
        return line.ToString();
    }

    private static string Fact(CallFindingView fact) =>
        $"{Time(fact)}{Suffix("[", fact.Aspect, "] ")}{fact.Pattern}{Suffix(" (\"", fact.Said, "\")")}{(fact.FollowUp ? " - worth asking about next time" : "")}{Marks(fact)}";

    /// <summary>
    /// What the quote check and the user said about a finding, where it matters for trusting it.
    /// </summary>
    private static string Marks(CallFindingView finding)
    {
        List<string> marks = [];
        switch (finding.Quote)
        {
            case CallQuoteState.Possibly:
                marks.Add("the two transcriptions disagree here");
                break;
            case CallQuoteState.NotFound:
                marks.Add("quote not found in the transcript");
                break;
            case CallQuoteState.OtherSpeaker:
                marks.Add("quote was said by the other side");
                break;
        }
        if (finding.Feedback == CallFindingFeedback.Useful)
        {
            marks.Add("I marked this useful");
        }
        return marks.Count == 0 ? string.Empty : $" ({string.Join("; ", marks)})";
    }

    private static string Time(CallFindingView finding) =>
        finding.Time is double seconds ? $"[{CallText.Timestamp(seconds)}] " : string.Empty;

    private static string Word(string lemma, string reading, string wordClass) =>
        !string.IsNullOrWhiteSpace(reading) && reading != lemma ? $"{lemma} ({reading}, {CallText.ClassLabel(wordClass)})" : $"{lemma} ({CallText.ClassLabel(wordClass)})";

    private static string Severity(int severity) => CallText.SeverityLabel(severity).ToLowerInvariant();

    private static string Verdict(LessonVerdict verdict) => verdict switch
    {
        LessonVerdict.Correct => "correct",
        LessonVerdict.Partial => "partly correct",
        LessonVerdict.Wrong => "wrong",
        _ => "not answered"
    };

    private static string Limited(string title, int count, int limit) =>
        limit != ContextBlock.AllItems && count > limit ? $"{title} (top {limit} of {count})" : title;

    private static IEnumerable<T> Take<T>(IEnumerable<T> items, int limit) =>
        limit == ContextBlock.AllItems ? items : items.Take(limit);

    private static string Suffix(string before, string? value, string after = "") =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : before + value.Trim() + after;

    private static string Join(string separator, params string[] parts) =>
        string.Join(separator, parts.Where(p => !string.IsNullOrWhiteSpace(p)));

    private static DateTime CallDate(CallAnalysisSummary call) => call.DateKnown ? call.Date : call.Date.ToLocalTime();

    private static string Day(DateTime date) =>
        (date.Kind == DateTimeKind.Utc && date.TimeOfDay != TimeSpan.Zero ? date.ToLocalTime() : date).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>
    /// Lines with a blank line before each heading.
    /// </summary>
    private sealed class Text
    {
        private readonly StringBuilder _text = new();

        public void Heading(string title)
        {
            if (_text.Length > 0)
            {
                Blank();
            }
            _text.Append("### ").Append(title).Append('\n');
        }

        public void Line(string line) => _text.Append(line).Append('\n');

        public void Blank()
        {
            if (_text.Length > 0 && !(_text.Length >= 2 && _text[^1] == '\n' && _text[^2] == '\n'))
            {
                _text.Append('\n');
            }
        }

        public void Numbered(IEnumerable<string> items)
        {
            int number = 0;
            foreach (string item in items)
            {
                Line($"{++number}. {item}");
            }
        }

        public void Table(IEnumerable<string> header, IEnumerable<IEnumerable<string>> rows)
        {
            List<string> columns = [.. header];
            Line("| " + string.Join(" | ", columns.Select(Cell)) + " |");
            Line("|" + string.Concat(columns.Select(_ => " --- |")));
            foreach (IEnumerable<string> row in rows)
            {
                Line("| " + string.Join(" | ", row.Select(Cell)) + " |");
            }
        }

        public void More(int count, int limit)
        {
            if (limit != ContextBlock.AllItems && count > limit)
            {
                Line($"({count - limit} more not included)");
            }
        }

        private static string Cell(string text) => text.Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ");

        public override string ToString() => _text.ToString().TrimEnd();
    }
}
