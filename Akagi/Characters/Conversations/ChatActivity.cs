using Akagi.Bridge.Chat.Models;
using Akagi.Users;
using System.Collections.Concurrent;
using static Akagi.Bridge.Chat.Transmissions.Responses.ChatActivityResponseTransmission;

namespace Akagi.Characters.Conversations;

/// <summary>
/// Live progress of the chats: a turn started, a step (any message, tool calls included) was added, the turn ended.
/// The website listens, so the user can watch the characters work instead of waiting in silence.
/// </summary>
internal interface IChatActivityFeed
{
    /// <summary>Raised for every activity; <c>Message</c> is set for steps only.</summary>
    event Action<User, string, ActivityKind, Message?>? Published;

    /// <summary>Starts reporting a turn of <paramref name="chatId"/> (a character or group id) on this async flow.</summary>
    IDisposable BeginTurn(User user, string chatId);

    /// <summary>Whether a turn of the chat is running right now.</summary>
    bool IsBusy(string userId, string chatId);
}

internal sealed class ChatActivityHub : IChatActivityFeed
{
    private static readonly AsyncLocal<Scope?> Current = new();
    private static readonly AsyncLocal<int> MuteDepth = new();

    private readonly ConcurrentDictionary<(string User, string Chat), int> _running = new();

    public event Action<User, string, ActivityKind, Message?>? Published;

    public IDisposable BeginTurn(User user, string chatId)
    {
        Scope? outer = Current.Value;
        // A reflection or consultation inside a turn is part of that turn
        if (outer != null && outer.ChatId == chatId && outer.User.Id == user.Id)
        {
            return NoScope.Instance;
        }

        Scope scope = new(this, user, chatId, outer);
        Current.Value = scope;
        _running.AddOrUpdate((user.Id ?? string.Empty, chatId), 1, (_, count) => count + 1);
        Publish(user, chatId, ActivityKind.TurnStarted, null);
        return scope;
    }

    public bool IsBusy(string userId, string chatId) => _running.TryGetValue((userId, chatId), out int count) && count > 0;

    /// <summary>
    /// Reports a message added on this async flow to the turn running there, if any.
    /// </summary>
    internal static void Report(Message message)
    {
        if (Current.Value is Scope scope && MuteDepth.Value == 0)
        {
            scope.Hub.Publish(scope.User, scope.ChatId, ActivityKind.Step, message);
        }
    }

    /// <summary>
    /// Messages added until disposed are not reported, e.g. the members' copies of a group message.
    /// </summary>
    internal static IDisposable Mute()
    {
        MuteDepth.Value++;
        return new MuteScope();
    }

    private void Publish(User user, string chatId, ActivityKind kind, Message? message)
    {
        try
        {
            Published?.Invoke(user, chatId, kind, message);
        }
        catch
        {
            // Showing progress must never break a turn
        }
    }

    private void End(Scope scope)
    {
        _running.AddOrUpdate((scope.User.Id ?? string.Empty, scope.ChatId), 0, (_, count) => Math.Max(0, count - 1));
        Publish(scope.User, scope.ChatId, ActivityKind.TurnEnded, null);
    }

    private sealed class Scope(ChatActivityHub hub, User user, string chatId, Scope? outer) : IDisposable
    {
        private bool _disposed;

        public ChatActivityHub Hub { get; } = hub;
        public User User { get; } = user;
        public string ChatId { get; } = chatId;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            Current.Value = outer;
            Hub.End(this);
        }
    }

    private sealed class MuteScope : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                MuteDepth.Value--;
            }
        }
    }

    private sealed class NoScope : IDisposable
    {
        public static readonly NoScope Instance = new();

        public void Dispose()
        {
        }
    }
}

/// <summary>
/// How stored messages look on the website: what was said, and every tool call with its arguments and result.
/// </summary>
internal static class BridgeMessages
{
    public const int MaxOutputLength = 4000;
    public const int MaxArgumentLength = 2000;

    public static Bridge.Chat.Models.TextMessage? From(Message message)
    {
        Bridge.Chat.Models.TextMessage? bridge = message switch
        {
            TextMessage text => new Bridge.Chat.Models.TextMessage { Text = text.Text },
            VoiceMessage voice => new Bridge.Chat.Models.TextMessage
            {
                Text = string.IsNullOrWhiteSpace(voice.Content) ? "(voice message)" : voice.Content
            },
            CommandMessage command => new Bridge.Chat.Models.TextMessage
            {
                Tool = new ToolCall
                {
                    Name = command.Command.Name,
                    Arguments = [.. (command.Command.Arguments ?? [])
                        .Where(a => !string.IsNullOrWhiteSpace(a.Value))
                        .Select(a => new ToolArgument { Name = a.Name, Value = Shorten(a.Value, MaxArgumentLength) })],
                    Output = Shorten(command.Output, MaxOutputLength)
                }
            },
            _ => null
        };
        if (bridge == null)
        {
            return null;
        }
        bridge.Time = message.Time;
        bridge.From = Message.ToBridgeType(message.From);
        bridge.CharacterId = message.CharacterId;
        bridge.Internal = message.Internal;
        return bridge;
    }

    private static string Shorten(string? text, int length)
    {
        text ??= string.Empty;
        return text.Length <= length ? text : text[..length] + $"… ({text.Length - length} more characters)";
    }
}
