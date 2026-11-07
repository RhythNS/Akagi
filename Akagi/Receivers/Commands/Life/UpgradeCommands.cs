using Akagi.Characters.Upgrades;

namespace Akagi.Receivers.Commands.Life;

internal class SuggestUpgradeCommand : LifeCommand, IStateCommand
{
    public const double DuplicateSimilarity = 0.75;

    public override string Name => "SuggestUpgrade";

    public override string Description =>
        "File a concrete feature request for your own code in your developer's backlog: a new module, tool or behavior " +
        "you need to serve the user better. Be specific about what it does. Asking again for the same idea raises its weight.";

    public override Argument[] GetDefaultArguments() =>
    [
        Text("Title", "Short name of the upgrade, e.g. 'Movie night sync module'.", required: true),
        Text("Description", "What it should do, concretely enough to implement.", required: true),
        Text("Motivation", "Why you want it: what you noticed about the user that made you think of it."),
        Text("Priority", "low, medium or high."),
        Text("Area", "Part of you it touches, e.g. memory, voice, health, japanese, routines, appearance.")
    ];

    public override async Task<Command[]> Execute(Context context)
    {
        string title = RequireArgument("Title");
        string description = RequireArgument("Description");
        string userId = context.User.Id ?? throw new InvalidOperationException("User has no id.");
        IUpgradeProposalDatabase database = context.DatabaseFactory.GetDatabase<IUpgradeProposalDatabase>();

        List<UpgradeProposal> existing = await database.GetForUser(userId);
        UpgradeProposal? duplicate = existing
            .Where(p => p.IsOpen)
            .Select(p => (Proposal: p, Score: UpgradeProposal.Similarity(p.Title, title)))
            .Where(p => p.Score >= DuplicateSimilarity)
            .OrderByDescending(p => p.Score)
            .Select(p => p.Proposal)
            .FirstOrDefault();

        if (duplicate != null)
        {
            duplicate.Mentions++;
            duplicate.UpdatedAt = DateTime.UtcNow;
            if (ParsePriority(GetArgument("Priority")) is UpgradePriority raised && raised > duplicate.Priority)
            {
                duplicate.Priority = raised;
            }
            if (GetArgument("Motivation") is string motivation && !duplicate.Motivation.Contains(motivation, StringComparison.OrdinalIgnoreCase))
            {
                duplicate.Motivation = string.IsNullOrWhiteSpace(duplicate.Motivation) ? motivation : $"{duplicate.Motivation} | {motivation}";
            }
            await database.SaveDocumentAsync(duplicate);
            return Reply(context, $"Already in the backlog as [{duplicate.ShortId}] {duplicate.Title} ({duplicate.Status}); you have asked for it {duplicate.Mentions} times now.");
        }

        UpgradeProposal proposal = new()
        {
            UserId = userId,
            CharacterId = context.Character.Id ?? string.Empty,
            Title = title,
            Description = description,
            Motivation = GetArgument("Motivation") ?? string.Empty,
            Priority = ParsePriority(GetArgument("Priority")) ?? UpgradePriority.Medium,
            Area = GetArgument("Area") ?? string.Empty,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        await database.SaveDocumentAsync(proposal);
        return Reply(context, $"Filed upgrade [{proposal.ShortId}] {proposal.Title} ({proposal.Priority}). The user can review it with /upgrades.");
    }

    internal static UpgradePriority? ParsePriority(string? text) => text?.Trim().ToLowerInvariant() switch
    {
        "low" or "nice to have" => UpgradePriority.Low,
        "medium" or "normal" => UpgradePriority.Medium,
        "high" or "urgent" or "important" => UpgradePriority.High,
        _ => null
    };
}

internal class ListUpgradesCommand : LifeCommand
{
    public override string Name => "ListUpgrades";

    public override string Description => "See your upgrade backlog: what you asked your developer for and what he already built or declined.";

    public override Argument[] GetDefaultArguments() =>
    [
        Text("Status", "open (default), done, rejected or all.")
    ];

    public override async Task<Command[]> Execute(Context context)
    {
        List<UpgradeProposal> proposals = await context.DatabaseFactory.GetDatabase<IUpgradeProposalDatabase>().GetForUser(context.User.Id!);
        string filter = GetArgument("Status")?.ToLowerInvariant() ?? "open";
        IEnumerable<UpgradeProposal> selected = filter switch
        {
            "all" => proposals,
            "done" => proposals.Where(p => p.Status == UpgradeStatus.Done),
            "rejected" => proposals.Where(p => p.Status == UpgradeStatus.Rejected),
            _ => proposals.Where(p => p.IsOpen)
        };

        List<string> lines = [.. selected.OrderByDescending(p => p.Priority).ThenByDescending(p => p.UpdatedAt).Select(p => p.ToString())];
        return Reply(context, lines.Count == 0 ? $"No {filter} upgrades." : string.Join(Environment.NewLine, lines));
    }
}
