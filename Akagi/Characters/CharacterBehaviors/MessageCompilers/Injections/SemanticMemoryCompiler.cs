using Akagi.Bridge.Attributes;
using Akagi.Characters.Conversations;
using Akagi.Characters.Memories;
using Akagi.Receivers;
using System.Text;

namespace Akagi.Characters.CharacterBehaviors.MessageCompilers.Injections;

[GraphNode]
internal class SemanticMemoryCompiler : InjectionCompiler
{
    private int _maxTokenBudget = 400;
    private int _topK = 5;
    private float _minSimilarityThreshold = 0.35f;
    private float _relevanceWeight = 1.0f;
    private float _recencyWeight = 0.3f;
    private float _importanceWeight = 0.5f;
    private float _decayFactor = 0.995f;
    private bool _updateLastAccessed = true;
    private bool _includeGlobalLore = true;
    private string _header = "Relevant Memories & Context";

    public int MaxTokenBudget
    {
        get => _maxTokenBudget;
        set => SetProperty(ref _maxTokenBudget, value);
    }

    public int TopK
    {
        get => _topK;
        set => SetProperty(ref _topK, value);
    }

    public float MinSimilarityThreshold
    {
        get => _minSimilarityThreshold;
        set => SetProperty(ref _minSimilarityThreshold, value);
    }

    public float RelevanceWeight
    {
        get => _relevanceWeight;
        set => SetProperty(ref _relevanceWeight, value);
    }

    public float RecencyWeight
    {
        get => _recencyWeight;
        set => SetProperty(ref _recencyWeight, value);
    }

    public float ImportanceWeight
    {
        get => _importanceWeight;
        set => SetProperty(ref _importanceWeight, value);
    }

    public float DecayFactor
    {
        get => _decayFactor;
        set => SetProperty(ref _decayFactor, value);
    }

    public bool UpdateLastAccessed
    {
        get => _updateLastAccessed;
        set => SetProperty(ref _updateLastAccessed, value);
    }

    public bool IncludeGlobalLore
    {
        get => _includeGlobalLore;
        set => SetProperty(ref _includeGlobalLore, value);
    }

    public string Header
    {
        get => _header;
        set => SetProperty(ref _header, value);
    }

    protected override async Task<Message[]> GetInjectionMessagesAsync(Context context)
    {
        if (context.EmbeddingService == null || string.IsNullOrWhiteSpace(context.Character.Id))
        {
            return [];
        }

        string queryText = ExtractQueryText(context);
        if (string.IsNullOrWhiteSpace(queryText))
        {
            return [];
        }

        float[] queryEmbedding;
        try
        {
            queryEmbedding = await context.EmbeddingService.GenerateEmbeddingAsync(queryText);
        }
        catch
        {
            // If embedding fails (network timeout or offline), fail gracefully without halting conversation
            return [];
        }

        if (queryEmbedding.Length == 0)
        {
            return [];
        }

        IMemoryDatabase memoryDb = context.DatabaseFactory.GetDatabase<IMemoryDatabase>();
        List<MemoryRecord> candidates = await memoryDb.GetActiveMemoriesAsync(
            context.Character.Id,
            context.User?.Id,
            IncludeGlobalLore);

        if (candidates.Count == 0)
        {
            return [];
        }

        List<ScoredMemory> ranked = MemoryScoringEngine.ScoreAndRank(
            candidates,
            queryEmbedding,
            DateTime.UtcNow,
            MinSimilarityThreshold,
            RelevanceWeight,
            RecencyWeight,
            ImportanceWeight,
            DecayFactor);

        if (ranked.Count == 0)
        {
            return [];
        }

        List<MemoryRecord> selected = [];
        int currentTokens = 0;

        foreach (ScoredMemory item in ranked)
        {
            if (selected.Count >= TopK)
            {
                break;
            }

            // Estimate ~3.8 characters per token
            int estTokens = (int)Math.Ceiling(item.Record.Fact.Length / 3.8);
            if (currentTokens + estTokens > MaxTokenBudget && selected.Count > 0)
            {
                continue;
            }

            selected.Add(item.Record);
            currentTokens += estTokens;
        }

        if (selected.Count == 0)
        {
            return [];
        }

        if (UpdateLastAccessed)
        {
            DateTime now = DateTime.UtcNow;
            IEnumerable<string> ids = selected.Select(m => m.Id!).Where(id => !string.IsNullOrEmpty(id));
            _ = memoryDb.UpdateLastAccessedBatchAsync(ids, now);
        }

        StringBuilder sb = new();
        sb.AppendLine($"### {Header}:");
        foreach (MemoryRecord record in selected)
        {
            string datePart = record.Timestamp != DateTime.MinValue && record.Timestamp != default
                ? $"({record.Timestamp:yyyy-MM-dd}) "
                : string.Empty;

            sb.AppendLine($"- {datePart}{record.Fact}");
        }

        TextMessage injectionMessage = new()
        {
            From = MessageType,
            Time = DateTime.UtcNow,
            Text = sb.ToString().TrimEnd(),
            CharacterId = context.Character.Id
        };

        return [injectionMessage];
    }

    private static string ExtractQueryText(Context context)
    {
        if (context.Conversation == null || context.Conversation.Messages.Count == 0)
        {
            return string.Empty;
        }

        // Search backwards for the latest text message sent by the user
        TextMessage? lastUserMessage = context.Conversation.Messages
            .OfType<TextMessage>()
            .LastOrDefault(m => (m.From & Message.Type.User) != 0);

        if (lastUserMessage != null && !string.IsNullOrWhiteSpace(lastUserMessage.Text))
        {
            return lastUserMessage.Text;
        }

        // Fall back to the most recent text message of any origin
        TextMessage? anyLastMessage = context.Conversation.Messages
            .OfType<TextMessage>()
            .LastOrDefault();

        return anyLastMessage?.Text ?? string.Empty;
    }
}
