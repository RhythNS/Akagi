using Akagi.Receivers.Commands;
using Akagi.Receivers.Commands.Life;
using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Text;

namespace Akagi.Life.Japanese.Calls;

internal record CoachingResult(List<CallFinding> Moments, CallCoaching? Advice);

/// <summary>
/// Passes that read the whole call at once: patterns of a conversation and facts that build up over minutes do not fit
/// in one chunk.
/// </summary>
internal interface ICallWholeReviewer
{
    /// <summary>
    /// How the learner converses: reactions, questions, follow-ups, turn-taking; Japanese-specific and general.
    /// </summary>
    public Task<CoachingResult> CoachAsync(CallJob job, CancellationToken cancellationToken);

    /// <summary>
    /// What was learned about the partner and the learner, without repeating what is already known.
    /// </summary>
    public Task<List<CallFinding>> FactsAsync(CallJob job, IReadOnlyList<string> knownPartner, IReadOnlyList<string> knownLearner,
        CancellationToken cancellationToken);
}

internal partial class ToolCallReviewer
{
    /// <summary>
    /// Transcript characters per whole-call request. An hour of conversation is about 60 000, so almost every call is one
    /// request; longer ones are read in consecutive windows.
    /// </summary>
    public const int WholeCallCharacters = 150_000;
    public const int MaxMomentsPerWindow = 15;

    public static readonly string[] CoachingTools = ["NoteConversationMoment", "WriteConversationAdvice", "StopExecution"];
    public static readonly string[] FactTools = ["NotePersonFact", "StopExecution"];

    public async Task<CoachingResult> CoachAsync(CallJob job, CancellationToken cancellationToken)
    {
        List<CallFinding> moments = [];
        CallCoaching? advice = null;
        List<string> windows = WholeCallWindows(job);
        for (int i = 0; i < windows.Count; i++)
        {
            _logger.LogInformation("Call {JobId}: coaching window {Window}/{Windows}", job.Id, i + 1, windows.Count);
            Command[] calls = await RequestAsync(job, "Conversation Coaching", BuildCoachingInstruction(job),
                BuildCoachingMessage(job, windows[i], i, windows.Count), CoachingTools, cancellationToken);
            (List<CallFinding> found, CallCoaching? written) = ReadCoaching(calls);
            moments.AddRange(found);
            if (written != null)
            {
                advice = advice == null ? written : new CallCoaching
                {
                    Summary = advice.Summary + " " + written.Summary,
                    Tips = [.. advice.Tips.Concat(written.Tips).Distinct().Take(5)]
                };
            }
        }
        return new CoachingResult(moments, advice);
    }

    public async Task<List<CallFinding>> FactsAsync(CallJob job, IReadOnlyList<string> knownPartner, IReadOnlyList<string> knownLearner,
        CancellationToken cancellationToken)
    {
        List<CallFinding> facts = [];
        List<string> windows = WholeCallWindows(job);
        for (int i = 0; i < windows.Count; i++)
        {
            _logger.LogInformation("Call {JobId}: facts window {Window}/{Windows}", job.Id, i + 1, windows.Count);
            // Facts from an earlier window are known by now
            List<string> partner = [.. knownPartner, .. facts.Where(f => f.Subject == FactSubject.Partner).Select(f => f.Pattern)];
            List<string> learner = [.. knownLearner, .. facts.Where(f => f.Subject == FactSubject.Learner).Select(f => f.Pattern)];
            Command[] calls = await RequestAsync(job, "Call Facts", BuildFactsInstruction(job),
                BuildFactsMessage(job, windows[i], i, windows.Count, partner, learner), FactTools, cancellationToken);
            facts.AddRange(ReadFacts(calls));
        }
        return facts;
    }

    internal static (List<CallFinding> Moments, CallCoaching? Advice) ReadCoaching(IEnumerable<Command> calls)
    {
        List<CallFinding> moments = [];
        CallCoaching? advice = null;
        foreach (Command call in calls)
        {
            switch (call)
            {
                case NoteConversationMomentCommand moment when moment.Said.Length > 0:
                    moments.Add(new CallFinding
                    {
                        Kind = FindingKind.Conversation,
                        Japanese = moment.Japanese,
                        Aspect = moment.Aspect.Length > 0 ? moment.Aspect : "other",
                        WentWell = moment.WentWell,
                        Context = moment.PartnerLine,
                        Said = moment.Said,
                        Better = moment.WentWell ? string.Empty : moment.Better,
                        Explanation = moment.Explanation,
                        Pattern = moment.Aspect,
                        Severity = moment.WentWell ? 1 : 2
                    });
                    break;
                case WriteConversationAdviceCommand written when written.Summary.Length > 0 || written.Tips.Length > 0:
                    advice = new CallCoaching { Summary = written.Summary, Tips = [.. written.Tips] };
                    break;
            }
        }
        return (moments, advice);
    }

