using Akagi.Connectors.Desu;
using Akagi.Connectors.Tatoeba;
using Akagi.Flow;
using Akagi.Life;
using Akagi.Life.Study;
using Microsoft.Extensions.DependencyInjection;
using System.Text;

namespace Akagi.Receivers.Commands.Life;

internal class AddVocabularyCommand : LifeCommand
{
    public override string Name => "AddVocabulary";

    public override string Description =>
        "Add a Japanese word the user is learning (or just struggled with) to their review deck.";

    public override Argument[] GetDefaultArguments() =>
    [
        Text("Term", "The word as written, e.g. '影響'.", required: true),
        Text("Reading", "Kana reading, e.g. 'えいきょう'."),
        Text("Meaning", "Short meaning in the user's language.")
    ];

    public override async Task<Command[]> Execute(Context context)
    {
        DateTime now = DateTime.UtcNow;
        LifeProfile profile = await LoadProfile(context);
        bool added = profile.Study.AddVocab(RequireArgument("Term"), GetArgument("Reading") ?? string.Empty, GetArgument("Meaning") ?? string.Empty, now, out VocabItem item);
        if (added)
        {
            profile.Study.GetDay(profile.LocalDate(now)).NewItems++;
        }
        profile.MarkChanged();
        await SaveProfile(context, profile);

        return Reply(context, added
            ? $"Added {item.Term} [{item.Reading}] '{item.Meaning}' to the deck; first review tomorrow."
            : $"{item.Term} is already in the deck (box {item.Box}, {item.Correct}/{item.Seen} correct).");
    }
}

internal class ReviewVocabularyCommand : LifeCommand
{
    public override string Name => "ReviewVocabulary";

    public override string Description =>
        "Record the result of quizzing the user on a word. Correct answers move it to a longer interval, mistakes reset it.";

    public override Argument[] GetDefaultArguments() =>
    [
        Text("Term", "The word that was reviewed.", required: true),
        Flag("Correct", "Whether the user knew it.", required: true)
    ];

    public override async Task<Command[]> Execute(Context context)
    {
        DateTime now = DateTime.UtcNow;
        bool correct = GetBoolArgument("Correct") ?? throw new ArgumentException("Correct must be true or false.");
        LifeProfile profile = await LoadProfile(context);
        VocabItem? item = profile.Study.FindVocab(RequireArgument("Term"));
        if (item == null)
        {
            return Reply(context, $"'{GetArgument("Term")}' is not in the deck. Add it with AddVocabulary first.");
        }

        item.Review(correct, now);
        profile.Study.GetDay(profile.LocalDate(now)).Reviews++;
        profile.MarkChanged();
        await SaveProfile(context, profile);

        return Reply(context, $"{item.Term}: {(correct ? "correct" : "missed")}, now in box {item.Box}, next review {profile.ToLocal(item.NextReviewAt):yyyy-MM-dd}.");
    }
}

internal class DueVocabularyCommand : LifeCommand
{
    public override string Name => "DueVocabulary";

    public override string Description => "List words that are due for review, weakest first, to quiz the user.";

    public override Argument[] GetDefaultArguments() =>
    [
        Integer("Count", "How many words to return (default 10).")
    ];

    public override async Task<Command[]> Execute(Context context)
    {
        DateTime now = DateTime.UtcNow;
        LifeProfile profile = await LoadProfile(context);
        int count = Math.Clamp(GetIntArgument("Count") ?? 10, 1, 50);
        List<VocabItem> due = [.. profile.Study.Vocabulary
            .Where(v => v.IsDue(now))
            .OrderBy(v => v.Box)
            .ThenBy(v => v.NextReviewAt)
            .Take(count)];

        if (due.Count == 0)
        {
            return Reply(context, "Nothing is due for review right now.");
        }

        StringBuilder builder = new();
        builder.AppendLine($"{due.Count} word(s) due (term [reading] meaning, box):");
        foreach (VocabItem item in due)
        {
            builder.AppendLine($"- {item.Term} [{item.Reading}] {item.Meaning} (box {item.Box})");
        }
        return Reply(context, builder.ToString().TrimEnd());
    }
}

internal class RecordGrammarMistakeCommand : LifeCommand
{
    public override string Name => "RecordGrammarMistake";

    public override string Description =>
        "Record a grammar mistake the user made so recurring problems can be practiced. Use the grammar point as Pattern, e.g. '〜ようにする'.";

    public override Argument[] GetDefaultArguments() =>
    [
        Text("Pattern", "The grammar point, e.g. '〜わけではない'.", required: true),
        Text("Explanation", "What went wrong, briefly."),
        Text("Example", "The user's incorrect sentence."),
        Text("Correction", "The corrected sentence.")
    ];

    public override async Task<Command[]> Execute(Context context)
    {
        DateTime now = DateTime.UtcNow;
        LifeProfile profile = await LoadProfile(context);
        GrammarMistake mistake = profile.Study.RecordMistake(
            RequireArgument("Pattern"),
            GetArgument("Explanation") ?? string.Empty,
            GetArgument("Example") ?? string.Empty,
            GetArgument("Correction") ?? string.Empty,
            now);
        profile.MarkChanged();
        await SaveProfile(context, profile);

        string recurring = mistake.Count > 1 ? $" This is mistake #{mistake.Count} with this pattern - worth a focused drill." : string.Empty;
        return Reply(context, $"Recorded mistake with {mistake.Pattern}.{recurring}");
    }
}

