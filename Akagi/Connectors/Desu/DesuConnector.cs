using Akagi.Flow;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using System.Collections.Concurrent;
using System.Text;
using Wacton.Desu.Japanese;

namespace Akagi.Connectors.Desu;

internal class DesuConnector : IDesuConnector, ISystemInitializer
{
    private static IJapaneseEntry[]? _entries;

    private readonly ILogger<DesuConnector> _logger;

    public DesuConnector(ILogger<DesuConnector> logger)
    {
        _logger = logger;
    }

    public Task InitializeAsync()
    {
        Task.Run(() =>
        {
            try
            {
                _logger.LogInformation("Loading entries from Desu...");
                _entries = [.. JapaneseDictionary.ParseEntries()];
                _logger.LogInformation("Loaded {Count} entries from Desu", _entries.Length);
            }
            catch (Exception ex)
            {
                _logger.LogError("Failed to load entries: {Message}", ex.Message);
            }
        });
        return Task.CompletedTask;
    }

    public string Lookup(string word, DesuUserConfig userConfig)
    {
        if (_entries is null || _entries.Length == 0)
        {
            return "No entries loaded. Please try again later.";
        }

        ConcurrentBag<IJapaneseEntry> foundEntries = [];
        Parallel.ForEach(_entries, entry =>
        {
            if (entry.Kanjis.Any(x => string.Equals(x.Text, word, StringComparison.OrdinalIgnoreCase)) ||
                entry.Readings.Any(x => string.Equals(x.Text, word, StringComparison.OrdinalIgnoreCase)) ||
                entry.Senses.Any(s => 
                    s.Glosses.Any(g => string.Equals(g.Language.ThreeLetterCode, userConfig.Language, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(g.Term, word, StringComparison.OrdinalIgnoreCase)))
                )
            {
                foundEntries.Add(entry);
            }

        });

        IJapaneseEntry[] toReturnEntries = [.. foundEntries.OrderBy(e => e.Sequence)];

        StringBuilder sb = new();
        sb.AppendLine($"{userConfig.DefaultPrint}{word}");

        if (foundEntries.IsEmpty)
        {
            sb.AppendLine("No entries found...");
        }
        else
        {
            foreach (IJapaneseEntry item in toReturnEntries)
            {
                sb.AppendLine();
                sb.AppendLine($"Entry: {string.Join(", ", item.Kanjis.Select(k => k.Text))} [{string.Join(", ", item.Readings.Select(r => r.Text))}]");
                foreach (ISense sense in item.Senses)
                {
                    List<string> translations = [];
                    foreach (IGloss gloss in sense.Glosses)
                    {
                        if (!string.Equals(gloss.Language.ThreeLetterCode, userConfig.Language, StringComparison.OrdinalIgnoreCase))
                        {
                            break;
                        }

                        translations.Add(gloss.Term);
                    }

                    if (translations.Count != 0)
                    {
                        sb.AppendLine(string.Join(", ", translations));
                    }
                }
            }
        }

        return sb.ToString();
    }
}