    internal static List<CallFinding> ReadFacts(IEnumerable<Command> calls) =>
        [.. calls.OfType<NotePersonFactCommand>()
            .Where(f => f.Fact.Length > 0)
            .Select(f => new CallFinding
            {
                Kind = FindingKind.Fact,
                Subject = f.About == NotePersonFactCommand.FactSubjectChoice.Learner ? FactSubject.Learner : FactSubject.Partner,
                Aspect = f.Topic.Length > 0 ? f.Topic : "other",
                Pattern = f.Fact,
                Said = f.Quote,
                FollowUp = f.AskLater
            })];

    /// <summary>
    /// The whole transcript with labels and times, in windows of <see cref="WholeCallCharacters"/>.
    /// </summary>
    internal static List<string> WholeCallWindows(CallJob job)
    {
        List<string> windows = [];
        StringBuilder text = new();
        foreach (TranscriptSegment segment in job.Segments.OrderBy(s => s.Start))
        {
            string line = TranscriptChunker.Format(segment);
            if (text.Length > 0 && text.Length + line.Length > WholeCallCharacters)
            {
                windows.Add(text.ToString().TrimEnd());
                text.Clear();
            }
            text.AppendLine(line);
        }
        if (text.Length > 0)
        {
            windows.Add(text.ToString().TrimEnd());
        }
        return windows;
    }

    internal static string BuildCoachingInstruction(CallJob job)
    {
        string partner = string.IsNullOrWhiteSpace(job.Partner) ? "their partner" : job.Partner;
        StringBuilder text = new();
        text.AppendLine($"You are {{{{character}}}}, {{{{user}}}}'s Japanese tutor and conversation coach. You are reading the whole transcript of a real conversation between {{{{user}}}} and {partner}.");
        text.AppendLine("Grammar and word choice are reviewed elsewhere. Your job is the conversation itself: how {{user}} keeps it going, shows interest, reacts, asks, and shares.");
        text.AppendLine(job.HasBothVoices
            ? $"Lines labeled '{job.LearnerSpeaker}' are {{{{user}}}} and lines labeled '{job.PartnerLabel}' are {partner}; each voice was recorded on its own channel."
            : "Infer from the context which lines are {{user}}'s.");
        text.AppendLine();
        text.AppendLine("Look at two kinds of things and set Scope accordingly:");
        text.AppendLine("- Japanese: how this is done in Japanese. Variety and fit of aizuchi and reactions (へえ、そうなんだ、マジで、いいね、大変だったね instead of the same そう again and again), softening and hedging that sounds natural, how questions are phrased and ended, picking up the partner's words, keeping the register of the relationship.");
        text.AppendLine("- General: conversation skill in any language. Follow-up questions on what the partner just shared, open questions instead of yes/no ones, sharing something back instead of only answering, taking turns fairly, building on a topic instead of dropping it, noticing feelings, remembering what was said earlier.");
        text.AppendLine();
        text.AppendLine($"Call NoteConversationMoment for the most instructive moments, up to {MaxMomentsPerWindow}. Most should be openings {{{{user}}}} missed or reactions that could be better, each with a natural Japanese alternative in Better. Include at least two moments where {{{{user}}}} did it well (WentWell yes), so the feedback stays fair.");
        text.AppendLine("Copy Said character for character from one of {{user}}'s lines and PartnerLine from the line they were reacting to. The quotes are checked against the transcript and moments with quotes that are not there are thrown away.");
        text.AppendLine("Then call WriteConversationAdvice once: the overall picture and up to five tips, each with a ready-to-use Japanese example.");
        text.AppendLine("Use the measurements you are given as evidence, but judge from the conversation: a high speaking share is fine when the partner asked a lot.");
        text.Append("Make all your calls in this one response, there is no second turn.");
        return text.ToString();
    }

    internal static string BuildCoachingMessage(CallJob job, string window, int index, int count)
    {
        StringBuilder text = new();
        text.AppendLine(Evidence(job));
        text.AppendLine();
        text.AppendLine(count == 1 ? "The whole conversation:" : $"Part {index + 1} of {count} of the conversation:");
        text.Append(window);
        return text.ToString();
    }

