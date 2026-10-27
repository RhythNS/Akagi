using Akagi.Characters.Groups;
using Akagi.Characters.Memories;
using Akagi.Data;
using Akagi.Embeddings;
using Akagi.Receivers;
using Akagi.Users;

namespace Akagi.Characters.Conversations;

/// <summary>
/// The conversations a session spans: a single character, or a group together with every member,
/// since group members keep their own copy of the group conversation.
/// </summary>
internal class SessionScope
{
    public CharacterGroup? Group { get; init; }
    public required IReadOnlyList<Character> Characters { get; init; }

    public IEnumerable<ConversationContainer> Containers =>
        Group != null ? [Group.Conversations, .. Characters.Select(c => c.Conversations)] : Characters.Select(c => c.Conversations);

    /// <summary>
    /// The conversation the user sees: the group's when there is one.
    /// </summary>
    public ConversationContainer Primary => Group?.Conversations ?? Characters[0].Conversations;

    public static SessionScope ForCharacter(Character character) => new() { Characters = [character] };

    public static async Task<SessionScope> ForGroup(CharacterGroup group, ICharacterDatabase characters)
    {
        List<Character> members = [];
        foreach (string memberId in group.MemberCharacterIds)
        {
            Character? member = await characters.GetCharacter(memberId);
            if (member != null)
            {
                members.Add(member);
            }
        }
        return new SessionScope { Group = group, Characters = members };
    }
}

internal record SessionEndResult(Conversation Ended, ConversationEndMode Mode, int Reflected, int MemoriesStored);

/// <summary>
/// Explicit conversation lifecycle: a session stays open until the user ends it, and the user decides whether
/// the companion reflects on it, forgets it (incognito) or keeps the user's own summary instead.
/// </summary>
internal class SessionManager
{
    public const string SessionMemoryCategory = "Session";
    public const float SummaryImportance = 6.0f;

    private readonly IReceiver _receiver;
    private readonly IDatabaseFactory _databaseFactory;
    private readonly IEmbeddingService? _embeddingService;

    public SessionManager(IReceiver receiver, IDatabaseFactory databaseFactory, IEmbeddingService? embeddingService = null)
    {
        _receiver = receiver;
        _databaseFactory = databaseFactory;
        _embeddingService = embeddingService;
    }

    public static ConversationEndMode? ParseMode(string? mode)
    {
        return mode?.Trim().ToLowerInvariant() switch
        {
            null or "" or "reflect" or "default" => ConversationEndMode.Reflect,
            "incognito" or "forget" or "private" => ConversationEndMode.Incognito,
            "summary" or "custom" or "customsummary" => ConversationEndMode.CustomSummary,
            _ => null
        };
    }

    /// <summary>
    /// Ends whatever is running the usual way and opens a session in every conversation of the scope.
    /// </summary>
    public async Task<Conversation> StartAsync(SessionScope scope, User user, string topic, DateTime nowUtc)
    {
        List<Character> endedNormally = [.. scope.Characters.Where(c => c.Conversations.Current is { Messages.Count: > 0 })];

        Conversation? primary = null;
        foreach (ConversationContainer container in scope.Containers)
        {
            Conversation session = container.StartSession(topic, nowUtc);
            if (container == scope.Primary)
            {
                primary = session;
            }
        }

        await SaveAsync(scope);
        foreach (Character character in endedNormally)
        {
            await _receiver.OnConversationEnded(character, user);
        }
        return primary!;
    }

    /// <summary>
    /// Ends the running conversation (a session or a normal one) with the chosen mode.
    /// Returns null when nothing was running.
    /// </summary>
    public async Task<SessionEndResult?> EndAsync(SessionScope scope, User user, ConversationEndMode mode, string? summary, DateTime nowUtc)
    {
        if (mode == ConversationEndMode.CustomSummary && string.IsNullOrWhiteSpace(summary))
        {
            throw new ArgumentException("A custom summary needs the summary text.", nameof(summary));
        }

        Conversation? primaryEnded = null;
        if (scope.Group != null)
        {
            primaryEnded = scope.Group.Conversations.EndCurrent(mode, nowUtc);
        }

        List<(Character Character, Conversation Ended)> ended = [];
        foreach (Character character in scope.Characters)
        {
            Conversation? conversation = character.Conversations.EndCurrent(mode, nowUtc);
            if (conversation != null)
            {
                ended.Add((character, conversation));
                primaryEnded ??= conversation;
            }
        }

        if (primaryEnded == null)
        {
            return null;
        }

        int memories = 0;
        if (mode == ConversationEndMode.CustomSummary)
        {
            foreach ((Character character, Conversation conversation) in ended)
            {
                StoreSummaryThought(character, conversation, summary!.Trim(), nowUtc);
                await StoreSummaryMemoryAsync(character, user, summary!.Trim(), nowUtc);
                memories++;
            }
        }

        await SaveAsync(scope);

        int reflected = 0;
        if (mode == ConversationEndMode.Reflect)
        {
            foreach ((Character character, Conversation conversation) in ended.Where(e => e.Ended.Messages.Count > 0))
            {
                await _receiver.ReflectConversation(character, user, conversation.Id);
                await _receiver.OnConversationEnded(character, user);
                reflected++;
            }
            await SaveAsync(scope);
        }

        return new SessionEndResult(primaryEnded, mode, reflected, memories);
    }

    internal static void StoreSummaryThought(Character character, Conversation conversation, string summary, DateTime nowUtc)
    {
        ConversationThought? existing = character.Memory.Conversations.Thoughts.FirstOrDefault(t => t.ConversationId == conversation.Id);
        if (existing != null)
        {
            existing.ShortSummary = Shorten(summary, 80);
            existing.LongSummary = summary;
        }
        else
        {
            character.Memory.Conversations.AddThought(new ConversationThought
            {
                ConversationId = conversation.Id,
                ShortSummary = Shorten(summary, 80),
                LongSummary = summary,
                Timestamp = nowUtc
            });
        }
        character.Dirty = true;
    }

    private async Task StoreSummaryMemoryAsync(Character character, User user, string summary, DateTime nowUtc)
    {
        float[] embedding = [];
        if (_embeddingService != null)
        {
            try
            {
                embedding = await _embeddingService.GenerateEmbeddingAsync(summary);
            }
            catch
            {
                // The memory is still useful without an embedding; it is found by recency and importance
            }
        }

        await _databaseFactory.GetDatabase<IMemoryDatabase>().SaveDocumentAsync(new MemoryRecord
        {
            CharacterId = character.Id!,
            UserId = user.Id,
            Fact = summary,
            Embedding = embedding,
            Importance = SummaryImportance,
            Category = SessionMemoryCategory,
            Timestamp = nowUtc,
            LastAccessed = nowUtc
        });
    }

    private async Task SaveAsync(SessionScope scope)
    {
        if (scope.Group != null)
        {
            await _databaseFactory.SaveIfDirty(scope.Group);
        }
        foreach (Character character in scope.Characters)
        {
            await _databaseFactory.SaveIfDirty(character);
        }
    }

    private static string Shorten(string text, int length) => text.Length <= length ? text : text[..(length - 3)].TrimEnd() + "...";
}
