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

        string answer = ConsultCommand.CollectAnswer(messages, "tutor", "Sensei");

        Assert.StartsWith("Sensei answered:", answer);
        Assert.Contains("so that", answer);
        Assert.Contains("(StopExecution: done)", answer);
        Assert.DoesNotContain("not mine", answer);
        Assert.DoesNotContain("system", answer);
        Assert.Equal("Sensei had nothing to add.", ConsultCommand.CollectAnswer([], "tutor", "Sensei"));
    }

    [Fact]
    public void CollectAnswer_WithoutSpokenAnswer_SaysSoAndKeepsOnlyWhatWasDone()
    {
        // On prod Ren ran out of steps after ~225 calls, nearly all refused, and Akagi got all of them back
        DateTime now = DateTime.UtcNow;
        List<Message> messages = [];
        CommandMessage Call(string output) => new()
        {
            From = Message.Type.Character, CharacterId = "ren", Time = now, Command = new StopExecutionCommand(), Output = output
        };
        messages.Add(Call("Added update 2 to #23."));
        for (int i = 0; i < 100; i++)
        {
            messages.Add(Call(ToolGuard.RefusalPrefix + "identical to another call in this reply."));
            messages.Add(Call("Plan for Move out: 10 open tickets."));
        }
        messages.Add(Call("Error: boom"));

        string answer = ConsultCommand.CollectAnswer(messages, "ren", "Ren");

        Assert.StartsWith("Ren gave no answer (it ran out of steps). Only what is listed here was done", answer);
        Assert.Contains("(StopExecution: Added update 2 to #23.)", answer);
        Assert.Single(answer.Split('\n'), l => l.Contains("10 open tickets"));
        Assert.Contains("(101 call(s) were refused or failed and changed nothing.)", answer);
        Assert.DoesNotContain("identical", answer);
    }

    [Fact]
    public void CollectAnswer_KeepsTheNewestToolResultsWithinTheBudget()
    {
        DateTime now = DateTime.UtcNow;
        string big = new('x', ConsultCommand.MaxToolResultsLength / 2);
        Message[] messages =
        [
            .. Enumerable.Range(1, 4).Select(i => new CommandMessage
            {
                From = Message.Type.Character, CharacterId = "ren", Time = now, Command = new StopExecutionCommand(), Output = $"{i} {big}"
            }),
            new TextMessage { From = Message.Type.Character, CharacterId = "ren", Time = now, Text = "Done." }
        ];

        string answer = ConsultCommand.CollectAnswer(messages, "ren", "Ren");

        Assert.Contains("(3 earlier tool result(s) left out.)", answer);
        Assert.Contains("(StopExecution: 4 ", answer);
        Assert.DoesNotContain("(StopExecution: 3 ", answer);
        Assert.EndsWith("Done.", answer);
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
