using Akagi.Characters;
using Akagi.Characters.Conversations;
using Akagi.Characters.Groups;
using Akagi.Characters.Groups.Strategies;
using Akagi.Receivers.Commands;
using Akagi.Tests.Support;

namespace Akagi.Tests.Life;

public class PersonaTests
{
    [Fact]
    public void FindMember_MatchesExactThenPartialIgnoringMention()
    {
        Character tutor = new() { Name = "Sensei" };
        Character health = new() { Name = "HealthMonitor" };

        Assert.Same(tutor, ConsultCommand.FindMember([tutor, health], "@sensei"));
        Assert.Same(health, ConsultCommand.FindMember([tutor, health], "health"));
        Assert.Null(ConsultCommand.FindMember([tutor, health], "Chef"));
    }

    [Fact]
    public void CollectAnswer_IncludesSpeechAndToolResultsOfTheAdvisorOnly()
    {
        StopExecutionCommand tool = new();
        Message[] messages =
        [
            new TextMessage { From = Message.Type.Character, CharacterId = "tutor", Time = DateTime.UtcNow, Text = "〜ように means 'so that'." },
            new CommandMessage { From = Message.Type.Character, CharacterId = "tutor", Time = DateTime.UtcNow, Command = tool, Output = "done" },
            new TextMessage { From = Message.Type.Character, CharacterId = "persona", Time = DateTime.UtcNow, Text = "not mine" },
            new TextMessage { From = Message.Type.System, CharacterId = "tutor", Time = DateTime.UtcNow, Text = "system" },
        ];

        string answer = ConsultCommand.CollectAnswer(messages, "tutor");

        Assert.Contains("so that", answer);
        Assert.Contains("(StopExecution: done)", answer);
        Assert.DoesNotContain("not mine", answer);
        Assert.DoesNotContain("system", answer);
    }

    [Fact]
    public async Task Consult_OutsideGroup_TellsPersonaToAnswerItself()
    {
        LifeTestContext test = new();

        string output = await test.Run<ConsultCommand>(("Member", "Sensei"), ("Request", "explain ように"));

        Assert.Contains("only works inside a group", output);
    }

    [Fact]
    public async Task Consult_UnknownMember_ListsAdvisorsAndExcludesSelf()
    {
        LifeTestContext test = new();
        InMemoryCharacterDatabase characters = new();
        test.Database.Register(characters);
        Character tutor = new() { Name = "Sensei" };
        await characters.SaveDocumentAsync(tutor);
        await characters.SaveDocumentAsync(test.Character);
        test.Context.Group = new CharacterGroup { Name = "Companion", MemberCharacterIds = [test.Character.Id!, tutor.Id!] };

        string output = await test.Run<ConsultCommand>(("Member", "Chef"), ("Request", "cook"));

        Assert.Contains("Available: Sensei.", output);
        Assert.DoesNotContain("Akagi", output.Split("Available:")[1]);
    }

    [Fact]
    public void SelectPersonaGroup_OnlyPicksGroupsWhereCharacterIsPersona()
    {
        CharacterGroup orchestrated = new() { Name = "Work" };
        CharacterGroup companion = new() { Name = "Companion" };
        CharacterGroup paused = new() { Name = "Paused", AllowAutomaticProcessing = false };

        CharacterGroup? selected = ProactiveDelivery.SelectPersonaGroup(
        [
            (orchestrated, new OrchestratorStrategy { ManagerCharacterId = "akagi" }),
            (paused, new PersonaStrategy { PersonaCharacterId = "akagi" }),
            (companion, new PersonaStrategy { PersonaCharacterId = "akagi" }),
        ], "akagi");

        Assert.Same(companion, selected);
        Assert.Null(ProactiveDelivery.SelectPersonaGroup([(companion, new PersonaStrategy { PersonaCharacterId = "other" })], "akagi"));
        Assert.Null(ProactiveDelivery.SelectPersonaGroup([(companion, null)], "akagi"));
    }
}
