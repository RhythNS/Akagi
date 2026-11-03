using Akagi.Bridge.Chat.Transmissions.Requests;
using Akagi.Bridge.Chat.Transmissions.Responses;
using Akagi.Communication.SocketComs.Transmissions;
using Akagi.LLMs;
using Akagi.LLMs.OpenRouter;
using Akagi.Tests.Support;
using Akagi.Users;
using Microsoft.Extensions.Logging.Abstractions;
using System.Collections.ObjectModel;
using System.Text.Json;

namespace Akagi.Tests.LLMs;

/// <summary>
/// Picking models per usage: the OpenRouter model list, its cache, and storing the choice.
/// </summary>
public class ModelSettingsTests
{
    private sealed class Definitions : InMemoryDatabase<LLMDefinition>, ILLMDefinitionDatabase
    {
        public Definitions() : base("llmDefinitions")
        {
        }

        public async Task<string?> GetDefaultIdAsync() => (await GetDefaultDefinitionAsync())?.Id;

        public Task<LLMDefinition?> GetDefaultDefinitionAsync() => Task.FromResult(Items.FirstOrDefault());

        public Task<LLMDefinition?> GetDefaultDefinitionAsync(ILLM.LLMUsage usage) =>
            Task.FromResult(LLMDefinitionDatabase.PickDefault([.. Items], usage));
    }

    private sealed class FakeCatalog : ILLMModelCatalog
    {
        public int Calls { get; private set; }
        public bool Fail { get; set; }
        public List<LLMModelInfo> Models { get; set; } = [];

        public ILLM.LLMType Type => ILLM.LLMType.OpenRouter;

        public Task<IReadOnlyList<LLMModelInfo>> GetModelsAsync(CancellationToken cancellationToken = default)
        {
            Calls++;
            return Fail
                ? throw new HttpRequestException("down")
                : Task.FromResult<IReadOnlyList<LLMModelInfo>>(Models);
        }
    }

    private static LLMModelInfo Model(string id, bool tools = true) =>
        new(ILLM.LLMType.OpenRouter, id, id, 1_000_000, 0.3m, 2.5m, tools);

    private const string ModelsJson = """
        {
          "data": [
            {
              "id": "google/gemini-3-flash",
              "name": "Google: Gemini 3 Flash",
              "context_length": 1048576,
              "pricing": { "prompt": "0.0000003", "completion": "0.0000025" },
              "supported_parameters": ["temperature", "tools", "tool_choice"]
            },
            {
              "id": "openrouter/auto",
              "name": "",
              "pricing": { "prompt": "-1", "completion": "-1" }
            },
            { "id": "" }
          ]
        }
        """;

    [Fact]
    public void OpenRouterModelsAreReadWithPricesPerMillion()
    {
        OpenRouterModelCatalog.ModelsResponse? body = JsonSerializer.Deserialize<OpenRouterModelCatalog.ModelsResponse>(ModelsJson);

        IReadOnlyList<LLMModelInfo> models = OpenRouterModelCatalog.Parse(body);

        Assert.Equal(2, models.Count);
        LLMModelInfo gemini = models[0];
        Assert.Equal("google/gemini-3-flash", gemini.Id);
        Assert.Equal("Google: Gemini 3 Flash", gemini.Name);
        Assert.Equal(1048576, gemini.ContextLength);
        Assert.Equal(0.3m, gemini.PromptPricePerMillion);
        Assert.Equal(2.5m, gemini.CompletionPricePerMillion);
        Assert.True(gemini.SupportsTools);

        LLMModelInfo auto = models[1];
        Assert.Equal("openrouter/auto", auto.Name);
        Assert.Null(auto.PromptPricePerMillion);
        Assert.False(auto.SupportsTools);
    }

    [Fact]
    public async Task TheModelListIsCachedAndAFailureKeepsTheOldOne()
    {
        DateTime now = new(2026, 11, 3, 12, 0, 0, DateTimeKind.Utc);
        FakeCatalog catalog = new() { Models = [Model("google/gemini-3-flash")] };
        LLMModelDirectory directory = new([catalog], NullLogger<LLMModelDirectory>.Instance, () => now);

        Assert.Single((await directory.GetModelsAsync()).Models);
        await directory.GetModelsAsync();
        Assert.Equal(1, catalog.Calls);

        now += LLMModelDirectory.CacheDuration;
        catalog.Fail = true;
        LLMModelListing stale = await directory.GetModelsAsync();
        Assert.Equal(2, catalog.Calls);
        Assert.Single(stale.Models);
        Assert.Single(stale.Errors);

        // A failed provider is not asked again right away
        await directory.GetModelsAsync();
        Assert.Equal(2, catalog.Calls);

        now += LLMModelDirectory.RetryAfterFailure;
        catalog.Fail = false;
        catalog.Models = [Model("google/gemini-3-flash"), Model("anthropic/claude-sonnet-5.5")];
        LLMModelListing recovered = await directory.GetModelsAsync();
        Assert.Equal(2, recovered.Models.Count);
        Assert.Empty(recovered.Errors);
    }

    private sealed record Setup(Definitions Definitions, InMemoryUserDatabase Users, FakeCatalog Catalog, LLMPreferenceService Service, User User);

