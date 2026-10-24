using Akagi.Characters.Conversations;
using Akagi.Receivers;
using System.Text;

namespace Akagi.Characters.CharacterBehaviors.MessageCompilers.Injections;

internal class GroupMemberDescriptionInjectionCompiler : InjectionCompiler
{
    public enum RenderMode
    {
        Single,
        PerMember,
    }

    private RenderMode _renderMode = RenderMode.Single;
    private bool _includeName = true;
    private bool _excludeSelf = false;
    private string? _prefix = null;

    public RenderMode Mode
    {
        get => _renderMode;
        set => SetProperty(ref _renderMode, value);
    }

    public bool IncludeName
    {
        get => _includeName;
        set => SetProperty(ref _includeName, value);
    }

    public bool ExcludeSelf
    {
        get => _excludeSelf;
        set => SetProperty(ref _excludeSelf, value);
    }

    public string? Prefix
    {
        get => _prefix;
        set => SetProperty(ref _prefix, value);
    }

    protected override async Task<Message[]> GetInjectionMessagesAsync(Context context)
    {
        if (context.Group == null || context.Group.MemberCharacterIds.Length == 0)
        {
            return [];
        }

        ICharacterDatabase database = context.DatabaseFactory.GetDatabase<ICharacterDatabase>();

        var characterTasks = context.Group.MemberCharacterIds
            .Select(id => database.GetCharacter(id));
        var loadedMembers = await Task.WhenAll(characterTasks);
        IEnumerable<Character> members = loadedMembers.Where(c => c != null)!;

        if (ExcludeSelf)
        {
            members = members.Where(m => m.Id != context.Character.Id);
        }

        Character[] filtered = [.. members.Where(m => !string.IsNullOrWhiteSpace(m.Description))];

        if (filtered.Length == 0)
        {
            return [];
        }

        switch (Mode)
        {
            case RenderMode.Single:
                StringBuilder sb = new();
                if (!string.IsNullOrWhiteSpace(Prefix))
                {
                    sb.AppendLine(Prefix);
                }
                foreach (Character member in filtered)
                {
                    sb.AppendLine(IncludeName ? $"{member.Name}: {member.Description}" : member.Description);
                }
                return
                [
                    new TextMessage
                    {
                        From = MessageType,
                        Time = DateTime.UtcNow,
                        Text = sb.ToString().TrimEnd(),
                        CharacterId = context.Character.Id!,
                    }
                ];

            case RenderMode.PerMember:
                List<Message> messages = [];
                if (!string.IsNullOrWhiteSpace(Prefix))
                {
                    messages.Add(new TextMessage
                    {
                        From = MessageType,
                        Time = DateTime.UtcNow,
                        Text = Prefix,
                        CharacterId = context.Character.Id!,
                    });
                }
                foreach (Character member in filtered)
                {
                    messages.Add(new TextMessage
                    {
                        From = MessageType,
                        Time = DateTime.UtcNow,
                        Text = IncludeName ? $"{member.Name}: {member.Description}" : member.Description,
                        CharacterId = context.Character.Id!,
                    });
                }
                return [.. messages];

            default:
                throw new ArgumentException($"Unknown RenderMode: {Mode}");
        }
    }
}
