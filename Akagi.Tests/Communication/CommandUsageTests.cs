using Akagi.Bridge.Chat.Models;
using Akagi.Communication.Commands;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using Xunit.Abstractions;
using AkagiCommand = Akagi.Communication.Commands.Command;

namespace Akagi.Tests.Communication;

/// <summary>
/// The usage grammar behind the web command forms, and the command line the forms write.
/// </summary>
public class CommandUsageTests
{
    private readonly ITestOutputHelper _output;

    public CommandUsageTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private static CommandForm Single(string name, string usage, bool acceptsFiles = false) =>
        Assert.Single(CommandUsage.Parse(name, usage, acceptsFiles));

    /// <summary>
    /// Builds a line from values keyed by parameter name.
    /// </summary>
    private static CommandUsage.BuildResult Build(string name, CommandForm form, Dictionary<string, string> values) =>
        CommandUsage.Build(name, form, p => values.TryGetValue(p.Name, out string? v) ? v : null);

    [Fact]
    public void Split_SeparatesSummaryAndUsage()
    {
        CommandUsage.UsageText text = CommandUsage.Split(
            "Queues a call. Usage: /analyzeCall [partner] [casual|polite] [speaker=Me]. left/right picks your channel.");

        Assert.Equal("/analyzeCall [partner] [casual|polite] [speaker=Me]", text.Usage);
        Assert.Equal("Queues a call. left/right picks your channel.", text.Summary);
    }

    [Theory]
    [InlineData("Adds a goal to the active character. Usage /addGoal <goal>", "/addGoal <goal>")]
    [InlineData("Lists all reflectors associated with the active character.", null)]
    [InlineData("Shows or sets your time zone. Usage: /timezone [Europe/Berlin]", "/timezone [Europe/Berlin]")]
    public void Split_FindsUsageWithOrWithoutColon(string description, string? usage)
    {
        Assert.Equal(usage, CommandUsage.Split(description).Usage);
    }

    [Fact]
    public void Parse_RequiredOptionalAndChoices()
    {
        CommandForm form = Single("/upgrade", "/upgrade <id> accept|reject|done|reopen [note]");

        Assert.Collection(form.Parameters,
            p => { Assert.Equal(CommandParameterKind.Text, p.Kind); Assert.Equal("id", p.Name); Assert.False(p.Optional); },
            p =>
            {
                Assert.Equal(CommandParameterKind.Choice, p.Kind);
                Assert.Equal(["accept", "reject", "done", "reopen"], p.Options.Select(o => o.Value));
            },
            p => { Assert.Equal(CommandParameterKind.Text, p.Kind); Assert.True(p.Optional); });
    }

    [Fact]
    public void Parse_AlternativeFormsAndOptionParameters()
    {
        CommandForm[] forms = CommandUsage.Parse("/session", "/session start [topic] | /session end [reflect|incognito|summary <text>] | /session (status)");

        Assert.Equal(3, forms.Length);
        Assert.Equal("/session start [topic]", forms[0].Usage);
        Assert.Equal(CommandParameterKind.Literal, forms[0].Parameters[0].Kind);

        CommandParameter ending = forms[1].Parameters[1];
        Assert.True(ending.Optional);
        CommandOption summary = ending.Options.Single(o => o.Value == "summary");
        Assert.Equal("text", Assert.Single(summary.Parameters).Name);

        CommandParameter status = Assert.Single(forms[2].Parameters);
        Assert.True(status.Optional);
        Assert.Equal(CommandParameterKind.Literal, status.Kind);
    }

    [Fact]
    public void Parse_NamedValuesFlagsFilesAndRest()
    {
        CommandForm call = Single("/analyzeCall", "/analyzeCall [partner] [speaker=Me]");
        Assert.Equal("speaker", call.Parameters[1].Key);
        Assert.Equal("Me", call.Parameters[1].Default);

        CommandForm upload = Single("/uploadGraph", "/uploadGraph <json files> create|update [name] [--all]", acceptsFiles: true);
        Assert.Equal(CommandParameterKind.File, upload.Parameters[0].Kind);
        Assert.Equal(CommandParameterKind.Literal, upload.Parameters[3].Kind);
        Assert.True(upload.Parameters[3].Optional);

        CommandForm macro = Single("/saveMacro", "/saveMacro <name> <steps...>");
        Assert.Equal(CommandParameterKind.Rest, macro.Parameters[1].Kind);
        Assert.Equal("steps", macro.Parameters[1].Name);
    }

