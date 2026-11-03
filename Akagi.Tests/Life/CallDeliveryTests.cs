using Akagi.Characters;
using Akagi.Characters.Conversations;
using Akagi.Characters.Groups;
using Akagi.Communication;
using Akagi.Life.Japanese.Calls;
using Akagi.Tests.Support;
using Akagi.Users;

namespace Akagi.Tests.Life;

/// <summary>
/// Handing a finished call report to the companion.
/// </summary>
public class CallDeliveryTests
{
    private sealed class Communicators(params string[] names) : ICommunicatorFactory
    {
        public ICommunicator? Create(string name) => names.Contains(name, StringComparer.OrdinalIgnoreCase) ? new RecordingCommunicator() : null;

        public IEnumerable<string> GetAvailableCommunicators() => names;
    }

    private sealed class RecordingDelivery : IProactiveDelivery
    {
        public List<(Character Character, Message Message)> Delivered { get; } = [];

        public Task DeliverAsync(Character character, User user, Message message)
        {
            Delivered.Add((character, message));
            return Task.CompletedTask;
        }
    }

    private static async Task<(CallJob Job, InMemoryCharacterDatabase Characters, InMemoryUserDatabase Users)> SetupAsync(string lastUsed)
    {
        InMemoryCharacterDatabase characters = new();
        InMemoryUserDatabase users = new();
        User user = new() { Name = "Tester", LastUsedCommunicator = lastUsed };
        await users.SaveDocumentAsync(user);
        Character akagi = new() { Name = "Akagi" };
        await characters.SaveDocumentAsync(akagi);
        CallJob job = new() { Id = "6aea2e36dc05a104fc754471", UserId = user.Id!, DeliveryCharacterId = akagi.Id!, Title = "Yuki", Report = "Call analysis: ..." };
        return (job, characters, users);
    }

    [Fact]
    public async Task AReportThatCannotReachTheUserIsNotCountedAsDelivered()
    {
        (CallJob job, InMemoryCharacterDatabase characters, InMemoryUserDatabase users) = await SetupAsync("Socket");
        RecordingDelivery delivery = new();
        CallDelivery calls = new(characters, users, new Communicators("Telegram"), delivery);

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() => calls.DeliverAsync(job));

        Assert.Contains("/calls 754471", error.Message);
        Assert.Empty(delivery.Delivered);
    }

    [Fact]
    public async Task TheCompanionGetsTheStoredReport()
    {
        (CallJob job, InMemoryCharacterDatabase characters, InMemoryUserDatabase users) = await SetupAsync("Socket");
        RecordingDelivery delivery = new();
        CallDelivery calls = new(characters, users, new Communicators("Telegram", "Socket"), delivery);

        await calls.DeliverAsync(job);

        (Character character, Message message) = Assert.Single(delivery.Delivered);
        Assert.Equal("Akagi", character.Name);
        Assert.Contains("Call analysis: ...", Assert.IsType<TextMessage>(message).Text);
    }
}
