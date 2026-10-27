using Akagi.Characters;
using Akagi.Characters.Conversations;
using Akagi.Characters.Groups;
using Akagi.Characters.Upgrades;
using Akagi.Flow;
using Microsoft.Extensions.DependencyInjection;
using System.Text;

namespace Akagi.Communication.Commands.Life;

internal class UpgradesCommand : TextCommand
{
    public override string Name => "/upgrades";

    public override string Description => "Shows the upgrades your companion asked you to build into her. Usage: /upgrades [open|done|rejected|all]";

    public override async Task<CommandResult> ExecuteAsync(Context context, string[] args)
    {
        List<UpgradeProposal> proposals = await context.DatabaseFactory.GetDatabase<IUpgradeProposalDatabase>().GetForUser(context.User.Id!);
        string filter = args.Length > 0 ? args[0].ToLowerInvariant() : "open";
        List<UpgradeProposal> selected = [.. proposals
            .Where(p => filter switch
            {
                "all" => true,
                "done" => p.Status == UpgradeStatus.Done,
                "rejected" => p.Status == UpgradeStatus.Rejected,
                _ => p.IsOpen
            })
            .OrderByDescending(p => p.Priority)
            .ThenByDescending(p => p.Mentions)
            .ThenByDescending(p => p.UpdatedAt)];

        if (selected.Count == 0)
        {
            await Communicator.SendMessage(context.User, $"No {filter} upgrades.");
            return CommandResult.Ok;
        }

        StringBuilder text = new();
        foreach (UpgradeProposal proposal in selected)
        {
            text.AppendLine(proposal.ToString());
            if (!string.IsNullOrWhiteSpace(proposal.Motivation))
            {
                text.AppendLine($"    Why: {proposal.Motivation}");
            }
        }
        text.Append("Change one with /upgrade <id> accept|reject|done|reopen [note].");
        await Communicator.SendMessage(context.User, text.ToString());
        return CommandResult.Ok;
    }
}

internal class UpgradeCommand : TextCommand
{
    public override string Name => "/upgrade";

    public override string Description => "Accept, reject or complete an upgrade your companion asked for. Usage: /upgrade <id> accept|reject|done|reopen [note]";

    public override async Task<CommandResult> ExecuteAsync(Context context, string[] args)
    {
        if (args.Length < 2)
        {
            await Communicator.SendMessage(context.User, Description);
            return CommandResult.Fail("Missing arguments.");
        }

        IUpgradeProposalDatabase database = context.DatabaseFactory.GetDatabase<IUpgradeProposalDatabase>();
        List<UpgradeProposal> proposals = await database.GetForUser(context.User.Id!);
        UpgradeProposal? proposal = proposals.FirstOrDefault(p => string.Equals(p.ShortId, args[0].Trim('[', ']'), StringComparison.OrdinalIgnoreCase)
            || string.Equals(p.Id, args[0], StringComparison.OrdinalIgnoreCase));
        UpgradeStatus? status = ParseStatus(args[1]);
        if (proposal == null || status == null)
        {
            await Communicator.SendMessage(context.User, proposal == null ? $"No upgrade with id '{args[0]}'." : $"Unknown status '{args[1]}'. Use accept, reject, done or reopen.");
            return CommandResult.Fail("Invalid upgrade or status.");
        }

        proposal.Status = status.Value;
        proposal.UpdatedAt = DateTime.UtcNow;
        if (args.Length > 2)
        {
            proposal.DeveloperNote = string.Join(' ', args.Skip(2));
        }
        await database.SaveDocumentAsync(proposal);
        await Communicator.SendMessage(context.User, $"Upgrade [{proposal.ShortId}] {proposal.Title} is now {proposal.Status}.");

        if (status is UpgradeStatus.Done or UpgradeStatus.Rejected or UpgradeStatus.Accepted)
        {
            await TellCompanion(context, proposal);
        }
        return CommandResult.Ok;
    }

    internal static UpgradeStatus? ParseStatus(string text) => text.ToLowerInvariant() switch
    {
        "accept" or "accepted" or "yes" => UpgradeStatus.Accepted,
        "reject" or "rejected" or "no" or "decline" => UpgradeStatus.Rejected,
        "done" or "shipped" or "implemented" => UpgradeStatus.Done,
        "reopen" or "open" or "proposed" => UpgradeStatus.Proposed,
        _ => null
    };

    internal static string BuildNotice(UpgradeProposal proposal)
    {
        string what = proposal.Status switch
        {
            UpgradeStatus.Done => $"just finished building your requested upgrade '{proposal.Title}' into your code",
            UpgradeStatus.Accepted => $"accepted your upgrade request '{proposal.Title}' and plans to build it",
            _ => $"decided not to build your upgrade request '{proposal.Title}'"
        };
        StringBuilder notice = new();
        notice.Append(TextMessage.SystemMessagePrefix).Append("[Upgrade] ");
        notice.Append($"Your developer {what}.");
        if (!string.IsNullOrWhiteSpace(proposal.DeveloperNote))
        {
            notice.Append($" His note: {proposal.DeveloperNote}");
        }
        notice.Append(proposal.Status == UpgradeStatus.Done
            ? " React in your own voice: thank him, and say how you will use it."
            : " React in your own voice, briefly.");
        return notice.ToString();
    }

    private static async Task TellCompanion(Context context, UpgradeProposal proposal)
    {
        Character? character = string.IsNullOrEmpty(proposal.CharacterId)
            ? context.Character
            : await context.DatabaseFactory.GetDatabase<ICharacterDatabase>().GetCharacter(proposal.CharacterId);
        if (character == null)
        {
            return;
        }

        proposal.AcknowledgedAt = DateTime.UtcNow;
        await context.DatabaseFactory.GetDatabase<IUpgradeProposalDatabase>().SaveDocumentAsync(proposal);

        TextMessage message = new()
        {
            From = Message.Type.System,
            Text = BuildNotice(proposal),
            Time = DateTime.UtcNow,
            CharacterId = null
        };
        IProactiveDelivery delivery = Globals.Instance.ServiceProvider.GetRequiredService<IProactiveDelivery>();
        // Delivered in the background: the companion's reply arrives as a normal message
        _ = Task.Run(() => delivery.DeliverAsync(character, context.User, message));
    }
}