    [Fact]
    public void Parse_StopsAtProseAfterValues()
    {
        CommandForm form = Single("/saveMacro", "/saveMacro <name> <steps> where steps are semicolon-separated commands");

        Assert.Equal(["name", "steps"], form.Parameters.Select(p => p.Name));
    }

    [Fact]
    public void Parse_WithoutUsage_GivesOneEmptyForm()
    {
        CommandForm form = Assert.Single(CommandUsage.Parse("/listMemory", null));
        Assert.Empty(form.Parameters);
        Assert.Equal("/listMemory", form.Usage);
    }

    [Fact]
    public void Build_QuotesValuesWithSpacesAndRoundTrips()
    {
        CommandForm form = Single("/editMessage", "/editMessage <messageIndex> <newText>");

        CommandUsage.BuildResult result = Build("/editMessage", form, new() { ["messageIndex"] = "3", ["newText"] = "hello there, friend" });

        Assert.True(result.Complete);
        Assert.Equal("/editMessage 3 {{hello there, friend}}", result.Text);
        Assert.Equal(["3", "hello there, friend"], AkagiCommand.ParseArguments(AkagiCommand.ArgumentText(result.Text)));
    }

    [Fact]
    public void Build_ChoicesNamedFlagsAndRest()
    {
        CommandForm session = CommandUsage.Parse("/session", "/session start [topic] | /session end [reflect|incognito|summary <text>]")[1];
        Assert.Equal("/session end summary {{we fixed the bug}}",
            Build("/session", session, new() { ["reflect / incognito / summary"] = "summary", ["text"] = "we fixed the bug" }).Text);
        Assert.Equal("/session end", Build("/session", session, []).Text);

        CommandForm call = Single("/analyzeCall", "/analyzeCall [partner] [casual|polite] [speaker=Me]");
        // The whole named value is grouped, which is what the server's argument parser reads as one argument
        Assert.Equal("/analyzeCall Yuki polite {{speaker=A B}}",
            Build("/analyzeCall", call, new() { ["partner"] = "Yuki", ["casual / polite"] = "polite", ["speaker"] = "A B" }).Text);

        CommandForm upload = Single("/uploadGraph", "/uploadGraph <json files> create|update [name] [--all]", acceptsFiles: true);
        Assert.Equal("/uploadGraph create companion --all",
            Build("/uploadGraph", upload, new() { ["create / update"] = "create", ["name"] = "companion", ["--all"] = "true" }).Text);

        CommandForm macro = Single("/saveMacro", "/saveMacro <name> <steps...>");
        Assert.Equal("/saveMacro m static:x=1 /ping;/uptime",
            Build("/saveMacro", macro, new() { ["name"] = "m", ["steps"] = "static:x=1 /ping;/uptime" }).Text);
    }

    [Fact]
    public void Build_ReportsMissingRequiredValues()
    {
        CommandForm form = Single("/upgrade", "/upgrade <id> accept|reject [note]");

        CommandUsage.BuildResult result = Build("/upgrade", form, new() { ["id"] = "4" });

        Assert.False(result.Complete);
        Assert.Equal(["accept / reject"], result.Missing);
    }

    [Theory]
    [InlineData("/upgrades open", "/upgrades")]
    [InlineData("/upgrade 3 done", "/upgrade")]
    [InlineData("/UPGRADE@AkagiBot 3 done", "/upgrade")]
    [InlineData("/upgradesX", null)]
    public void Find_MatchesTheWholeCommandWord(string text, string? expected)
    {
        TestCommand[] commands = [new("/upgrade"), new("/upgrades")];

        Assert.Equal(expected, AkagiCommand.Find(commands, text)?.Name);
    }

    [Fact]
    public void ArgumentText_SkipsTheCommandWord()
    {
        Assert.Equal("3 done", AkagiCommand.ArgumentText("/upgrade@AkagiBot   3 done"));
        Assert.Equal(string.Empty, AkagiCommand.ArgumentText("/ping"));
    }