    /// <summary>
    /// What the transcript shows by counting, which a model reading it does poorly.
    /// </summary>
    internal static string Evidence(CallJob job)
    {
        CallStats stats = job.Stats?.Version == CallStats.CurrentVersion ? job.Stats : CallStats.Compute(job);
        StringBuilder text = new();
        text.AppendLine("Measurements of this conversation:");
        if (stats.LearnerShare is double share)
        {
            text.AppendLine($"- {{{{user}}}} spoke {share.ToString("P0", CultureInfo.InvariantCulture)} of the time, in {stats.LearnerTurns} turns of {stats.LearnerCharactersPerTurn:0} characters on average; the partner in {stats.PartnerTurns} turns of {stats.PartnerCharactersPerTurn:0}.");
            text.AppendLine($"- Questions asked: {{{{user}}}} {stats.LearnerQuestions}, the partner {stats.PartnerQuestions}.");
            if (stats.MedianResponseSeconds is double median)
            {
                text.AppendLine($"- {{{{user}}}} starts answering {median.ToString("0.0", CultureInfo.InvariantCulture)} s after the partner stops (median), and started {stats.LearnerOverlaps} times while the partner was still talking.");
            }
        }
        else
        {
            text.AppendLine($"- {stats.LearnerTurns} turns; the voices are not separated, so shares are unknown.");
        }

        static string Top(IEnumerable<CallWord> words, Func<CallWord, int> count) =>
            string.Join(", ", words.Where(w => count(w) > 0).OrderByDescending(count).Take(8).Select(w => $"{w.Lemma} {count(w)}x"));
        IEnumerable<CallWord> reactions = stats.Words.Where(w => w.Class == WordClass.Interjection || w.Lemma is "そう" or "まあ" or "なるほど" or "確か");
        string mine = Top(reactions, w => w.Learner);
        if (mine.Length > 0)
        {
            text.AppendLine($"- {{{{user}}}}'s reactions and back-channel words: {mine}.");
        }
        string theirs = Top(reactions, w => w.Partner);
        if (stats.Separated && theirs.Length > 0)
        {
            text.AppendLine($"- The partner's, for comparison: {theirs}.");
        }
        return text.ToString().TrimEnd();
    }

    internal static string BuildFactsInstruction(CallJob job)
    {
        string partner = string.IsNullOrWhiteSpace(job.Partner) ? "the partner" : job.Partner;
        StringBuilder text = new();
        text.AppendLine($"You are {{{{character}}}}. You are reading the whole transcript of a conversation between {{{{user}}}} and {partner}, to remember what was learned about both of them.");
        text.AppendLine(job.HasBothVoices
            ? $"Lines labeled '{job.LearnerSpeaker}' are {{{{user}}}} and lines labeled '{job.PartnerLabel}' are {partner}."
            : "Infer from the context who is speaking.");
        text.AppendLine("{{user}} keeps these notes to remember the people they talk to, so be thorough: write down everything specific that was said.");
        text.AppendLine($"Call NotePersonFact once per fact. About is partner for {partner} and learner for {{{{user}}}}. Facts about others they mention go under whoever told them, e.g. '{partner}'s sister Mika is a nurse'.");
        text.AppendLine("Good facts are specific and lasting or worth following up on: work and study, family and pets with their names, where they live and have been, hobbies, the games, shows and music they like, opinions, plans with their dates, things that happened to them, how they feel about something.");
        text.AppendLine("Only write what the conversation says; if something is unclear or misheard, write 'seems to' or leave it out. Do not note small talk without content ('it is hot today') or anything about the language lesson itself.");
        text.AppendLine("Quote must be copied character for character from one line of the transcript that says it. Facts whose quote is not in the transcript are thrown away.");
        text.AppendLine("Set AskLater to yes for things worth asking about next time: upcoming events, unfinished stories, problems they were dealing with, recommendations to check.");
        text.AppendLine("You are given what is already known. Do not repeat a known fact; if something changed, note the new state ('now works at...').");
        text.Append("Make all your calls in this one response, there is no second turn. If nothing new was learned, call StopExecution.");
        return text.ToString();
    }

    internal static string BuildFactsMessage(CallJob job, string window, int index, int count, IReadOnlyList<string> knownPartner, IReadOnlyList<string> knownLearner)
    {
        string partner = string.IsNullOrWhiteSpace(job.Partner) ? "the partner" : job.Partner;
        StringBuilder text = new();
        text.AppendLine(knownPartner.Count == 0 ? $"Nothing is known about {partner} yet." : $"Already known about {partner}:");
        foreach (string fact in knownPartner)
        {
            text.AppendLine("- " + fact);
        }
        text.AppendLine();
        text.AppendLine(knownLearner.Count == 0 ? "Nothing from earlier calls is known about {{user}} yet." : "Already known about {{user}} from earlier calls:");
        foreach (string fact in knownLearner)
        {
            text.AppendLine("- " + fact);
        }
        text.AppendLine();
        text.AppendLine(count == 1 ? "The whole conversation:" : $"Part {index + 1} of {count} of the conversation:");
        text.Append(window);
        return text.ToString();
    }
}
