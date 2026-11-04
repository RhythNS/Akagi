using Akagi.LLMs;

namespace Akagi.Life.Japanese.Calls;

/// <summary>
/// What one pass of a call's analysis used and cost, summed over its requests. Re-analyses and catch-ups add to it, so
/// it is what the call cost in all.
/// </summary>
internal class CallCost
{
    public const string Transcription = "transcription";
    public const string Review = "review";
    public const string Coaching = "coaching";
    public const string Facts = "facts";
    public const string Merge = "merge";

    public static readonly string[] Order = [Transcription, Review, Coaching, Facts, Merge];

    public string Pass { get; set; } = string.Empty;
    public int Requests { get; set; }
    public long PromptTokens { get; set; }
    public long CachedTokens { get; set; }
    public long CompletionTokens { get; set; }
    public long ReasoningTokens { get; set; }
    public double AudioSeconds { get; set; }
    /// <summary>
    /// In US dollars.
    /// </summary>
    public double Cost { get; set; }
    /// <summary>
    /// Part of the cost was worked out from the price rather than reported by the provider.
    /// </summary>
    public bool Estimated { get; set; }
}

internal static class CallCosts
{
    public static CallCost For(CallJob job, string pass)
    {
        CallCost? cost = job.Costs.FirstOrDefault(c => c.Pass == pass);
        if (cost == null)
        {
            cost = new CallCost { Pass = pass };
            job.Costs.Add(cost);
        }
        return cost;
    }

    public static void Add(CallJob job, string pass, IEnumerable<LLMCallUsage> usage)
    {
        CallCost cost = For(job, pass);
        foreach (LLMCallUsage used in usage)
        {
            cost.Requests++;
            cost.PromptTokens += used.PromptTokens;
            cost.CachedTokens += used.CachedTokens;
            cost.CompletionTokens += used.CompletionTokens;
            cost.ReasoningTokens += used.ReasoningTokens;
            cost.Cost += used.Cost ?? 0;
            cost.Estimated |= used.Cost == null;
        }
    }

    /// <summary>
    /// One paid transcription request: its reported cost, or else its audio length at <paramref name="pricePerHour"/>.
    /// </summary>
    public static void AddTranscription(CallJob job, TranscriptionUsage? usage, double fallbackSeconds, double pricePerHour)
    {
        CallCost cost = For(job, CallCost.Transcription);
        double seconds = usage?.Seconds is > 0 ? usage.Seconds : fallbackSeconds;
        cost.Requests++;
        cost.AudioSeconds += seconds;
        if (usage?.Cost is double reported)
        {
            cost.Cost += reported;
        }
        else
        {
            cost.Cost += seconds / 3600 * pricePerHour;
            cost.Estimated = true;
        }
    }

    public static double Total(CallJob job) => job.Costs.Sum(c => c.Cost);
}
