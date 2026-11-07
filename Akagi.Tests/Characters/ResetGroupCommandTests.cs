using Akagi.Characters;
using Akagi.Characters.Companions;
using Akagi.Characters.Conversations;
using Akagi.Characters.Groups;
using Akagi.Characters.Memories;
using Akagi.Characters.Routines;
using Akagi.Characters.Upgrades;
using Akagi.Communication.Commands;
using Akagi.Communication.Commands.Groups;
using Akagi.Life;
using Akagi.Life.Mood;
using Akagi.Tests.Support;
using Akagi.Users;
using MongoDB.Bson;

namespace Akagi.Tests.Characters;

public class ResetGroupCommandTests
{
    private readonly InMemoryDatabaseFactory _database = new();
    private readonly InMemoryCharacterDatabase _characters = new();
    private readonly InMemoryMemoryDatabase _memories = new();
    private readonly InMemoryCompanionStateDatabase _companions = new();
    private readonly InMemoryRoutineDatabase _routines = new();
    private readonly InMemoryUpgradeProposalDatabase _proposals = new();
    private readonly InMemoryLifeProfileDatabase _profiles = new();
    private readonly RecordingCommunicator _communicator = new();
    private readonly User _user = new() { Id = ObjectId.GenerateNewId().ToString(), Name = "Tester" };
    private readonly string _otherUser = ObjectId.GenerateNewId().ToString();
    private readonly Character _akagi;
    private readonly Character _sensei;
    private readonly CharacterGroup _group;

    public ResetGroupCommandTests()
    {
        _database.Register(_characters);
        _database.Register(_memories);
        _database.Register(_companions);
        _database.Register(_routines);
        _database.Register(_proposals);
        _database.Register(_profiles);

        _akagi = Member("Akagi");
        _sensei = Member("Sensei");
        _group = new CharacterGroup { Id = ObjectId.GenerateNewId().ToString(), Name = "Companion", UserId = _user.Id!, MemberCharacterIds = [_akagi.Id!, _sensei.Id!] };
        _group.Conversations.GetCurrentConversation();
        _group.SharedMemory.ShortTerm.AddThought(new SingleFactThought { Fact = "He has an exam on Friday." });

        foreach (Character member in new[] { _akagi, _sensei })
        {
            _memories.SaveDocumentAsync(new MemoryRecord { CharacterId = member.Id!, UserId = _user.Id }).Wait();
            _companions.SaveDocumentAsync(new CompanionState { UserId = _user.Id!, CharacterId = member.Id! }).Wait();
        }
        // Lore of the character's definition and another user's memory stay
        _memories.SaveDocumentAsync(new MemoryRecord { CharacterId = _akagi.Id!, UserId = null }).Wait();
        _memories.SaveDocumentAsync(new MemoryRecord { CharacterId = _akagi.Id!, UserId = _otherUser }).Wait();
        // A check-in she scheduled herself goes; a routine from the graph stays
        _routines.SaveDocumentAsync(new FollowUpRoutine { UserId = _user.Id!, OwnerCharacterId = _akagi.Id!, CreatedBySelf = true }).Wait();
        _routines.SaveDocumentAsync(new DailyRoutine { UserId = _user.Id! }).Wait();
        _proposals.SaveDocumentAsync(new UpgradeProposal { UserId = _user.Id!, CharacterId = _akagi.Id! }).Wait();

        LifeProfile profile = new() { UserId = _user.Id!, TimeZoneId = "Europe/Berlin" };
        profile.AddMood(new MoodEntry());
        _profiles.SaveDocumentAsync(profile).Wait();
    }

    private Character Member(string name)
    {
        Character character = new() { Id = ObjectId.GenerateNewId().ToString(), Name = name };
        character.Conversations.GetCurrentConversation();
        character.Memory.LongTerm.AddThought(new SingleFactThought { Fact = "He likes ramen." });
        _characters.SaveDocumentAsync(character).Wait();
        return character;
    }

    private async Task<string> Run(Character? active, params string[] args)
    {
        ResetGroupCommand command = new();
        command.Init(_communicator);
        CommandResult result = await command.ExecuteAsync(new Command.Context { User = _user, Group = _group, Character = active, DatabaseFactory = _database }, args);
        Assert.True(result.Success);
        return _communicator.Sent.Last();
    }

    [Fact]
    public async Task WithoutConfirm_OnlySaysWhatWouldGo()
    {
        string message = await Run(null);

        Assert.Contains("/resetGroup confirm would reset group Companion", message);
        Assert.Contains("- Akagi: 1 conversation, its memory, 1 stored memory, its companion state, 1 self-made routine, 1 upgrade proposal", message);
        Assert.Contains("Nothing was changed", message);
        Assert.Single(_group.Conversations.List);
        Assert.Equal(4, _memories.Items.Count());
        Assert.Equal(2, _routines.Items.Count());
        Assert.Single(_profiles.Items.Single().Mood);
    }

    [Fact]
    public async Task Confirm_EmptiesTheGroupAndItsMembers_AndKeepsTheGraphsPart()
    {
        string message = await Run(null, "confirm");

        Assert.StartsWith("Reset group Companion:", message);
        Assert.Empty(_group.Conversations.List);
        Assert.Empty(_group.SharedMemory.ShortTerm.Thoughts);
        foreach (Character member in _characters.Items)
        {
            Assert.Empty(member.Conversations.List);
            Assert.Empty(member.Memory.LongTerm.Thoughts);
        }
        Assert.Equal([null, _otherUser], _memories.Items.Select(m => m.UserId));
        Assert.Empty(_companions.Items);
        Assert.IsType<DailyRoutine>(Assert.Single(_routines.Items));
        Assert.Empty(_proposals.Items);
        // The life profile only goes when asked
        Assert.Single(_profiles.Items.Single().Mood);
    }

    [Fact]
    public async Task Confirm_ResetsTheActiveCharactersOwnInstance()
    {
        // The context saves this instance when the command ends, so a fresh copy reset instead would be overwritten
        Character active = (await _characters.GetCharacter(_akagi.Id!))!;

        await Run(active, "confirm");

        Assert.Empty(active.Conversations.List);
        Assert.Empty(active.Memory.LongTerm.Thoughts);
    }

    [Fact]
    public async Task Life_EmptiesTheProfileButKeepsTheTimeZone()
    {
        string message = await Run(null, "confirm", "--life");

        LifeProfile profile = Assert.Single(_profiles.Items);
        Assert.Empty(profile.Mood);
        Assert.Equal("Europe/Berlin", profile.TimeZoneId);
        Assert.Equal(_user.Id, profile.UserId);
        Assert.Contains("your life profile", message);
    }
}
