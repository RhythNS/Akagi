using Akagi.Characters.Conversations;
using Akagi.LLMs.OpenRouter;
using Akagi.Receivers.Commands;
using Akagi.Receivers.Commands.Life;
using System.Text.Json;

namespace Akagi.Tests.LLMs;

public class ToolExchangeTests
{
    private static CommandMessage Result(Command command, string output, DateTime time) => new()
    {
        Command = command,
        Output = output,
        From = Akagi.Characters.Conversations.Message.Type.System,
        CharacterId = "c",
        Time = time
    };

    [Fact]
    public void ToolHistory_IsReplayedForOfferedCommandsOnly()
    {
        DateTime time = new(2026, 10, 27, 9, 0, 0, DateTimeKind.Utc);
        RecordMoodCommand mood = new() { Arguments = [new Argument { Name = "Note", Description = "", Value = "tired" }] };
        CommandMessage[] history =
        [
            Result(mood, "Recorded checkin: tired", time),
            Result(new StopExecutionCommand(), "Stopped execution!", time),
            Result(new LookupWordCommand(), "Entry: 影響", time),
        ];
        int sequence = 0;

        List<OpenRouterPayload.Message> messages = OpenRouterClient.BuildToolExchange(history, ["recordmood", "StopExecution"], ref sequence);

        Assert.Equal(3, messages.Count);
        OpenRouterPayload.ToolRequestMessage request = Assert.IsType<OpenRouterPayload.ToolRequestMessage>(messages[0]);
        Assert.Equal(OpenRouterPayload.MessageRoleEnum.Assistant, request.Role);
        Assert.Equal(["RecordMood", "StopExecution"], request.ToolCalls.Select(c => c.Function!.Name));
        Assert.Equal(2, request.ToolCalls.Select(c => c.Id).Distinct().Count());
        Assert.Equal(2, sequence);

        string json = JsonSerializer.Serialize<OpenRouterPayload.Message>(messages[1], OpenRouterClient.PayloadJsonOptions);
        Assert.Contains("\"role\":\"tool\"", json);
        Assert.Contains($"\"tool_call_id\":\"{request.ToolCalls[0].Id}\"", json);
        Assert.Contains("tired", json);
    }

    [Fact]
    public void ToolHistory_WithNoOfferedCommands_IsEmpty()
    {
        int sequence = 0;
        List<OpenRouterPayload.Message> messages = OpenRouterClient.BuildToolExchange(
            [Result(new StopExecutionCommand(), "x", DateTime.UtcNow)], ["Consult"], ref sequence);

        Assert.Empty(messages);
    }
}
