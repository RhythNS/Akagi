using Akagi.Bridge.Attributes;
using Akagi.Characters.Companions;
using Akagi.Characters.Conversations;
using Akagi.Characters.Upgrades;
using Akagi.Life;
using Akagi.Receivers;

namespace Akagi.Characters.CharacterBehaviors.MessageCompilers.Injections;

/// <summary>
/// Injects the companion's own state: outfit, the activity she shares with the user, the people she knows from his
/// stories, and her upgrade history.
/// </summary>
[NodeDocu("Injects the companion's outfit, shared activity, known people (with playful jealousy cues) and upgrade history.")]
internal class CompanionStateInjectionCompiler : InjectionCompiler
{
    private CompanionSections _sections = CompanionSections.All;
    private string _header = "### Your own state";

    public CompanionSections Sections
    {
        get => _sections;
        set => SetProperty(ref _sections, value);
    }
    [NodeDocu("Heading above the injected block. {{user}} and {{character}} are replaced with names.")]
    public string Header
    {
        get => _header;
        set => SetProperty(ref _header, value);
    }

    protected override async Task<Message[]> GetInjectionMessagesAsync(Context context)
    {
        if (string.IsNullOrEmpty(context.User.Id) || string.IsNullOrEmpty(context.Character.Id))
        {
            return [];
        }

        CompanionState? state = await context.DatabaseFactory.GetDatabase<ICompanionStateDatabase>().Get(context.User.Id, context.Character.Id);
        List<UpgradeProposal> upgrades = Sections.HasFlag(CompanionSections.Upgrades)
            ? await context.DatabaseFactory.GetDatabase<IUpgradeProposalDatabase>().GetForUser(context.User.Id)
            : [];
        LifeProfile? profile = await context.DatabaseFactory.GetDatabase<ILifeProfileDatabase>().GetForUser(context.User.Id);

        string body = CompanionContextRenderer.Render(state, upgrades, profile, DateTime.UtcNow, Sections);
        if (string.IsNullOrWhiteSpace(body))
        {
            return [];
        }

        string header = Header
            .Replace("{{user}}", context.User.Name)
            .Replace("{{character}}", context.Character.Name);
        return
        [
            new TextMessage
            {
                From = MessageType,
                Time = DateTime.UtcNow,
                Text = string.IsNullOrWhiteSpace(header) ? body : header + Environment.NewLine + body,
                CharacterId = context.Character.Id
            }
        ];
    }
}
