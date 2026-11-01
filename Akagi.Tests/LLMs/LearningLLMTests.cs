using Akagi.Characters.CharacterBehaviors.SystemProcessors;
using Akagi.LLMs;
using Akagi.LLMs.OpenRouter;
using Akagi.Receivers;
using Akagi.Receivers.Commands;
using Akagi.Tests.Support;
using Akagi.Users;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using System.Collections.ObjectModel;

namespace Akagi.Tests.LLMs;

public class LearningLLMTests
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

    private sealed class FakeClient : IOpenRouterClient
    {
        public string? Model { get; private set; }

        public void SetModel(string model) => Model = model;

        public Task<Command[]> GetNextSteps(SystemProcessor systemProcessor, Context context) => throw new NotSupportedException();

        public IAsyncEnumerable<LLMStreamEvent> GetNextStepsStreamAsync(SystemProcessor systemProcessor, Context context, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private readonly Definitions _definitions = new();
    private readonly LLMFactory _factory;

    public LearningLLMTests()
    {
        _factory = new LLMFactory(new ServiceCollection().AddTransient<IOpenRouterClient, FakeClient>().BuildServiceProvider(), _definitions);
    }

    private async Task<LLMDefinition> Seed(string model)
    {
        LLMDefinition definition = new() { Type = ILLM.LLMType.OpenRouter, Model = model };
        await _definitions.SaveDocumentAsync(definition);
        return definition;
    }

    private static User UserWith(Dictionary<string, string> preferences) => new()
    {
        Id = ObjectId.GenerateNewId().ToString(),
        LLMPreferences = new ReadOnlyDictionary<string, string>(preferences)
    };

    private async Task<string?> ModelFor(User user, ILLM.LLMUsage usage) => ((FakeClient)await _factory.Create(user, null, usage)).Model;

    [Fact]
    public async Task Learning_DefaultsToGeminiFlash38ButOthersKeepTheGeneralDefault()
    {
        await Seed("google/gemini-2.0-flash-001");
        await Seed(LLMDefinitionDatabase.LearningModel);
        User user = UserWith([]);

        Assert.Equal("google/gemini-3.8-flash", await ModelFor(user, ILLM.LLMUsage.Learning));
        Assert.Equal("google/gemini-2.0-flash-001", await ModelFor(user, ILLM.LLMUsage.TextGeneral));
    }

    [Fact]
    public async Task NewUsers_GetNoLearningPreference_SoTheLearningDefaultApplies()
    {
        LLMDefinition general = await Seed("google/gemini-2.0-flash-001");
        await Seed(LLMDefinitionDatabase.LearningModel);

        Dictionary<string, string> preferences = LLMDefinition.CreateDummyDictionary(general.Id!);

        Assert.DoesNotContain(nameof(ILLM.LLMUsage.Learning), preferences.Keys);
        Assert.Equal(nameof(ILLM.LLMUsage.TextRoleplay), preferences.Keys.First());
        Assert.Equal("google/gemini-3.8-flash", await ModelFor(UserWith(preferences), ILLM.LLMUsage.Learning));
    }

    [Fact]
    public async Task Learning_FollowsTheUsersPreference()
    {
        await Seed("google/gemini-2.0-flash-001");
        await Seed(LLMDefinitionDatabase.LearningModel);
        LLMDefinition chosen = await Seed("some/other-model");

        User user = UserWith(new() { [nameof(ILLM.LLMUsage.Learning)] = chosen.Id! });

        Assert.Equal("some/other-model", await ModelFor(user, ILLM.LLMUsage.Learning));
    }

    [Fact]
    public async Task Learning_FallsBackWhenItsDefaultWasDeleted()
    {
        await Seed("google/gemini-2.0-flash-001");

        Assert.Equal("google/gemini-2.0-flash-001", await ModelFor(UserWith([]), ILLM.LLMUsage.Learning));
    }

    [Fact]
    public void ExistingUsageIndices_StayStable()
    {
        // /changeLLM and stored graphs address usages by number
        Assert.Equal(5, (int)ILLM.LLMUsage.ReflectionGeneral);
        Assert.Equal(6, (int)ILLM.LLMUsage.Learning);
    }
}
