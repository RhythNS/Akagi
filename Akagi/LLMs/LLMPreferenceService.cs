using Akagi.Users;
using System.Collections.ObjectModel;

namespace Akagi.LLMs;

/// <summary>
/// The model one usage runs on for a user. <see cref="Current"/> is null when the user has no preference, so
/// <see cref="Default"/> applies.
/// </summary>
internal record LLMUsagePreference(ILLM.LLMUsage Usage, string Description, bool NeedsTools, LLMDefinition? Current, LLMDefinition? Default);

internal class LLMPreferenceException(string message) : Exception(message);

internal interface ILLMPreferenceService
{
    public Task<IReadOnlyList<LLMUsagePreference>> GetAsync(User user);

    /// <summary>
    /// Lets <paramref name="usage"/> run on <paramref name="model"/>, creating its definition when needed.
    /// </summary>
    public Task SetAsync(User user, ILLM.LLMUsage usage, ILLM.LLMType type, string model);

    /// <summary>
    /// Drops the preference, so the default model applies again.
    /// </summary>
    public Task ResetAsync(User user, ILLM.LLMUsage usage);
}

internal class LLMPreferenceService : ILLMPreferenceService
{
    private static readonly Dictionary<ILLM.LLMUsage, string> Descriptions = new()
    {
        [ILLM.LLMUsage.TextRoleplay] = "What roleplay characters say.",
        [ILLM.LLMUsage.TextGeneral] = "What assistant characters say.",
        [ILLM.LLMUsage.CommandsRoleplay] = "Tool calls of roleplay characters.",
        [ILLM.LLMUsage.CommandsGeneral] = "Tool calls of assistant characters.",
        [ILLM.LLMUsage.ReflectionRoleplay] = "Background thinking of roleplay characters, such as memory consolidation.",
        [ILLM.LLMUsage.ReflectionGeneral] = "Background analysis of assistant characters.",
        [ILLM.LLMUsage.Learning] = "Writing and grading lessons, and reviewing your recorded calls."
    };

    private readonly ILLMDefinitionDatabase _definitions;
    private readonly IUserDatabase _users;
    private readonly ILLMModelDirectory _models;
    private readonly SemaphoreSlim _createLock = new(1, 1);

    public LLMPreferenceService(ILLMDefinitionDatabase definitions, IUserDatabase users, ILLMModelDirectory models)
    {
        _definitions = definitions;
        _users = users;
        _models = models;
    }

    public static string Describe(ILLM.LLMUsage usage) => Descriptions.GetValueOrDefault(usage, string.Empty);

    public static bool NeedsTools(ILLM.LLMUsage usage) =>
        usage is ILLM.LLMUsage.CommandsRoleplay or ILLM.LLMUsage.CommandsGeneral;

    public async Task<IReadOnlyList<LLMUsagePreference>> GetAsync(User user)
    {
        List<LLMDefinition> definitions = await _definitions.GetDocumentsAsync();
        return [.. Enum.GetValues<ILLM.LLMUsage>().Select(usage =>
        {
            LLMDefinition? current = user.LLMPreferences.TryGetValue(usage.ToString(), out string? id)
                ? definitions.FirstOrDefault(d => d.Id == id)
                : null;
            return new LLMUsagePreference(usage, Describe(usage), NeedsTools(usage), current, LLMDefinitionDatabase.PickDefault(definitions, usage));
        })];
    }

    public async Task SetAsync(User user, ILLM.LLMUsage usage, ILLM.LLMType type, string model)
    {
        model = model.Trim();
        if (model.Length == 0)
        {
            throw new LLMPreferenceException("Pick a model first.");
        }

        LLMModelListing listing = await _models.GetModelsAsync();
        if (listing.Knows(type) && listing.Find(type, model) == null)
        {
            throw new LLMPreferenceException($"{type} has no model called {model}.");
        }

        LLMDefinition definition = await GetOrCreateDefinitionAsync(type, model);
        Dictionary<string, string> preferences = user.LLMPreferences.ToDictionary();
        preferences[usage.ToString()] = definition.Id!;
        user.LLMPreferences = new ReadOnlyDictionary<string, string>(preferences);
        await _users.SaveDocumentAsync(user);
    }

    public async Task ResetAsync(User user, ILLM.LLMUsage usage)
    {
        Dictionary<string, string> preferences = user.LLMPreferences.ToDictionary();
        if (preferences.Remove(usage.ToString()))
        {
            user.LLMPreferences = new ReadOnlyDictionary<string, string>(preferences);
            await _users.SaveDocumentAsync(user);
        }
    }

    private async Task<LLMDefinition> GetOrCreateDefinitionAsync(ILLM.LLMType type, string model)
    {
        await _createLock.WaitAsync();
        try
        {
            LLMDefinition? existing = (await _definitions.GetDocumentsAsync())
                .FirstOrDefault(d => d.Type == type && d.Model == model);
            if (existing != null)
            {
                return existing;
            }

            LLMDefinition created = new() { Type = type, Model = model };
            await _definitions.SaveDocumentAsync(created);
            return created;
        }
        finally
        {
            _createLock.Release();
        }
    }
}
