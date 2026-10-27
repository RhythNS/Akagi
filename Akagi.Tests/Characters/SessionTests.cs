using Akagi.Characters;
using Akagi.Characters.CharacterBehaviors.MessageCompilers;
using Akagi.Characters.Conversations;
using Akagi.Characters.Groups;
using Akagi.Characters.Memories;
using Akagi.Receivers;
using Akagi.Tests.Support;
using Akagi.Users;
using MongoDB.Bson;

namespace Akagi.Tests.Characters;

internal class InMemoryMemoryDatabase : InMemoryDatabase<MemoryRecord>, IMemoryDatabase
{
    public InMemoryMemoryDatabase() : base("memories")
    {
    }

    public Task<List<MemoryRecord>> GetActiveMemoriesAsync(string characterId, string? userId, bool includeGlobalLore = true) =>
        Task.FromResult(Items.Where(m => m.CharacterId == characterId && m.UserId == userId && !m.IsArchived).ToList());

    public Task<List<MemoryRecord>> GetAllMemoriesAsync(string characterId, string? userId) =>
        Task.FromResult(Items.Where(m => m.CharacterId == characterId && m.UserId == userId).ToList());

    public Task ArchiveMemoryAsync(string memoryId)
    {
        Items.First(m => m.Id == memoryId).IsArchived = true;
        return Task.CompletedTask;
    }

    public Task UpdateLastAccessedAsync(string memoryId, DateTime accessedAt) => Task.CompletedTask;

    public Task UpdateLastAccessedBatchAsync(IEnumerable<string> memoryIds, DateTime accessedAt) => Task.CompletedTask;

    public Task EnsureIndexesAsync() => Task.CompletedTask;
}

/// <summary>
/// Records what the session manager asks the receiver to do instead of running reflectors.
/// </summary>
internal class RecordingReceiver : IReceiver
{
    public List<(string CharacterId, int ConversationId)> Reflected { get; } = [];
    public List<string> Ended { get; } = [];

    public Task ReflectConversation(Character character, User user, int conversationId)
    {
        Reflected.Add((character.Id!, conversationId));
        return Task.CompletedTask;
    }

    public Task OnConversationEnded(Character character, User user)
    {
        Ended.Add(character.Id!);
        return Task.CompletedTask;
    }

    public Task OnMessageRecieved(Akagi.Communication.ICommunicator from, User user, Character character, Message message) => throw new NotSupportedException();
    public Task OnGroupMessageRecieved(Akagi.Communication.ICommunicator from, User user, CharacterGroup group, Message message) => throw new NotSupportedException();
    public Task OnGroupSystemEvent(User user, CharacterGroup group, Message message) => throw new NotSupportedException();
    public Task OnPromptContinue(Akagi.Communication.ICommunicator? from, Character character, User user) => throw new NotSupportedException();
    public Task Reflect(Character character, User user, string name) => throw new NotSupportedException();
    public Task OnSystemEvent(Character character, User user, Message message) => throw new NotSupportedException();
}

public class SessionTests
{
    private static readonly DateTime Now = new(2026, 10, 27, 20, 0, 0, DateTimeKind.Utc);

    private sealed class Fixture
    {
        public InMemoryDatabaseFactory Database { get; } = new();
        public InMemoryMemoryDatabase Memories { get; } = new();
        public RecordingReceiver Receiver { get; } = new();
        public User User { get; } = new() { Id = ObjectId.GenerateNewId().ToString(), Name = "Tester" };
        public SessionManager Sessions { get; }

        public Fixture()
        {
            Database.Register(Memories);
            Sessions = new SessionManager(Receiver, Database);
        }

        public static Character NewCharacter(string name) => new() { Id = ObjectId.GenerateNewId().ToString(), Name = name };
    }

    private static void Say(Conversation conversation, string text, Message.Type from = Message.Type.User) =>
        conversation.AddMessage(new TextMessage { Text = text, From = from, Time = DateTime.UtcNow, CharacterId = null });

