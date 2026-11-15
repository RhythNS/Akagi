using AngleSharp.Dom;
using Akagi.Bridge.Chat.Models;
using Akagi.Web.Models.Chat;
using Akagi.Web.Pages.Chat.Components;
using Bunit;
using TextMessage = Akagi.Web.Models.Chat.TextMessage;

namespace Akagi.Web.Tests;

/// <summary>
/// A chat turn opened up: everything the characters did between the user's message and the next one.
/// </summary>
public class ChatTurnTests : BunitContext
{
    private static readonly DateTime Time = new(2026, 11, 15, 15, 18, 0, DateTimeKind.Utc);

    private static TextMessage Say(Message.Type from, string text, string? characterId = null, bool isInternal = false) =>
        new() { From = from, Text = text, Time = Time, CharacterId = characterId, Internal = isInternal };

    private static TextMessage Tool(string name, string output, string characterId = "ren", params (string Name, string Value)[] arguments) => new()
    {
        From = Message.Type.Character,
        Time = Time,
        CharacterId = characterId,
        Internal = true,
        Tool = new ToolCall
        {
            Name = name,
            Output = output,
            Arguments = [.. arguments.Select(a => new ToolArgument { Name = a.Name, Value = a.Value })]
        }
    };

    private static string Name(string? id) => id switch
    {
        "ren" => "Ren",
        "akagi" => "Akagi",
        _ => "Companion"
    };

    /// <summary>A group turn like the ones on prod: a consultation, the advisor's tools and answer, the reply.</summary>
    private static List<Message> GroupChat() =>
    [
        Say(Message.Type.User, "Do we need to think of anything else?"),
        Say(Message.Type.System, "[System] [Consultation for Ren from Akagi] Update the board", isInternal: true),
        Tool("AddTicketUpdate", "Added update 2 to #23.", "ren", ("Ticket", "#23")),
        Tool("EditProject", "Not done: identical to another call in this reply."),
        Say(Message.Type.Character, "Both tickets updated.", "ren", isInternal: true),
        Say(Message.Type.Character, "Done! Anything else on your mind?", "akagi"),
        new TextMessage { From = Message.Type.Character, Time = Time, CharacterId = "akagi", Tool = new ToolCall { Name = "RecordMood", Output = "Recorded event" } },
        Say(Message.Type.System, "[System] [Proactive] Morning greeting"),
        Say(Message.Type.Character, "Good morning!", "akagi"),
    ];

    [Fact]
    public void ToolCallsAndInternalMessagesAreStepsNotBubbles()
    {
        Assert.False(Tool("ShowPlan", "plan").IsShownInChat);
        Assert.False(Say(Message.Type.Character, "Both tickets updated.", "ren", isInternal: true).IsShownInChat);
        Assert.True(Say(Message.Type.Character, "Done!", "akagi").IsShownInChat);

        TextMessage fromBridge = TextMessage.FromBridgeMessage(new Bridge.Chat.Models.TextMessage
        {
            From = Bridge.Chat.Models.TextMessage.Type.Character,
            Time = Time,
            CharacterId = "ren",
            Internal = true,
            Tool = new ToolCall { Name = "ShowBoard" }
        });
        Assert.Equal(("ren", true, "ShowBoard"), (fromBridge.CharacterId, fromBridge.Internal, fromBridge.Tool?.Name));
    }

    [Fact]
    public void Turns_StartWithTheUserOrAnInstructionButNotWithAConsultation()
    {
        List<ChatTurn> turns = ChatTurns.Build(1, GroupChat());

        Assert.Equal(2, turns.Count);
        Assert.Equal(7, turns[0].Steps.Count);
        Assert.Equal(["Do we need to think of anything else?", "Done! Anything else on your mind?"], turns[0].Bubbles.Select(b => b.Text));
        Assert.Equal(5, turns[0].HiddenCount);
        Assert.Equal(["Good morning!"], turns[1].Bubbles.Select(b => b.Text));
        // The routine instruction itself is a step the chat does not show
        Assert.Equal(1, turns[1].HiddenCount);
        Assert.Equal(["1:0", "1:7"], turns.Select(t => t.Key));
    }

    [Fact]
    public void Turns_KeepStepsBeforeTheFirstMessageInATurnOfTheirOwn()
    {
        List<ChatTurn> turns = ChatTurns.Build(3, [Tool("ConsolidateMemory", "Saved"), Say(Message.Type.User, "Hi")]);

        Assert.Equal(2, turns.Count);
        Assert.Empty(turns[0].Bubbles);
        Assert.Equal(1, turns[0].HiddenCount);
    }

    [Fact]
    public void Describe_NamesWhoDidWhat()
    {
        List<Message> chat = GroupChat();

        Assert.Equal(
        [
            "You wrote", "Akagi asks Ren", "Ren used AddTicketUpdate", "Ren used EditProject", "Ren answered",
            "Akagi wrote", "Akagi used RecordMood", "Instruction", "Akagi wrote"
        ], chat.Select(m => ChatTurns.Describe(m, Name)));
    }

    [Fact]
    public void StepText_LeavesOutWhatTheTitleSays()
    {
        Assert.Equal("Update the board", ChatTurns.StepText(Say(Message.Type.System,
            "[System] [Consultation for Ren from Akagi] Update the board\nRen: answer Akagi directly and concisely; Akagi will talk to the user.")));
        Assert.Equal("[Proactive] Morning greeting", ChatTurns.StepText(Say(Message.Type.System, "[System] [Proactive] Morning greeting")));
        Assert.Equal("Both tickets updated.", ChatTurns.StepText(Say(Message.Type.Character, "Both tickets updated.")));
    }

    [Fact]
    public void Steps_ShowArgumentsResultsAndWhatWasNotDone()
    {
        string longOutput = new('x', ChatTurnSteps.OpenOutputLength + 1);
        List<Message> steps = [.. GroupChat().Take(7), Tool("ShowPlan", longOutput)];

        IRenderedComponent<ChatTurnSteps> rendered = Render<ChatTurnSteps>(p => p
            .Add(c => c.Steps, steps)
            .Add(c => c.Name, Name)
            .Add(c => c.Live, true));

        Assert.Equal(8, rendered.FindAll(".turn-step:not(.live)").Count);
        Assert.Contains("Akagi asks Ren", rendered.Markup);
        IElement update = rendered.FindAll(".turn-step.tool")[0];
        Assert.Equal("Ticket", update.QuerySelector("dt")!.TextContent);
        Assert.Equal("#23", update.QuerySelector("dd")!.TextContent);
        Assert.True(update.QuerySelector("details")!.HasAttribute("open"));
        Assert.Contains("not done", rendered.FindAll(".turn-step.refused").Single().TextContent);
        // Long results stay closed until clicked
        Assert.False(rendered.FindAll(".turn-step.tool").Last().QuerySelector("details")!.HasAttribute("open"));
        Assert.Contains("Still working", rendered.Find(".turn-step.live").TextContent);
    }
}