    /// <summary>
    /// Every shipped command turns into forms whose parameters have names, and none keeps prose as fixed words.
    /// </summary>
    [Fact]
    public void AllCommands_DescribeIntoUsableForms()
    {
        StringBuilder dump = new();
        List<Type> types = [.. typeof(AkagiCommand).Assembly.GetTypes()
            .Where(t => typeof(AkagiCommand).IsAssignableFrom(t) && !t.IsAbstract && !t.ContainsGenericParameters)];
        Assert.True(types.Count > 50);

        foreach (Type type in types.OrderBy(t => t.Name))
        {
            AkagiCommand command = (AkagiCommand)RuntimeHelpers.GetUninitializedObject(type);
            Bridge.Chat.Models.Command described = command.Describe();

            Assert.StartsWith("/", described.Name);
            Assert.NotEmpty(described.Forms);
            Assert.False(string.IsNullOrWhiteSpace(described.Category));
            Assert.DoesNotContain("Usage", described.Summary, StringComparison.OrdinalIgnoreCase);
            foreach (CommandForm form in described.Forms)
            {
                Assert.All(form.Parameters, p => Assert.False(string.IsNullOrWhiteSpace(p.Name), $"{described.Name}: unnamed parameter"));
                Assert.True(form.Parameters.Count(p => p.Kind == CommandParameterKind.Rest) <= 1, described.Name);
                dump.AppendLine($"{described.Category,-16} {form.Usage}  =>  {string.Join(", ", form.Parameters.Select(Show))}");
            }
            if (!described.AcceptsFiles)
            {
                Assert.DoesNotContain(described.Forms.SelectMany(f => f.Parameters), p => p.Kind == CommandParameterKind.File);
            }
        }
        _output.WriteLine(dump.ToString());
    }

    /// <summary>
    /// The command list survives the socket bridge, nested option parameters included.
    /// </summary>
    [Fact]
    public void CommandList_RoundTripsThroughMessagePack()
    {
        Bridge.Chat.Models.Command[] commands = [.. typeof(AkagiCommand).Assembly.GetTypes()
            .Where(t => typeof(AkagiCommand).IsAssignableFrom(t) && !t.IsAbstract && !t.ContainsGenericParameters)
            .Select(t => ((AkagiCommand)RuntimeHelpers.GetUninitializedObject(t)).Describe())];
        Bridge.Chat.Transmissions.Responses.CommandResponseTransmission sent = new() { Commands = commands };

        Bridge.Chat.Transmissions.Responses.CommandResponseTransmission received =
            MessagePack.MessagePackSerializer.Deserialize<Bridge.Chat.Transmissions.Responses.CommandResponseTransmission>(
                MessagePack.MessagePackSerializer.Serialize(sent));

        Assert.Equal(commands.Length, received.Commands.Length);
        Bridge.Chat.Models.Command session = received.Commands.Single(c => c.Name == "/session");
        Assert.Equal("ActiveCharacters", session.Category);
        Assert.Equal(3, session.Forms.Length);
        CommandOption summary = session.Forms[1].Parameters[1].Options.Single(o => o.Value == "summary");
        Assert.Equal(CommandParameterKind.Text, Assert.Single(summary.Parameters).Kind);
        Assert.True(received.Commands.Single(c => c.Name == "/uploadGraph").AcceptsFiles);
    }

    private static string Show(CommandParameter p)
    {
        string text = p.Kind switch
        {
            CommandParameterKind.Choice => "{" + string.Join("|", p.Options.Select(o => o.Value + (o.Parameters.Length > 0 ? " " + string.Join(" ", o.Parameters.Select(Show)) : ""))) + "}",
            CommandParameterKind.Literal => p.Name,
            _ => $"{p.Kind}:{p.Key ?? p.Name}" + (p.Default != null ? "=" + p.Default : "")
        };
        return p.Optional ? "?" + text : text;
    }

    private class TestCommand : TextCommand
    {
        public TestCommand(string name)
        {
            Name = name;
        }

        public override string Name { get; }
        public override string Description => string.Empty;
        public override Task<CommandResult> ExecuteAsync(Context context, string[] args) => Task.FromResult(CommandResult.Ok);
    }
}
