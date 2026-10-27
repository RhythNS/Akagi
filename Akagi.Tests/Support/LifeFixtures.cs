using Akagi.Characters;
using Akagi.Characters.Companions;
using Akagi.Characters.Conversations;
using Akagi.Characters.Routines;
using Akagi.Characters.Upgrades;
using Akagi.Communication;
using Akagi.Communication.Commands;
using Akagi.Life;
using Akagi.Life.Japanese;
using Akagi.LLMs;
using Akagi.Receivers;
using Akagi.Receivers.Commands;
using Akagi.Users;
using MongoDB.Bson;

namespace Akagi.Tests.Support;

internal class InMemoryLifeProfileDatabase : InMemoryDatabase<LifeProfile>, ILifeProfileDatabase
{
    public InMemoryLifeProfileDatabase() : base("life_profiles")
    {
    }

    public Task<LifeProfile?> GetForUser(string userId) => Task.FromResult(Items.FirstOrDefault(p => p.UserId == userId));

    public async Task<LifeProfile> GetOrCreate(string userId)
    {
        LifeProfile? profile = await GetForUser(userId);
        if (profile != null)
        {
            return profile;
        }
        profile = new LifeProfile { UserId = userId };
        await SaveDocumentAsync(profile);
        return profile;
    }
}

internal class InMemoryRoutineDatabase : InMemoryDatabase<Routine>, IRoutineDatabase
{
    public InMemoryRoutineDatabase() : base("routines")
    {
    }

    public Task<List<Routine>> GetRoutines(IEnumerable<string> ids) =>
        Task.FromResult(ids.Select(id => Items.FirstOrDefault(r => r.Id == id)).OfType<Routine>().ToList());

    public Task<List<Routine>> GetOwnedRoutines(string characterId) =>
        Task.FromResult(Items.Where(r => r.OwnerCharacterId == characterId).ToList());

    public Task<List<string>> GetOwnerCharacterIds() =>
        Task.FromResult(Items.Where(r => r.Enabled && !string.IsNullOrEmpty(r.OwnerCharacterId)).Select(r => r.OwnerCharacterId).Distinct().ToList());
}

internal class InMemoryJapaneseDiagnosisDatabase : InMemoryDatabase<JapaneseDiagnosis>, IJapaneseDiagnosisDatabase
{
    public InMemoryJapaneseDiagnosisDatabase() : base("japanese_diagnoses")
    {
    }

    public Task<JapaneseDiagnosis?> GetForUser(string userId) => Task.FromResult(Items.FirstOrDefault(d => d.UserId == userId));

    public async Task<JapaneseDiagnosis> GetOrCreate(string userId)
    {
        JapaneseDiagnosis? diagnosis = await GetForUser(userId);
        if (diagnosis == null)
        {
            diagnosis = new JapaneseDiagnosis { UserId = userId };
            await SaveDocumentAsync(diagnosis);
        }
        return diagnosis;
    }
}

internal class InMemoryCompanionStateDatabase : InMemoryDatabase<CompanionState>, ICompanionStateDatabase
{
    public InMemoryCompanionStateDatabase() : base("companion_states")
    {
    }

    public Task<CompanionState?> Get(string userId, string characterId) =>
        Task.FromResult(Items.FirstOrDefault(s => s.UserId == userId && s.CharacterId == characterId));

    public async Task<CompanionState> GetOrCreate(string userId, string characterId)
    {
        CompanionState? state = await Get(userId, characterId);
        if (state == null)
        {
            state = new CompanionState { UserId = userId, CharacterId = characterId };
            await SaveDocumentAsync(state);
        }
        return state;
    }
}

internal class InMemoryUpgradeProposalDatabase : InMemoryDatabase<UpgradeProposal>, IUpgradeProposalDatabase
{
    public InMemoryUpgradeProposalDatabase() : base("upgrade_proposals")
    {
    }

    public Task<List<UpgradeProposal>> GetForUser(string userId) => Task.FromResult(Items.Where(p => p.UserId == userId).ToList());
}

internal class InMemoryCharacterDatabase : InMemoryDatabase<Character>, ICharacterDatabase
{
    public InMemoryCharacterDatabase() : base("characters")
    {
    }

