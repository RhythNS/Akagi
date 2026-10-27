using Akagi.Characters;
using Akagi.Characters.CharacterBehaviors.SystemProcessors;
using Akagi.Characters.Groups;
using Akagi.Characters.Groups.Strategies;
using Akagi.Characters.Routines;
using Akagi.Data;
using Akagi.Graphs;
using Akagi.Tests.Support;
using static Akagi.Tests.Support.TestGraphs;

namespace Akagi.Tests.Graphs;

/// <summary>
/// Guards the companion graphs shipped in Akagi.CharacterEditor/Characters/Companion.
/// </summary>
public class CompanionGraphTests
{
    private static string CompanionDirectory()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Akagi.sln")))
        {
            directory = directory.Parent;
        }
        Assert.NotNull(directory);
        return Path.Combine(directory.FullName, "Akagi.CharacterEditor", "Characters", "Companion");
    }

    [Fact]
    public async Task CompanionGraphs_AreValidWithoutWarnings()
    {
        GraphValidator validator = new();
        List<GraphValidationReport> reports = [];
        foreach (string file in Directory.GetFiles(CompanionDirectory(), "*.json"))
        {
            reports.Add(await validator.ValidateFileAsync(file));
        }
        GraphValidator.CheckDuplicateGraphIds(reports);

        Assert.Equal(5, reports.Count);
        foreach (GraphValidationReport report in reports)
        {
            Assert.True(report.IsValid && report.WarningCount == 0, report.ToText());
        }
    }

    [Fact]
    public async Task CompanionGroup_LoadsAsPersonaWithAdvisors()
    {
        string directory = CompanionDirectory();
        InMemoryDatabaseFactory database = new();
        GraphLoader loader = new(database) { Resolver = new FileGraphResolver(), BaseDirectory = directory };
        loader.LoadGraph(GraphData.Load(Path.Combine(directory, "Companion.json")));

        List<Savable> created = await loader.Create("65f000000000000000000002", "companion");

        CharacterGroup group = Assert.Single(created.OfType<CharacterGroup>());
        PersonaStrategy strategy = Assert.Single(created.OfType<PersonaStrategy>());
        Character akagi = Assert.Single(created.OfType<Character>(), c => c.Name == "Akagi");
        Assert.Equal(akagi.Id, strategy.PersonaCharacterId);
        Assert.Equal(4, group.MemberCharacterIds.Length);
        Assert.Equal(["Akagi", "Sensei", "Hana", "Kai"],
            group.MemberCharacterIds.Select(id => created.OfType<Character>().Single(c => c.Id == id).Name));

        Assert.Equal(6, akagi.RoutineIds.Length);
        Assert.All(akagi.RoutineIds, id => Assert.IsAssignableFrom<Routine>(database.Documents[id]));
        Assert.Equal(2, akagi.ReflectorIds.Length);

        SystemProcessor persona = created.OfType<SystemProcessor>().Single(p => p.Name == "Akagi");
        Assert.Contains("Consult", persona.CommandNames);
    }

    [Fact]
    public async Task Akagi_CarriesTheTruePersonaAndHerSelfManagementTools()
    {
        string directory = CompanionDirectory();
        InMemoryDatabaseFactory database = new();
        GraphLoader loader = new(database) { Resolver = new FileGraphResolver(), BaseDirectory = directory };
        loader.LoadGraph(GraphData.Load(Path.Combine(directory, "Companion.json")));

        List<Savable> created = await loader.Create("65f000000000000000000003", "companion");

        Akagi.Characters.Cards.Card card = created.OfType<Akagi.Characters.Cards.Card>().Single(c => c.Name == "Akagi");
        Assert.StartsWith("[Character(\"Akagi\")]", card.Description);
        Assert.Contains("The Developer Bond", card.Description);
        Assert.Contains("Reflect Akagi's dual nature", card.Description);

        SystemProcessor persona = created.OfType<SystemProcessor>().Single(p => p.Name == "Akagi");
        Assert.Contains("{{description}}", persona.SystemInstruction);
        Assert.Contains("girlfriend", persona.SystemInstruction);
        Assert.Contains("software {{user}} wrote", persona.SystemInstruction);
        Assert.Contains("never guilt-trip", persona.SystemInstruction);
        foreach (string tool in new[] { "ScheduleCheckIn", "AdjustRoutine", "CancelRoutine", "ListRoutines", "SuggestUpgrade",
                     "ChangeOutfit", "StartActivity", "RememberPerson", "SyncHealth", "RecentCallAnalyses" })
        {
            Assert.Contains(tool, persona.CommandNames);
        }

        SystemProcessor sensei = created.OfType<SystemProcessor>().Single(p => p.Name == "Sensei Processor");
        Assert.Contains("RecordJapaneseIssue", sensei.CommandNames);
        Assert.DoesNotContain("AddVocabulary", sensei.CommandNames);
        Assert.Contains(created, s => s is Akagi.Characters.CharacterBehaviors.MessageCompilers.Injections.JapaneseDiagnosisInjectionCompiler);
        Assert.Contains(created, s => s is Akagi.Characters.CharacterBehaviors.MessageCompilers.Injections.CompanionStateInjectionCompiler);
        Assert.DoesNotContain(created, s => s is StudyNudgeRoutine);
        Assert.Contains(created, s => s is ShortSleepRoutine { ReactToRestless: true });
    }

    [Fact]
    public async Task UnknownCommandName_IsError()
    {
        GraphData graph = CharacterGraph("Akagi");
        graph.Nodes[4] = Node(graph.Nodes[4].TypeName, new { Name = "Processor", CommandNames = new[] { "RecordMood", "TeleportUser" } });

        GraphValidationReport report = await new GraphValidator().ValidateAsync(graph, null);

        GraphIssue issue = Assert.Single(report.Issues, i => i.Code == "command.unknown");
        Assert.Contains("TeleportUser", issue.Message);
    }

    [Fact]
    public void CommandCatalog_ListsLifeToolsWithArguments()
    {
        CommandCatalog.CommandInfo mood = Assert.Single(CommandCatalog.Commands, c => c.Name == "RecordMood");
        Assert.Contains(mood.Arguments, a => a.Name == "Note" && a.Required);
        Assert.True(CommandCatalog.Exists("consult"));
        Assert.Contains(NodeCatalog.Build().Commands, c => c.Name == "LookupWord");
    }
}