    [Fact]
    public async Task Start_EndsRunningConversationNormallyAndOpensSession()
    {
        Fixture fixture = new();
        Character akagi = Fixture.NewCharacter("Akagi");
        Say(akagi.Conversations.GetCurrentConversation(), "hi");

        Conversation session = await fixture.Sessions.StartAsync(SessionScope.ForCharacter(akagi), fixture.User, "movie night", Now);

        Assert.True(session.IsActiveSession);
        Assert.Equal("movie night", session.SessionTopic);
        Assert.Same(session, akagi.Conversations.ActiveSession);
        Assert.Equal(2, akagi.Conversations.List.Count);
        Assert.Equal([akagi.Id!], fixture.Receiver.Ended);
    }

    [Fact]
    public async Task EndReflect_RunsAllReflectorsOnTheSession()
    {
        Fixture fixture = new();
        Character akagi = Fixture.NewCharacter("Akagi");
        Conversation session = await fixture.Sessions.StartAsync(SessionScope.ForCharacter(akagi), fixture.User, string.Empty, Now);
        Say(session, "I passed the mock exam!");

        SessionEndResult? result = await fixture.Sessions.EndAsync(SessionScope.ForCharacter(akagi), fixture.User, ConversationEndMode.Reflect, null, Now);

        Assert.NotNull(result);
        Assert.Equal(1, result.Reflected);
        Assert.Equal([(akagi.Id!, session.Id)], fixture.Receiver.Reflected);
        Assert.True(session.IsCompleted);
        Assert.Equal(ConversationEndMode.Reflect, session.EndMode);
        Assert.Null(akagi.Conversations.ActiveSession);
    }

    [Fact]
    public async Task EndIncognito_ReflectsNothingAndHidesTheConversationFromPrompts()
    {
        Fixture fixture = new();
        Character akagi = Fixture.NewCharacter("Akagi");
        Conversation session = await fixture.Sessions.StartAsync(SessionScope.ForCharacter(akagi), fixture.User, "private", Now);
        Say(session, "secret thoughts");

        await fixture.Sessions.EndAsync(SessionScope.ForCharacter(akagi), fixture.User, ConversationEndMode.Incognito, null, Now);
        Say(akagi.Conversations.GetCurrentConversation(), "new topic");

        Assert.Empty(fixture.Receiver.Reflected);
        Assert.Empty(fixture.Receiver.Ended);
        Assert.True(session.IsIncognito);
        Assert.Contains(session, akagi.Conversations.List);

        Context context = new()
        {
            Character = akagi,
            Conversation = akagi.Conversations.GetCurrentConversation(),
            User = fixture.User,
            Communicator = new RecordingCommunicator(),
            LLMFactory = new NoLLMFactory(),
            DatabaseFactory = fixture.Database
        };
        DefaultMessageCompiler compiler = new();
        await compiler.Init(Message.Type.User | Message.Type.Character, null!);
        Message[] compiled = await compiler.CompileAsync(context);

        TextMessage only = Assert.IsType<TextMessage>(Assert.Single(compiled));
        Assert.Equal("new topic", only.Text);
    }

    [Fact]
    public async Task EndWithSummary_StoresUserTextAsThoughtAndMemory()
    {
        Fixture fixture = new();
        Character akagi = Fixture.NewCharacter("Akagi");
        Conversation session = await fixture.Sessions.StartAsync(SessionScope.ForCharacter(akagi), fixture.User, string.Empty, Now);
        Say(session, "long talk about moving to Osaka");

        SessionEndResult? result = await fixture.Sessions.EndAsync(SessionScope.ForCharacter(akagi), fixture.User,
            ConversationEndMode.CustomSummary, "We decided to visit Osaka in spring to look at apartments.", Now);

        Assert.Equal(1, result!.MemoriesStored);
        Assert.Empty(fixture.Receiver.Reflected);
        ConversationThought thought = Assert.Single(akagi.Memory.Conversations.Thoughts);
        Assert.Equal(session.Id, thought.ConversationId);
        Assert.Contains("Osaka in spring", thought.LongSummary);
        MemoryRecord memory = Assert.Single(fixture.Memories.Items);
        Assert.Equal(akagi.Id, memory.CharacterId);
        Assert.Equal(fixture.User.Id, memory.UserId);
        Assert.Equal(SessionManager.SessionMemoryCategory, memory.Category);
    }