    public Task<Character?> GetCharacter(string id) => GetDocumentByIdAsync(id);

    public Task<List<Character>> GetCharactersForUser(User user) => Task.FromResult(Items.Where(c => c.UserId == user.Id).ToList());

    public Task<List<Character>> GetCharactersWithRoutines() => Task.FromResult(Items.Where(c => c.RoutineIds.Length > 0).ToList());
}

internal class InMemoryUserDatabase : InMemoryDatabase<User>, IUserDatabase
{
    public InMemoryUserDatabase() : base("users")
    {
    }

    public Task<User?> GetUser(MongoDB.Driver.FilterDefinition<User> user) => throw new NotSupportedException();

    public Task<User?> GetByUsername(string username) => Task.FromResult(Items.FirstOrDefault(u => u.Username == username));
}

internal class RecordingCommunicator : ICommunicator
{
    public List<string> Sent { get; } = [];

    public string Name => "test";
    public Akagi.Communication.Commands.Command[] AvailableCommands => [];

    public Task SendMessage(User user, Character character, string message)
    {
        Sent.Add(message);
        return Task.CompletedTask;
    }

    public Task SendMessage(User user, Character character, Message message)
    {
        Sent.Add(message is TextMessage text ? text.Text : message.ToString() ?? string.Empty);
        return Task.CompletedTask;
    }

    public Task SendMessage(User user, string message) => SendMessage(user, null!, message);

    public Task SendMessage(User user, Message message) => SendMessage(user, null!, message);

    public Task SendAudio(User user, Character character, Stream stream, string fileName) => Task.CompletedTask;

    public Task SendAudio(User user, Stream stream, string fileName) => Task.CompletedTask;
}

internal class NoLLMFactory : ILLMFactory
{
    public Task<ILLM> Create(User user, LLMDefinition? overrideModel, ILLM.LLMUsage usageType) =>
        throw new NotSupportedException("Tests must not call real LLMs.");
}

/// <summary>
/// A user, character and receiver context backed by in-memory databases, for running LLM tool commands directly.
/// </summary>
internal class LifeTestContext
{
    public InMemoryDatabaseFactory Database { get; } = new();
    public InMemoryLifeProfileDatabase Profiles { get; } = new();
    public InMemoryRoutineDatabase Routines { get; } = new();
    public InMemoryUpgradeProposalDatabase Upgrades { get; } = new();
    public InMemoryJapaneseDiagnosisDatabase Diagnoses { get; } = new();
    public InMemoryCompanionStateDatabase Companions { get; } = new();
    public User User { get; } = new() { Id = ObjectId.GenerateNewId().ToString(), Name = "Tester" };
    public Character Character { get; } = new() { Id = ObjectId.GenerateNewId().ToString(), Name = "Akagi" };
    public Conversation Conversation { get; } = new();
    public Context Context { get; }

    public LifeTestContext()
    {
        Database.Register(Profiles);
        Database.Register(Routines);
        Database.Register(Upgrades);
        Database.Register(Diagnoses);
        Database.Register(Companions);
        Context = new Context
        {
            Character = Character,
            Conversation = Conversation,
            User = User,
            Communicator = new RecordingCommunicator(),
            LLMFactory = new NoLLMFactory(),
            DatabaseFactory = Database
        };
    }

    public LifeProfile Profile => Profiles.Items.Single(p => p.UserId == User.Id);

    /// <summary>
    /// Runs a tool command the way the LLM would call it and returns the tool output.
    /// </summary>
    public async Task<string> Run<T>(params (string Name, object Value)[] arguments) where T : Receivers.Commands.Command, new()
    {
        T command = new();
        Argument[] args = command.GetDefaultArguments();
        foreach ((string name, object value) in arguments)
        {
            Argument argument = args.Single(a => a.Name == name);
            argument.Value = value is IFormattable formattable
                ? formattable.ToString(null, System.Globalization.CultureInfo.InvariantCulture)
                : value.ToString()!;
        }
        command.Arguments = args;

        await command.Execute(Context);
        return ((CommandMessage)Conversation.Messages[^1]).Output;
    }
}
