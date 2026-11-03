using Akagi.Bridge.Chat.Models;
using System.Globalization;

namespace Akagi.Web.Pages.Settings;

/// <summary>
/// Finds models for what the user typed. Every word has to appear in the id or the name; words that start a part of
/// the id or a word of the name count more, so "ge" brings up Gemini before models that merely contain "ge".
/// Equal matches stay in the given order.
/// </summary>
public static class ModelSearch
{
    private static readonly char[] Separators = ['/', '-', ':', '.', '_', ' ', '(', ')'];

    public static IReadOnlyList<LLMModel> Find(IEnumerable<LLMModel> models, string? query, int limit = 12)
    {
        string[] words = (query ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (words.Length == 0)
        {
            return [];
        }

        string trimmed = query!.Trim();
        return [.. models
            .Select(model => (model, score: Score(model, words, trimmed)))
            .Where(x => x.score > 0)
            // Stable: equal matches keep the provider's order, which lists new models first
            .OrderByDescending(x => x.score)
            .Take(limit)
            .Select(x => x.model)];
    }

    /// <summary>
    /// Zero when a word is missing.
    /// </summary>
    internal static int Score(LLMModel model, string[] words, string query)
    {
        if (string.Equals(model.Id, query, StringComparison.OrdinalIgnoreCase))
        {
            return 1000;
        }

        string[] parts = [.. model.Id.Split(Separators, StringSplitOptions.RemoveEmptyEntries),
            .. model.Name.Split(Separators, StringSplitOptions.RemoveEmptyEntries)];

        // The family, "gemini" in "google/gemini-3-flash" or "Google: Gemini 3 Flash", says most about a model
        string[] families = [FirstPart(model.Id[(model.Id.IndexOf('/') + 1)..]), FirstPart(model.Name[(model.Name.LastIndexOf(':') + 1)..])];

        int score = 0;
        foreach (string word in words)
        {
            if (families.Any(f => f.StartsWith(word, StringComparison.OrdinalIgnoreCase)))
            {
                score += 4;
            }
            else if (parts.Any(p => p.StartsWith(word, StringComparison.OrdinalIgnoreCase)))
            {
                score += 3;
            }
            else if (model.Id.Contains(word, StringComparison.OrdinalIgnoreCase)
                || model.Name.Contains(word, StringComparison.OrdinalIgnoreCase))
            {
                score += 1;
            }
            else
            {
                return 0;
            }
        }
        return score;
    }

    private static string FirstPart(string text) =>
        text.Split(Separators, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;

    /// <summary>
    /// "TextRoleplay" becomes "Text roleplay".
    /// </summary>
    public static string Humanize(string usage)
    {
        System.Text.StringBuilder builder = new();
        foreach (char c in usage)
        {
            if (char.IsUpper(c) && builder.Length > 0)
            {
                builder.Append(' ').Append(char.ToLowerInvariant(c));
            }
            else
            {
                builder.Append(c);
            }
        }
        return builder.ToString();
    }

    public static string FormatContext(int tokens) => tokens switch
    {
        <= 0 => string.Empty,
        >= 1_000_000 => (tokens / 1_000_000d).ToString("0.#", CultureInfo.InvariantCulture) + "M",
        >= 1_000 => (tokens / 1_000d).ToString("0", CultureInfo.InvariantCulture) + "K",
        _ => tokens.ToString(CultureInfo.InvariantCulture)
    };

    /// <summary>
    /// Input and output price per million tokens, or empty when unknown.
    /// </summary>
    public static string FormatPrice(LLMModel model)
    {
        if (model.PromptPrice is not double input || model.CompletionPrice is not double output)
        {
            return string.Empty;
        }
        if (input == 0 && output == 0)
        {
            return "free";
        }
        return $"${Price(input)} / ${Price(output)}";
    }

    private static string Price(double value) =>
        value.ToString(value < 1 ? "0.###" : "0.##", CultureInfo.InvariantCulture);
}
