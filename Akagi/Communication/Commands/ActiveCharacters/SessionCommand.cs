using Akagi.Characters;
using Akagi.Characters.Conversations;
using Akagi.Embeddings;
using Akagi.Flow;
using Akagi.Receivers;
using Microsoft.Extensions.DependencyInjection;
using System.Globalization;

namespace Akagi.Communication.Commands.ActiveCharacters;

internal class SessionCommand : TextCommand
{
    public override string Name => "/session";

    public override string Description =>
        "Explicit conversations. Usage: /session start [topic] | /session end [reflect|incognito|summary <text>] | /session (status). " +
        "Incognito keeps the chat visible but nothing is remembered; summary stores your text instead of an automatic reflection.";

    public override async Task<CommandResult> ExecuteAsync(Context context, string[] args)
    {
        if (context.Character == null && context.Group == null)
        {
            await Communicator.SendMessage(context.User, "You need an active character or group to use sessions.");
            return CommandResult.Fail("No active character or group.");
        }

        IServiceProvider services = Globals.Instance.ServiceProvider;
        IReceiver receiver = services.GetRequiredService<IReceiver>();
        SessionManager sessions = new(receiver, context.DatabaseFactory, services.GetService<IEmbeddingService>());
        string verb = args.Length > 0 ? args[0].ToLowerInvariant() : "status";

        string? message = null;
        if (context.Group != null)
        {
            // Like a group turn: under the group's lock, so reflections read the group's conversation with the user's messages
            bool ran = await receiver.TryRunForGroupAsync(context.Group, context.User, async group =>
                message = await Run(sessions, await SessionScope.ForGroup(group, context.DatabaseFactory.GetDatabase<ICharacterDatabase>()), context, verb, args));
            message ??= ran ? null : "The group is busy with a reply. Try again in a moment.";
        }
        else
        {
            message = await Run(sessions, SessionScope.ForCharacter(context.Character!), context, verb, args);
        }

        await Communicator.SendMessage(context.User, message ?? "The active group no longer exists.");
        return CommandResult.Ok;
    }

    private async Task<string> Run(SessionManager sessions, SessionScope scope, Context context, string verb, string[] args)
    {
        if (scope.Characters.Count == 0)
        {
            return "The active group has no members.";
        }
        return verb switch
        {
            "start" => await Start(sessions, scope, context, string.Join(' ', args.Skip(1))),
            "end" or "stop" => await End(sessions, scope, context, args.Skip(1).ToArray()),
            "status" => Status(scope),
            _ => $"Unknown option '{args[0]}'. {Description}"
        };
    }

    internal static string Status(SessionScope scope)
    {
        Conversation? session = scope.Primary.ActiveSession;
        if (session == null)
        {
            Conversation? current = scope.Primary.Current;
            return current == null
                ? "No session is running."
                : $"No session is running. The current conversation has {current.Messages.Count} message(s); /session start [topic] begins one.";
        }
        string topic = string.IsNullOrWhiteSpace(session.SessionTopic) ? "no topic" : $"topic: {session.SessionTopic}";
        return $"Session running since {session.Time.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)} UTC ({topic}), {session.Messages.Count} message(s). " +
               "End it with /session end [reflect|incognito|summary <text>].";
    }

    private static async Task<string> Start(SessionManager sessions, SessionScope scope, Context context, string topic)
    {
        if (scope.Primary.ActiveSession != null)
        {
            return "A session is already running. End it first with /session end [reflect|incognito|summary <text>].";
        }

        await sessions.StartAsync(scope, context.User, topic.Trim(), DateTime.UtcNow);
        return string.IsNullOrWhiteSpace(topic)
            ? "Session started. It stays open until you end it with /session end."
            : $"Session '{topic.Trim()}' started. It stays open until you end it with /session end.";
    }

    private static async Task<string> End(SessionManager sessions, SessionScope scope, Context context, string[] args)
    {
        ConversationEndMode? mode = SessionManager.ParseMode(args.Length > 0 ? args[0] : null);
        if (mode == null)
        {
            return $"Unknown end mode '{args[0]}'. Use reflect, incognito or summary <text>.";
        }

        string? summary = mode == ConversationEndMode.CustomSummary ? string.Join(' ', args.Skip(1)).Trim() : null;
        if (mode == ConversationEndMode.CustomSummary && string.IsNullOrWhiteSpace(summary))
        {
            return "Please add your summary: /session end summary <text>.";
        }

        SessionEndResult? result = await sessions.EndAsync(scope, context.User, mode.Value, summary, DateTime.UtcNow);
        if (result == null)
        {
            return "Nothing is running to end.";
        }

        string name = result.Ended.IsSession ? "Session" : "Conversation";
        return result.Mode switch
        {
            ConversationEndMode.Incognito => $"{name} ended incognito. It stays in the history, but nothing from it will be remembered.",
            ConversationEndMode.CustomSummary => $"{name} ended. Your summary was stored as the memory of it.",
            _ => $"{name} ended and reflected ({result.Reflected} character(s))."
        };
    }
}
