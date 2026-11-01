using Akagi.Characters.CharacterBehaviors.SystemProcessors;
using Akagi.LLMs.OpenRouter;
using Akagi.Receivers.Commands;
using Akagi.Receivers.Commands.Life;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Akagi.Tests.LLMs;

public class ParallelToolCallTests
{
    private sealed class Factory : ICommandFactory
    {
        public T Create<T>() where T : Command => throw new NotSupportedException();

        public Command Create(string commandName) => new RecordJapaneseStrengthCommand();
    }

    private sealed class Monitor : IOptionsMonitor<OpenRouterClient.Options>
    {
        public OpenRouterClient.Options CurrentValue { get; } = new() { ApiKey = "none", BaseUrl = "none", ResponsePath = "none" };

        public OpenRouterClient.Options Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<OpenRouterClient.Options, string?> listener) => null;
    }

    [Fact]
    public void SeveralCallsOfOneTool_KeepTheirOwnArguments()
    {
        OpenRouterClient client = new(new Factory(), null!, new Monitor(), NullLogger<OpenRouterClient>.Instance);
        SystemProcessor processor = new() { Commands = [new RecordJapaneseStrengthCommand()] };

        Command first = client.BuildCommandFromFunction(processor, "RecordJapaneseStrength", """{"Strength":"first"}""");
        Command second = client.BuildCommandFromFunction(processor, "RecordJapaneseStrength", """{"Strength":"second"}""");

        Assert.NotSame(first, second);
        Assert.Equal("first", first.Arguments.Single(a => a.Name == "Strength").Value);
        Assert.Equal("second", second.Arguments.Single(a => a.Name == "Strength").Value);
    }
}