    private static async Task<Setup> CreateAsync()
    {
        Definitions definitions = new();
        await definitions.SaveDocumentAsync(new LLMDefinition { Type = ILLM.LLMType.OpenRouter, Model = "google/gemini-2.0-flash-001" });
        await definitions.SaveDocumentAsync(new LLMDefinition { Type = ILLM.LLMType.OpenRouter, Model = LLMDefinitionDatabase.LearningModel });
        string defaultId = (await definitions.GetDefaultIdAsync())!;

        InMemoryUserDatabase users = new();
        User user = new()
        {
            Username = "tester",
            Name = "Tester",
            LLMPreferences = new ReadOnlyDictionary<string, string>(LLMDefinition.CreateDummyDictionary(defaultId))
        };
        await users.SaveDocumentAsync(user);

        FakeCatalog catalog = new() { Models = [Model("google/gemini-3-flash"), Model("google/gemma-4", tools: false)] };
        LLMModelDirectory directory = new([catalog], NullLogger<LLMModelDirectory>.Instance);
        return new Setup(definitions, users, catalog, new LLMPreferenceService(definitions, users, directory), user);
    }

    [Fact]
    public async Task PickingAModelCreatesItsDefinitionOnceAndSavesTheUser()
    {
        Setup setup = await CreateAsync();

        await setup.Service.SetAsync(setup.User, ILLM.LLMUsage.TextRoleplay, ILLM.LLMType.OpenRouter, " google/gemini-3-flash ");
        await setup.Service.SetAsync(setup.User, ILLM.LLMUsage.CommandsRoleplay, ILLM.LLMType.OpenRouter, "google/gemini-3-flash");

        LLMDefinition created = Assert.Single(setup.Definitions.Items, d => d.Model == "google/gemini-3-flash");
        Assert.Equal(3, setup.Definitions.Items.Count());
        Assert.Equal(created.Id, setup.User.LLMPreferences[nameof(ILLM.LLMUsage.TextRoleplay)]);
        Assert.Equal(created.Id, setup.User.LLMPreferences[nameof(ILLM.LLMUsage.CommandsRoleplay)]);
        Assert.False(setup.User.Dirty);

        IReadOnlyList<LLMUsagePreference> preferences = await setup.Service.GetAsync(setup.User);
        Assert.Equal("google/gemini-3-flash", preferences.Single(p => p.Usage == ILLM.LLMUsage.TextRoleplay).Current?.Model);
        Assert.Equal("google/gemini-2.0-flash-001", preferences.Single(p => p.Usage == ILLM.LLMUsage.TextGeneral).Current?.Model);
    }

    [Fact]
    public async Task AModelTheProviderDoesNotListIsRefused()
    {
        Setup setup = await CreateAsync();

        await Assert.ThrowsAsync<LLMPreferenceException>(() =>
            setup.Service.SetAsync(setup.User, ILLM.LLMUsage.TextGeneral, ILLM.LLMType.OpenRouter, "google/gemini-9-ultra"));
        Assert.Equal(2, setup.Definitions.Items.Count());
    }

    [Fact]
    public async Task WithoutAListAnyTypedModelIsAccepted()
    {
        Setup setup = await CreateAsync();
        setup.Catalog.Fail = true;

        await setup.Service.SetAsync(setup.User, ILLM.LLMUsage.TextGeneral, ILLM.LLMType.OpenRouter, "vendor/brand-new");

        Assert.Contains(setup.Definitions.Items, d => d.Model == "vendor/brand-new");
    }

    [Fact]
    public async Task ResettingFallsBackToTheDefault()
    {
        Setup setup = await CreateAsync();
        await setup.Service.SetAsync(setup.User, ILLM.LLMUsage.Learning, ILLM.LLMType.OpenRouter, "google/gemini-3-flash");

        await setup.Service.ResetAsync(setup.User, ILLM.LLMUsage.Learning);

        LLMUsagePreference learning = (await setup.Service.GetAsync(setup.User)).Single(p => p.Usage == ILLM.LLMUsage.Learning);
        Assert.Null(learning.Current);
        Assert.Equal(LLMDefinitionDatabase.LearningModel, learning.Default?.Model);
        Assert.False(setup.User.LLMPreferences.ContainsKey(nameof(ILLM.LLMUsage.Learning)));
    }

    [Fact]
    public async Task TheSocketHandlerSendsModelsAndUsagesAndReportsBadInput()
    {
        Setup setup = await CreateAsync();
        LLMModelDirectory directory = new([setup.Catalog], NullLogger<LLMModelDirectory>.Instance);
        LLMSettingsRequestHandler handler = new(setup.Service, directory, NullLogger<LLMSettingsRequestHandler>.Instance);

        LLMSettingsResponseTransmission overview = await handler.ProcessAsync(setup.User,
            new LLMSettingsRequestTransmission { RequestId = "a", Action = LLMSettingsAction.Overview });
        Assert.Equal("a", overview.RequestId);
        Assert.Null(overview.Error);
        Assert.Equal(2, overview.Models.Length);
        Assert.Equal(Enum.GetValues<ILLM.LLMUsage>().Length, overview.Usages.Length);
        Assert.True(overview.Usages.Single(u => u.Usage == "CommandsGeneral").NeedsTools);
        Assert.Null(overview.Usages.Single(u => u.Usage == "Learning").Model);

        LLMSettingsResponseTransmission set = await handler.ProcessAsync(setup.User, new LLMSettingsRequestTransmission
        {
            Action = LLMSettingsAction.Set,
            Usage = "Learning",
            Provider = "OpenRouter",
            Model = "google/gemini-3-flash"
        });
        Assert.Null(set.Error);
        Assert.Empty(set.Models);
        Assert.Equal("google/gemini-3-flash", set.Usages.Single(u => u.Usage == "Learning").Model);

        LLMSettingsResponseTransmission bad = await handler.ProcessAsync(setup.User, new LLMSettingsRequestTransmission
        {
            Action = LLMSettingsAction.Set,
            Usage = "99",
            Provider = "OpenRouter",
            Model = "google/gemini-3-flash"
        });
        Assert.NotNull(bad.Error);
        Assert.NotEmpty(bad.Usages);
    }
}
