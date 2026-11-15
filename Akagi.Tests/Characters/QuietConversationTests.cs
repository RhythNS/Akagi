using Akagi.Characters;
using Akagi.Characters.CharacterBehaviors.Reflectors;
using Akagi.Characters.CharacterBehaviors.SystemProcessors;
using Akagi.Characters.Conversations;
using Akagi.Characters.Groups;
using Akagi.Receivers;
using Akagi.Tests.Support;
using Akagi.Users;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Bson;

namespace Akagi.Tests.Characters;

public class QuietConversationTests
{
    private static readonly DateTime Now = new(2026, 11, 16, 9, 0, 0, DateTimeKind.Utc);

    private sealed class Fixture
    {
        public InMemoryDatabaseFactory Database { get; } = new();
        public InMemoryUserDatabase Users { get; } = new();
        public InMemoryCharacterDatabase Characters { get; } = new();
        public InMemoryCharacterGroupDatabase Groups { get; } = new();
        public RecordingReceiver Receiver { get; } = new();
        public User User { get; } = new() { Id = ObjectId.GenerateNewId().ToString(), Name = "Tester" };
        public QuietConversationCloser Closer { get; }

        public Fixture()
        {
            Database.Register(new InMemoryMemoryDatabase());
            Database.Register(Users);
            Database.Register(Characters);
            Database.Register(Groups);
            Users.SaveDocumentAsync(User);
            Closer = new QuietConversationCloser(Users, Characters, Groups, Receiver, Database, NullLogger.Instance);
        }

        public Character Character(string name)
        {
            Character character = new() { Id = ObjectId.GenerateNewId().ToString(), Name = name, UserId = User.Id! };
            Characters.SaveDocumentAsync(character);
            return character;
        }

        public CharacterGroup Group(params Character[] members)
        {
            CharacterGroup group = new()
            {
                Id = ObjectId.GenerateNewId().ToString(),
                Name = "Companion",
                UserId = User.Id!,
                MemberCharacterIds = [.. members.Select(m => m.Id!)]
            };
            Groups.SaveDocumentAsync(group);
            return group;
        }
    }

    private static void Say(Conversation conversation, Message.Type from, DateTime time, string text = "hi") =>
        conversation.AddMessage(new TextMessage { Text = text, From = from, Time = time, CharacterId = null });

    [Fact]
    public void IsQuiet_CountsOnlyTheUsersMessages()
    {
        Conversation conversation = new() { Id = 1, Time = Now.AddDays(-1) };
        Say(conversation, Message.Type.User, Now.AddHours(-8));
        Assert.True(QuietConversationCloser.IsQuiet(conversation, Now));

        // Check-ins the companion sent on its own keep nothing open
        Say(conversation, Message.Type.Character, Now.AddHours(-2));
        Say(conversation, Message.Type.System, Now.AddMinutes(-5));
        Assert.True(QuietConversationCloser.IsQuiet(conversation, Now));

        Say(conversation, Message.Type.User, Now.AddHours(-6));
        Assert.False(QuietConversationCloser.IsQuiet(conversation, Now));
        Assert.True(QuietConversationCloser.IsQuiet(conversation, Now.AddHours(1)));
    }

    [Fact]
    public void IsQuiet_NeverForAConversationWithoutTheUserOrForASession()
    {
        Conversation companionOnly = new() { Id = 1, Time = Now.AddDays(-2) };
        Say(companionOnly, Message.Type.Character, Now.AddDays(-1));
        Assert.False(QuietConversationCloser.IsQuiet(companionOnly, Now));

        Conversation session = new() { Id = 2, Time = Now.AddDays(-2), IsSession = true };
        Say(session, Message.Type.User, Now.AddDays(-1));
        Assert.False(QuietConversationCloser.IsQuiet(session, Now));

        Assert.False(QuietConversationCloser.IsQuiet(null, Now));
    }

