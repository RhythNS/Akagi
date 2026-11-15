using Akagi.Characters.Groups;
using Akagi.Data;
using Akagi.Embeddings;
using Akagi.Receivers;
using Akagi.Users;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Akagi.Characters.Conversations;

/// <summary>
/// Ends a conversation the usual way, with reflection, once the user has not written in it for <see cref="Gap"/>.
/// Only the user's messages count: check-ins the companion sends on its own neither keep a conversation open nor make
/// one that has nothing from the user end. Sessions the user started stay open until the user ends them.
/// </summary>
internal class QuietConversationCloser
{
    public static readonly TimeSpan Gap = TimeSpan.FromHours(7);

    private readonly IUserDatabase _users;
    private readonly ICharacterDatabase _characters;
    private readonly ICharacterGroupDatabase _groups;
    private readonly IReceiver _receiver;
    private readonly SessionManager _sessions;
    private readonly ILogger _logger;

    public QuietConversationCloser(IUserDatabase users,
                                   ICharacterDatabase characters,
                                   ICharacterGroupDatabase groups,
                                   IReceiver receiver,
                                   IDatabaseFactory databaseFactory,
                                   ILogger logger,
                                   IEmbeddingService? embeddingService = null)
    {
        _users = users;
        _characters = characters;
        _groups = groups;
        _receiver = receiver;
        _sessions = new SessionManager(receiver, databaseFactory, embeddingService);
        _logger = logger;
    }

    /// <summary>
    /// True when the running conversation has a message from the user and the latest one is at least
    /// <see cref="Gap"/> old.
    /// </summary>
    public static bool IsQuiet(Conversation? conversation, DateTime nowUtc)
    {
        if (conversation == null || conversation.IsCompleted || conversation.IsSession)
        {
            return false;
        }
        DateTime? lastUser = null;
        foreach (Message message in conversation.Messages)
        {
            if (message.From == Message.Type.User && (lastUser == null || message.Time > lastUser))
            {
                lastUser = message.Time;
            }
        }
        return lastUser != null && nowUtc - lastUser.Value >= Gap;
    }

    /// <summary>
    /// Ends every quiet conversation; returns how many were ended. A group or character that is busy with a turn is
    /// left for the next tick.
    /// </summary>
    public async Task<int> TickAsync(DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        int ended = 0;
        foreach (User user in await _users.GetDocumentsAsync())
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                ended += await TickUserAsync(user, nowUtc);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ending quiet conversations failed for user {UserId}", user.Id);
            }
        }
        return ended;
    }

    private async Task<int> TickUserAsync(User user, DateTime nowUtc)
    {
        int ended = 0;
        List<CharacterGroup> groups = await _groups.GetGroupsForUser(user);
        HashSet<string> grouped = [.. groups.SelectMany(g => g.MemberCharacterIds)];

        foreach (CharacterGroup group in groups.Where(g => IsQuiet(g.Conversations.Current, nowUtc)))
        {
            bool done = false;
            await _receiver.TryRunForGroupAsync(group, user, async fresh =>
            {
                // The user may have written since the group was listed
                if (IsQuiet(fresh.Conversations.Current, nowUtc))
                {
                    done = await EndAsync(await SessionScope.ForGroup(fresh, _characters), user, nowUtc);
                }
            });
            ended += done ? 1 : 0;
        }

        foreach (Character character in await _characters.GetCharactersForUser(user))
        {
            // Group members end together with their group
            if (grouped.Contains(character.Id!) || !IsQuiet(character.Conversations.Current, nowUtc))
            {
                continue;
            }
            bool done = false;
            await _receiver.TryRunForCharacterAsync(character, user, async fresh =>
            {
                if (IsQuiet(fresh.Conversations.Current, nowUtc))
                {
                    done = await EndAsync(SessionScope.ForCharacter(fresh), user, nowUtc);
                }
            });
            ended += done ? 1 : 0;
        }
        return ended;
    }

    private async Task<bool> EndAsync(SessionScope scope, User user, DateTime nowUtc)
    {
        SessionEndResult? result = await _sessions.EndAsync(scope, user, ConversationEndMode.Reflect, null, nowUtc);
        if (result == null)
        {
            return false;
        }
        _logger.LogInformation("Ended conversation {ConversationId} ({Owner}) for user {UserId} after {Gap} without a message from the user; {Reflected} character(s) reflected",
            result.Ended.Id, scope.Group?.Name ?? scope.Characters[0].Name, user.Id, Gap, result.Reflected);
        return true;
    }
}

internal class QuietConversationService : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<QuietConversationService> _logger;

    public QuietConversationService(IServiceProvider serviceProvider, ILogger<QuietConversationService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        QuietConversationCloser closer = new(
            _serviceProvider.GetRequiredService<IUserDatabase>(),
            _serviceProvider.GetRequiredService<ICharacterDatabase>(),
            _serviceProvider.GetRequiredService<ICharacterGroupDatabase>(),
            _serviceProvider.GetRequiredService<IReceiver>(),
            _serviceProvider.GetRequiredService<IDatabaseFactory>(),
            _logger,
            _serviceProvider.GetService<IEmbeddingService>());

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(Interval, stoppingToken);
            try
            {
                await closer.TickAsync(DateTime.UtcNow, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Quiet conversation check failed");
            }
        }
    }
}
