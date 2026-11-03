using Akagi.Bridge.Chat.Models;
using Akagi.Web.Pages.Settings;
using Akagi.Web.Pages.Settings.Components;
using Bunit;

namespace Akagi.Web.Tests;

/// <summary>
/// Finding a model by typing a few letters, and the picker that offers the matches.
/// </summary>
public class ModelSettingsTests : BunitContext
{
    private static LLMModel Model(string id, string name, bool tools = true) => new()
    {
        Provider = "OpenRouter",
        Id = id,
        Name = name,
        ContextLength = 1_048_576,
        PromptPrice = 0.3,
        CompletionPrice = 2.5,
        SupportsTools = tools
    };

    private static readonly LLMModel[] Models =
    [
        Model("meta-llama/llama-4-maverick", "Meta: Llama 4 Maverick"),
        Model("google/gemini-3.5-pro", "Google: Gemini 3.5 Pro"),
        Model("google/gemini-3-flash", "Google: Gemini 3 Flash"),
        Model("google/gemma-4-27b", "Google: Gemma 4 27B", tools: false),
        Model("openai/gpt-6", "OpenAI: GPT-6"),
        Model("nvidia/nemotron-ultra-get", "NVIDIA: Nemotron Ultra")
    ];

    [Fact]
    public void TwoLettersBringUpGeminiBeforeModelsThatMerelyContainThemInTheProvidersOrder()
    {
        IReadOnlyList<LLMModel> found = ModelSearch.Find(Models, "ge");

        Assert.Equal(["google/gemini-3.5-pro", "google/gemini-3-flash", "google/gemma-4-27b", "nvidia/nemotron-ultra-get"],
            found.Select(m => m.Id));
    }

    [Fact]
    public void EveryWordHasToMatch()
    {
        Assert.Equal(["google/gemini-3.5-pro"], ModelSearch.Find(Models, "gem pro").Select(m => m.Id));
        Assert.Empty(ModelSearch.Find(Models, "gem claude"));
        Assert.Empty(ModelSearch.Find(Models, "  "));
    }

    [Fact]
    public void AnExactIdComesFirst()
    {
        Assert.Equal("google/gemini-3-flash", ModelSearch.Find(Models, "GOOGLE/GEMINI-3-FLASH")[0].Id);
    }

    [Fact]
    public void FormattingIsShort()
    {
        Assert.Equal("Text roleplay", ModelSearch.Humanize("TextRoleplay"));
        Assert.Equal("1M", ModelSearch.FormatContext(1_048_576));
        Assert.Equal("128K", ModelSearch.FormatContext(128_000));
        Assert.Equal("$0.3 / $2.5", ModelSearch.FormatPrice(Models[0]));
        Assert.Equal("free", ModelSearch.FormatPrice(new LLMModel { PromptPrice = 0, CompletionPrice = 0 }));
        Assert.Equal(string.Empty, ModelSearch.FormatPrice(new LLMModel()));
    }

    [Fact]
    public void TypingShowsSuggestionsAndEnterPicksTheHighlightedOne()
    {
        LLMModel? picked = null;
        IRenderedComponent<ModelPicker> picker = Render<ModelPicker>(p => p
            .Add(x => x.Models, Models)
            .Add(x => x.NeedsTools, true)
            .Add(x => x.OnPick, (LLMModel m) => picked = m));

        picker.Find("input").Focus();
        picker.Find("input").Input("ge");

        IReadOnlyList<AngleSharp.Dom.IElement> options = picker.FindAll("[role=option]");
        Assert.Equal(4, options.Count);
        Assert.Contains("Gemini 3.5 Pro", options[0].TextContent);
        Assert.Contains("no tools", options[2].TextContent);

        picker.Find("input").KeyDown("ArrowDown");
        picker.Find("input").KeyDown("Enter");

        Assert.Equal("google/gemini-3-flash", picked?.Id);
        Assert.Empty(picker.FindAll("[role=option]"));
    }

    [Fact]
    public void AFullIdThatIsNotListedCanStillBeUsed()
    {
        LLMModel? picked = null;
        IRenderedComponent<ModelPicker> picker = Render<ModelPicker>(p => p
            .Add(x => x.Models, Array.Empty<LLMModel>())
            .Add(x => x.CustomProvider, "OpenRouter")
            .Add(x => x.OnPick, (LLMModel m) => picked = m));

        picker.Find("input").Focus();
        picker.Find("input").Input("vendor/new-model");
        picker.Find("[role=option]").MouseDown();

        Assert.Equal("vendor/new-model", picked?.Id);
        Assert.Equal("OpenRouter", picked?.Provider);
    }
}
