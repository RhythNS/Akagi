using Akagi.Bridge.Attributes;
using Akagi.Characters.Conversations;
using Akagi.Life;
using Akagi.Receivers;

namespace Akagi.Characters.CharacterBehaviors.MessageCompilers.Injections;

/// <summary>
/// Injects what the companion knows about the user's life right now (local time, mood, worries, sleep, work,
/// study progress) together with tone guidance derived from it.
/// </summary>
[NodeDocu("Injects the user's current life context (time, mood, worries, sleep, work, study) and tone guidance.")]
internal class LifeContextInjectionCompiler : InjectionCompiler
{
    private LifeSections _sections = LifeSections.All;
    private string _header = "### What you know about the user's life right now";

    [NodeDocu("Which parts of the life context to include.")]
    public LifeSections Sections
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
        if (string.IsNullOrEmpty(context.User.Id))
        {
            return [];
        }

        LifeProfile? profile = await context.DatabaseFactory.GetDatabase<ILifeProfileDatabase>().GetForUser(context.User.Id);
        profile ??= new LifeProfile { UserId = context.User.Id };

        string body = LifeContextRenderer.Render(profile, DateTime.UtcNow, Sections);
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