internal class LogStudyCommand : LifeCommand
{
    public override string Name => "LogStudy";

    public override string Description => "Log time the user spent studying Japanese, which keeps their streak alive.";

    public override Argument[] GetDefaultArguments() =>
    [
        Integer("Minutes", "Minutes studied.", required: true),
        Text("Activity", "What they did, e.g. 'reading NHK Easy', 'shadowing', 'mock exam'.")
    ];

    public override async Task<Command[]> Execute(Context context)
    {
        DateTime now = DateTime.UtcNow;
        int minutes = GetIntArgument("Minutes") ?? throw new ArgumentException("Minutes must be a number.");
        LifeProfile profile = await LoadProfile(context);
        StudyDay day = profile.Study.GetDay(profile.LocalDate(now));
        day.Minutes += Math.Max(0, minutes);
        string? activity = GetArgument("Activity");
        if (activity != null)
        {
            day.Activities.Add(activity);
        }
        profile.MarkChanged();
        await SaveProfile(context, profile);

        StudySummary summary = StudySummary.Create(profile, now);
        return Reply(context, $"Logged {minutes} min of study. {summary.Describe()}");
    }
}

internal class StudyStatusCommand : LifeCommand
{
    public override string Name => "StudyStatus";

    public override string Description =>
        "Look up the user's Japanese study progress: streak, vocabulary toward the JLPT goal, due reviews and recurring grammar mistakes.";

    public override Argument[] GetDefaultArguments() => [];

    public override async Task<Command[]> Execute(Context context)
    {
        LifeProfile profile = await LoadProfile(context);
        StudySummary summary = StudySummary.Create(profile, DateTime.UtcNow);

        StringBuilder builder = new();
        builder.AppendLine(summary.Describe());
        foreach (GrammarMistake mistake in summary.TopMistakes)
        {
            builder.AppendLine($"- {mistake.Pattern} ({mistake.Count}x): {mistake.Explanation}");
            if (!string.IsNullOrEmpty(mistake.LastExample))
            {
                builder.AppendLine($"  wrote: {mistake.LastExample}{(string.IsNullOrEmpty(mistake.Correction) ? string.Empty : $" -> {mistake.Correction}")}");
            }
        }
        return Reply(context, builder.ToString().TrimEnd());
    }
}

internal class CheckReadingCommand : LifeCommand
{
    public override string Name => "CheckReading";

    public override string Description =>
        "Compare the kana reading the user intended with what was heard in their voice note (both in kana) and list pronunciation slips " +
        "such as vowel length, small tsu, dakuten or contracted sounds.";

    public override Argument[] GetDefaultArguments() =>
    [
        Text("Expected", "Correct reading in kana.", required: true),
        Text("Heard", "What the user actually said, in kana.", required: true)
    ];

    public override Task<Command[]> Execute(Context context)
    {
        ReadingChecker.Result result = ReadingChecker.Check(RequireArgument("Expected"), RequireArgument("Heard"));
        return Task.FromResult(Reply(context, result.Describe()));
    }
}

internal class LookupWordCommand : LifeCommand
{
    public override string Name => "LookupWord";

    public override string Description => "Look up a Japanese word (kanji, kana or English) in the JMDict dictionary.";

    public override Argument[] GetDefaultArguments() =>
    [
        Text("Word", "The word to look up.", required: true)
    ];

    public override Task<Command[]> Execute(Context context)
    {
        if (!Globals.IsInitialized)
        {
            return Task.FromResult(Reply(context, "Dictionary is not available."));
        }

        using IServiceScope scope = Globals.Instance.ServiceProvider.CreateScope();
        IDesuConnector desu = scope.ServiceProvider.GetRequiredService<IDesuConnector>();
        DesuUserConfig config = context.User.GetConfig<DesuUserConfig>() ?? new DesuUserConfig { DefaultPrint = string.Empty, Language = "eng" };
        string result = desu.Lookup(RequireArgument("Word"), config);
        return Task.FromResult(Reply(context, Truncate(result, 2500)));
    }

    internal static string Truncate(string text, int max) => text.Length <= max ? text : text[..max] + Environment.NewLine + "...";
}

internal class FindExampleSentencesCommand : LifeCommand
{
    public override string Name => "FindExampleSentences";

    public override string Description => "Find natural example sentences with translations from Tatoeba for a word or grammar pattern.";

    public override Argument[] GetDefaultArguments() =>
    [
        Text("Query", "Word or pattern to search for.", required: true),
        Integer("Count", "Number of sentences (default 3).")
    ];

    public override async Task<Command[]> Execute(Context context)
    {
        if (!Globals.IsInitialized)
        {
            return Reply(context, "Example sentence search is not available.");
        }

        using IServiceScope scope = Globals.Instance.ServiceProvider.CreateScope();
        ITatoebaConnector tatoeba = scope.ServiceProvider.GetRequiredService<ITatoebaConnector>();
        TatoebaUserConfig config = context.User.GetConfig<TatoebaUserConfig>()
            ?? new TatoebaUserConfig { TargetLanguage = "jpn", TranslationLanguage = "eng", MaxSentences = 3 };
        config.MaxSentences = Math.Clamp(GetIntArgument("Count") ?? config.MaxSentences, 1, 10);

        string result = await tatoeba.GetExample(RequireArgument("Query"), config);
        return Reply(context, LookupWordCommand.Truncate(result, 2500));
    }
}