    [Fact]
    public async Task Tick_EndsAQuietGroupAndReflectsEveryMemberOnTheGroupConversation()
    {
        Fixture fixture = new();
        Character akagi = fixture.Character("Akagi");
        Character sensei = fixture.Character("Sensei");
        CharacterGroup group = fixture.Group(akagi, sensei);
        Conversation chat = group.Conversations.GetCurrentConversation();
        Say(chat, Message.Type.User, Now.AddHours(-7));
        Say(chat, Message.Type.Character, Now.AddHours(-1));
        Say(akagi.Conversations.GetCurrentConversation(), Message.Type.Character, Now.AddHours(-1));
        sensei.Conversations.GetCurrentConversation();

        Assert.Equal(1, await fixture.Closer.TickAsync(Now));

        Assert.True(chat.IsCompleted);
        Assert.Equal(ConversationEndMode.Reflect, chat.EndMode);
        Assert.Equal([(akagi.Id!, chat.Id), (sensei.Id!, chat.Id)], fixture.Receiver.Reflected);
        Assert.Equal(2, group.Conversations.List.Count);
        Assert.Equal(2, akagi.Conversations.List.Count);
        Assert.Equal(2, sensei.Conversations.List.Count);

        // A check-in in the new conversation does not end it again, and members are not ended on their own
        Say(group.Conversations.GetCurrentConversation(), Message.Type.Character, Now.AddHours(1));
        Assert.Equal(0, await fixture.Closer.TickAsync(Now.AddDays(1)));
        Assert.Equal(2, group.Conversations.List.Count);
    }

    [Fact]
    public async Task Tick_WaitsForBusyAndRecentConversationsAndEndsStandaloneCharacters()
    {
        Fixture fixture = new();
        Character akagi = fixture.Character("Akagi");
        CharacterGroup group = fixture.Group(akagi);
        Say(group.Conversations.GetCurrentConversation(), Message.Type.User, Now.AddHours(-6));
        Character mio = fixture.Character("Mio");
        Say(mio.Conversations.GetCurrentConversation(), Message.Type.User, Now.AddHours(-8));

        fixture.Receiver.Busy = true;
        Assert.Equal(0, await fixture.Closer.TickAsync(Now));
        Assert.Single(mio.Conversations.List);

        fixture.Receiver.Busy = false;
        Assert.Equal(1, await fixture.Closer.TickAsync(Now));
        Assert.Equal(2, mio.Conversations.List.Count);
        Assert.Single(group.Conversations.List);
        Assert.Equal([(mio.Id!, 1)], fixture.Receiver.Reflected);
    }

    [Fact]
    public async Task MemoryConsolidation_ReadsTheGroupsFinishedConversations()
    {
        Fixture fixture = new();
        Character akagi = fixture.Character("Akagi");
        CharacterGroup group = fixture.Group(akagi);
        Conversation finished = group.Conversations.GetCurrentConversation();
        for (int i = 0; i < 4; i++)
        {
            Say(finished, Message.Type.User, Now.AddHours(-9));
        }
        group.Conversations.EndCurrent(ConversationEndMode.Reflect, Now);
        Conversation running = group.Conversations.GetCurrentConversation();
        for (int i = 0; i < 4; i++)
        {
            Say(running, Message.Type.User, Now);
        }
        // The member's own copy has no messages from the user
        Say(akagi.Conversations.GetCurrentConversation(), Message.Type.Character, Now.AddHours(-9));

        Probe probe = new();
        await probe.Init(NullLogger.Instance, new Context
        {
            Character = akagi,
            Group = group,
            Conversation = finished,
            User = fixture.User,
            Communicator = new RecordingCommunicator(),
            LLMFactory = new NoLLMFactory(),
            DatabaseFactory = fixture.Database
        }, new ProcessorDatabase());

        Assert.Equal([finished], probe.Pick());
    }

    private sealed class Probe : MemoryConsolidationReflector
    {
        public List<Conversation> Pick() => GetConversationsToConsolidate();
    }

    private sealed class ProcessorDatabase : InMemoryDatabase<SystemProcessor>, ISystemProcessorDatabase
    {
        public ProcessorDatabase() : base("system_processors")
        {
        }

        public async Task<SystemProcessor> GetSystemProcessor(string id) => (await GetDocumentByIdAsync(id))!;

        public Task<SystemProcessor[]> GetSystemProcessor(string[] ids) => throw new NotSupportedException();
    }
}
