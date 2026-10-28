using AngleSharp.Dom;
using Akagi.Bridge.Chat.Models;
using Akagi.Web.Pages.Chat.Components;
using Bunit;

namespace Akagi.Web.Tests;

/// <summary>
/// The chat's command palette: find a command, fill in its form, and get the command line it writes.
/// </summary>
public class CommandPaletteTests : BunitContext
{
    private readonly List<string> _ran = [];
    private readonly List<string> _edited = [];

    public CommandPaletteTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    private static Command Make(string name, string category, string usage, bool acceptsFiles = false) => new()
    {
        Name = name,
        Description = "Does " + name + ". Usage: " + usage,
        Summary = "Does " + name + ".",
        Category = category,
        AcceptsFiles = acceptsFiles,
        Forms = CommandUsage.Parse(name, usage, acceptsFiles)
    };

    private static readonly Command[] Commands =
    [
        Make("/ping", "Systems", "/ping"),
        Make("/upgrade", "Life", "/upgrade <id> accept|reject|done|reopen [note]"),
        Make("/upgrades", "Life", "/upgrades [open|done|rejected|all]"),
        Make("/session", "ActiveCharacters", "/session start [topic] | /session end [reflect|incognito|summary <text>] | /session (status)"),
        Make("/uploadGraph", "Savables", "/uploadGraph <json files> create|update [name] [--all]", acceptsFiles: true),
    ];

    private IRenderedComponent<CommandPalette> Render(string filter = "", bool fromComposer = false, string? attached = null, string[]? recent = null) =>
        Render<CommandPalette>(p => p
            .Add(c => c.Commands, Commands)
            .Add(c => c.Filter, filter)
            .Add(c => c.FromComposer, fromComposer)
            .Add(c => c.Visible, true)
            .Add(c => c.AttachedFileName, attached)
            .Add(c => c.Recent, recent ?? [])
            .Add(c => c.OnRun, line => _ran.Add(line))
            .Add(c => c.OnEditAsText, line => _edited.Add(line)));

    private static IElement Button(IRenderedComponent<CommandPalette> cut, string text) =>
        cut.FindAll("button").First(b => b.TextContent.Trim() == text);

    private static IElement RunButton(IRenderedComponent<CommandPalette> cut) => cut.Find("[data-command-run]");

    private static IElement Field(IRenderedComponent<CommandPalette> cut, string label)
    {
        IElement container = cut.FindAll("label.form-label").First(l => l.TextContent.Trim().StartsWith(label, StringComparison.Ordinal)).ParentElement!;
        return container.QuerySelector("input, textarea")!;
    }

    private static List<string> ListedNames(IRenderedComponent<CommandPalette> cut) =>
        [.. cut.FindAll(".command-list button code").Select(c => c.TextContent)];

    [Fact]
    public void TypedSlash_ListsMatchingCommandsBestFirst()
    {
        IRenderedComponent<CommandPalette> cut = Render("/upg", fromComposer: true);

        Assert.Equal(["/upgrade", "/upgrades"], ListedNames(cut));
        Assert.Contains("active", cut.Find(".command-list button").ClassList);
    }

    [Fact]
    public void NoFilter_ShowsRecentFirstThenEveryCategory()
    {
        IRenderedComponent<CommandPalette> cut = Render(recent: ["/session"]);

        List<string> headers = [.. cut.FindAll(".list-group-header").Select(h => h.TextContent)];
        Assert.Equal("Recent", headers[0]);
        Assert.Equal("/session", ListedNames(cut)[0]);
        Assert.Contains("Savables", headers);
    }

    [Fact]
    public void SearchBox_FiltersAndEnterOpensTheForm()
    {
        IRenderedComponent<CommandPalette> cut = Render();

        cut.Find("input").Input("sess");
        Assert.Equal(["/session"], ListedNames(cut));
        cut.Find("input").KeyDown(Key.Enter);

        Assert.True(cut.Instance.IsFormOpen);
        Assert.Contains("/session", cut.Find(".card-header code").TextContent);
    }

