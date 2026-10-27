using Akagi.Bridge.Attributes;
using Akagi.Characters.Conversations;
using Akagi.Life.Japanese;
using Akagi.Receivers;

namespace Akagi.Characters.CharacterBehaviors.MessageCompilers.Injections;

/// <summary>
/// Injects the user's long-term Japanese diagnosis so the tutor keeps working on the same weak spots across conversations.
/// </summary>
[NodeDocu("Injects the user's Japanese diagnosis: focus areas, improvements, strengths and speech trends.")]
internal class JapaneseDiagnosisInjectionCompiler : InjectionCompiler
{
    private int _focusCount = 5;
    private bool _includeExamples = true;
    private bool _includeGrammarGroups;
    private string _header = "### {{user}}'s Japanese diagnosis";

    [NodeDocu("How many focus areas to show.")]
    public int FocusCount
    {
        get => _focusCount;
        set => SetProperty(ref _focusCount, value);
    }
    public bool IncludeExamples
    {
        get => _includeExamples;
        set => SetProperty(ref _includeExamples, value);
    }
    [NodeDocu("Also list the N2 grammar nuance groups learners confuse, as a checklist for the tutor.")]
    public bool IncludeGrammarGroups
    {
        get => _includeGrammarGroups;
        set => SetProperty(ref _includeGrammarGroups, value);
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

        JapaneseDiagnosis? diagnosis = await context.DatabaseFactory.GetDatabase<IJapaneseDiagnosisDatabase>().GetForUser(context.User.Id);
        string body = Render(diagnosis, DateTime.UtcNow, FocusCount, IncludeExamples, IncludeGrammarGroups);
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

    internal static string Render(JapaneseDiagnosis? diagnosis, DateTime nowUtc, int focusCount, bool includeExamples, bool includeGrammarGroups)
    {
        List<string> parts = [];
        if (diagnosis is { HasData: true })
        {
            parts.Add(diagnosis.Describe(nowUtc, Math.Max(1, focusCount), includeExamples));
        }
        if (includeGrammarGroups)
        {
            parts.Add("N2 nuance groups to watch:" + Environment.NewLine + string.Join(Environment.NewLine, GrammarPatterns.DescribeGroups().Select(g => "- " + g)));
        }
        return string.Join(Environment.NewLine, parts);
    }
}