    [Fact]
    public async Task EndWithSummary_RequiresText()
    {
        Fixture fixture = new();
        Character akagi = Fixture.NewCharacter("Akagi");

        await Assert.ThrowsAsync<ArgumentException>(() =>
            fixture.Sessions.EndAsync(SessionScope.ForCharacter(akagi), fixture.User, ConversationEndMode.CustomSummary, " ", Now));
    }

    [Fact]
    public async Task GroupSession_SpansGroupAndMemberConversations()
    {
        Fixture fixture = new();
        Character akagi = Fixture.NewCharacter("Akagi");
        Character sensei = Fixture.NewCharacter("Sensei");
        CharacterGroup group = new() { Id = ObjectId.GenerateNewId().ToString(), Name = "Companion" };
        SessionScope scope = new() { Group = group, Characters = [akagi, sensei] };

        await fixture.Sessions.StartAsync(scope, fixture.User, "N2 grammar", Now);
        AggregateConversation conversation = new(group.Conversations.GetCurrentConversation(),
            [akagi.Conversations.GetCurrentConversation(), sensei.Conversations.GetCurrentConversation()]);
        Assert.True(conversation.IsActiveSession);
        Say(conversation, "ざるを得ない vs わけにはいかない?");

        SessionEndResult? result = await fixture.Sessions.EndAsync(scope, fixture.User, ConversationEndMode.Reflect, null, Now);

        Assert.True(group.Conversations.List.Single(c => c.IsSession).IsCompleted);
        Assert.Equal(2, result!.Reflected);
        Assert.Equal([akagi.Id!, sensei.Id!], fixture.Receiver.Reflected.Select(r => r.CharacterId));
    }

    [Fact]
    public async Task End_WithNothingRunning_ReturnsNull()
    {
        Fixture fixture = new();
        Character akagi = Fixture.NewCharacter("Akagi");

        Assert.Null(await fixture.Sessions.EndAsync(SessionScope.ForCharacter(akagi), fixture.User, ConversationEndMode.Reflect, null, Now));
    }

    [Theory]
    [InlineData(null, "Reflect")]
    [InlineData("incognito", "Incognito")]
    [InlineData("Summary", "CustomSummary")]
    [InlineData("forget", "Incognito")]
    public void ParseMode_AcceptsAliases(string? text, string expected)
    {
        Assert.Equal(expected, SessionManager.ParseMode(text)!.Value.ToString());
    }

    [Fact]
    public void ParseMode_RejectsUnknown()
    {
        Assert.Null(SessionManager.ParseMode("explode"));
    }

    [Fact]
    public async Task EndConversationTool_RefusesDuringSession()
    {
        LifeTestContext test = new();
        Conversation session = test.Character.Conversations.StartSession("study", Now);
        Say(session, "one");
        Say(session, "two", Message.Type.Character);
        test.Context.Conversation = session;

        Akagi.Receivers.Commands.EndConversationCommand command = new()
        {
            Arguments = [new Akagi.Receivers.Commands.Argument { Name = "MessageIndex", Description = "", Value = "1", ArgumentType = Akagi.Receivers.Commands.Argument.Type.Int }]
        };
        await command.Execute(test.Context);

        Assert.False(session.IsCompleted);
        Assert.False(test.Context.ConversationEnded);
        Assert.Contains("only the user can end it", ((CommandMessage)session.Messages[^1]).Output);
    }
}