    [Fact]
    public async Task FilledForm_RunsTheCommandLine()
    {
        IRenderedComponent<CommandPalette> cut = Render("/upgrade", fromComposer: true);
        await cut.InvokeAsync(() => cut.Instance.OpenByNameAsync("/upgrade"));

        Assert.True(RunButton(cut).HasAttribute("disabled"));
        Assert.Contains("Needs", cut.Markup);

        Field(cut, "id").Input("4");
        Button(cut, "done").Click();
        Field(cut, "note").Input("works now");
        Assert.Equal("/upgrade 4 done {{works now}}", cut.Find(".command-preview").TextContent);

        RunButton(cut).Click();

        Assert.Equal(["/upgrade 4 done {{works now}}"], _ran);
        Assert.False(cut.Instance.IsFormOpen);
    }

    [Fact]
    public async Task EnterInAField_Runs()
    {
        IRenderedComponent<CommandPalette> cut = Render();
        await cut.InvokeAsync(() => cut.Instance.OpenByNameAsync("/upgrade"));
        Field(cut, "id").Input("2");
        Button(cut, "accept").Click();

        Field(cut, "id").KeyDown(Key.Enter);

        Assert.Equal(["/upgrade 2 accept"], _ran);
    }

    [Fact]
    public async Task Forms_AndOptionParameters()
    {
        IRenderedComponent<CommandPalette> cut = Render();
        await cut.InvokeAsync(() => cut.Instance.OpenByNameAsync("/session"));

        Button(cut, "end [reflect|incognito|summary <text>]").Click();
        Assert.Equal("/session end", cut.Find(".command-preview").TextContent);
        Assert.Empty(cut.FindAll("input:not([type=checkbox])"));

        Button(cut, "summary").Click();
        Field(cut, "text").Input("we fixed the bug");

        Assert.Equal("/session end summary {{we fixed the bug}}", cut.Find(".command-preview").TextContent);

        // Clicking the picked option of an optional choice clears it again, with its parameters
        Button(cut, "summary").Click();
        Assert.Equal("/session end", cut.Find(".command-preview").TextContent);
    }

    [Fact]
    public async Task FileCommand_WaitsForTheAttachment()
    {
        IRenderedComponent<CommandPalette> cut = Render();
        await cut.InvokeAsync(() => cut.Instance.OpenByNameAsync("/uploadGraph"));
        Button(cut, "create").Click();
        Field(cut, "name").Input("companion");
        cut.Find("input[type=checkbox]").Change(true);

        Assert.Contains("Attach the file", cut.Markup);
        Assert.True(RunButton(cut).HasAttribute("disabled"));

        cut.Render(p => p.Add(c => c.AttachedFileName, "Companion.bundle.json"));
        Assert.Contains("Companion.bundle.json", cut.Markup);
        RunButton(cut).Click();

        Assert.Equal(["/uploadGraph create companion --all"], _ran);
    }

    [Fact]
    public async Task EditAsText_HandsTheLineToTheComposer()
    {
        IRenderedComponent<CommandPalette> cut = Render();
        await cut.InvokeAsync(() => cut.Instance.OpenByNameAsync("/upgrades"));
        Button(cut, "all").Click();

        Button(cut, "Edit as text").Click();

        Assert.Equal(["/upgrades all"], _edited);
        Assert.Empty(_ran);
    }

    [Fact]
    public void Highlight_MovesAndWraps()
    {
        IRenderedComponent<CommandPalette> cut = Render("/upg", fromComposer: true);

        cut.InvokeAsync(() => cut.Instance.MoveHighlight(1));
        Assert.Contains("/upgrades", cut.Find(".command-list button.active").TextContent);
        cut.InvokeAsync(() => cut.Instance.MoveHighlight(1));
        Assert.Contains("/upgrade", cut.Find(".command-list button.active code").TextContent);
        Assert.DoesNotContain("/upgrades", cut.Find(".command-list button.active code").TextContent);
    }
}
