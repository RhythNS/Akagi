using Akagi.Characters;
using Akagi.Characters.Companions;
using Akagi.Characters.Conversations;
using Akagi.Characters.Groups;
using Akagi.Characters.Memories;
using Akagi.Characters.Routines;
using Akagi.Characters.Upgrades;
using Akagi.Life;
using Akagi.Receivers;
using System.Text;

namespace Akagi.Communication.Commands.Groups;

/// <summary>
/// Starts the active group over: the group's and every member's conversations and memory, the memories stored for
/// them, their companion state, the routines they scheduled for themselves and their upgrade proposals. What the graph
/// defines (cards, behaviors, graph routines, global lore) and what calls produced (call analyses, people, the
/// Japanese diagnosis) stay. Without "confirm" it only says what it would remove.
/// </summary>
internal class ResetGroupCommand : TextCommand
{
    public override string Name => "/resetGroup";

    public override string Description =>
        "Starts the active group over: conversations, memories, companion state, self-made routines and upgrade proposals of the group and its members. " +
        "Shows what it would remove unless confirmed; --life also empties your life profile except the time zone. Usage: /resetGroup [confirm] [--life]";

    internal record MemberReset(string Name, int Conversations, int Memories, bool CompanionState, int Routines, int Proposals);

    public override async Task<CommandResult> ExecuteAsync(Context context, string[] args)
    {
        if (context.Group == null)
        {
            await Communicator.SendMessage(context.User, "No active group. Use /switchToGroup first.");
            return CommandResult.Fail("No active group.");
        }

        bool confirmed = args.Any(a => string.Equals(a, "confirm", StringComparison.OrdinalIgnoreCase));
        bool life = args.Any(a => string.Equals(a, "--life", StringComparison.OrdinalIgnoreCase));
        string userId = context.User.Id!;
        CharacterGroup group = context.Group;

        List<Character> members = await LoadMembersAsync(context, group);
        if (confirmed)
        {
            // A run still going would write its replies into the emptied conversations
            Receiver.TryCancelGroupRun(userId, group.Id!);
        }

        int groupConversations = group.Conversations.List.Count;
        List<MemberReset> resets = [];
        foreach (Character member in members)
        {
            resets.Add(await ResetMemberAsync(context, member, userId, confirmed));
        }
        bool lifeProfile = life && await ResetLifeAsync(context, userId, confirmed);

        if (confirmed)
        {
            group.Conversations = new ConversationContainer();
            group.SharedMemory = new Memory();
            // The active character is saved from the context when the command ends; the other members are saved here
            foreach (Character member in members.Where(m => !ReferenceEquals(m, context.Character)))
            {
                await context.DatabaseFactory.GetDatabase<ICharacterDatabase>().SaveDocumentAsync(member);
            }
        }

        await Communicator.SendMessage(context.User, Describe(group.Name, groupConversations, resets, life, lifeProfile, confirmed));
        return CommandResult.Ok;
    }

    /// <summary>
    /// The members, with the active character's own instance in place of a fresh copy, so the copy the context saves at
    /// the end is the reset one.
    /// </summary>
    private static async Task<List<Character>> LoadMembersAsync(Context context, CharacterGroup group)
    {
        ICharacterDatabase characters = context.DatabaseFactory.GetDatabase<ICharacterDatabase>();
        List<Character> members = [];
        foreach (string memberId in group.MemberCharacterIds.Distinct())
        {
            Character? member = context.Character?.Id == memberId ? context.Character : await characters.GetCharacter(memberId);
            if (member != null)
            {
                members.Add(member);
            }
        }
        return members;
    }

    private static async Task<MemberReset> ResetMemberAsync(Context context, Character member, string userId, bool apply)
    {
        string characterId = member.Id!;

        IMemoryDatabase memoryDatabase = context.DatabaseFactory.GetDatabase<IMemoryDatabase>();
        // Global lore has no user and belongs to the character's definition
        List<MemoryRecord> memories = [.. (await memoryDatabase.GetAllMemoriesAsync(characterId, userId)).Where(m => m.UserId == userId)];

        ICompanionStateDatabase companions = context.DatabaseFactory.GetDatabase<ICompanionStateDatabase>();
        CompanionState? state = await companions.Get(userId, characterId);

        // Graph routines are attached through Character.RoutineIds and stay; the ones it scheduled itself go
        IRoutineDatabase routineDatabase = context.DatabaseFactory.GetDatabase<IRoutineDatabase>();
        List<Routine> routines = [.. (await routineDatabase.GetOwnedRoutines(characterId)).Where(r => r.UserId == userId || r.UserId.Length == 0)];

        IUpgradeProposalDatabase proposalDatabase = context.DatabaseFactory.GetDatabase<IUpgradeProposalDatabase>();
        List<UpgradeProposal> proposals = [.. (await proposalDatabase.GetForUser(userId)).Where(p => p.CharacterId == characterId)];

        MemberReset reset = new(member.Name, member.Conversations.List.Count, memories.Count, state != null, routines.Count, proposals.Count);
        if (!apply)
        {
            return reset;
        }

        member.Conversations = new ConversationContainer();
        member.Memory = new Memory();
        foreach (MemoryRecord memory in memories)
        {
            await memoryDatabase.DeleteDocumentByIdAsync(memory.Id!);
        }
        if (state != null)
        {
            await companions.DeleteDocumentByIdAsync(state.Id!);
        }
        foreach (Routine routine in routines)
        {
            await routineDatabase.DeleteDocumentByIdAsync(routine.Id!);
        }
        foreach (UpgradeProposal proposal in proposals)
        {
            await proposalDatabase.DeleteDocumentByIdAsync(proposal.Id!);
        }
        return reset;
    }

    /// <summary>
    /// Empties the life profile but keeps the time zone, so times stay right without setting it again.
    /// </summary>
    private static async Task<bool> ResetLifeAsync(Context context, string userId, bool apply)
    {
        ILifeProfileDatabase profiles = context.DatabaseFactory.GetDatabase<ILifeProfileDatabase>();
        LifeProfile? profile = await profiles.GetForUser(userId);
        if (profile == null)
        {
            return false;
        }
        if (apply)
        {
            await profiles.DeleteDocumentByIdAsync(profile.Id!);
            if (profile.TimeZoneId.Length > 0)
            {
                await profiles.SaveDocumentAsync(new LifeProfile { UserId = userId, TimeZoneId = profile.TimeZoneId });
            }
        }
        return true;
    }

    internal static string Describe(string groupName, int groupConversations, IReadOnlyList<MemberReset> members, bool life, bool lifeProfile, bool done)
    {
        StringBuilder text = new();
        text.AppendLine(done ? $"Reset group {groupName}:" : $"/resetGroup confirm would reset group {groupName}:");
        text.AppendLine($"- group: {Count(groupConversations, "conversation")} and its shared memory");
        foreach (MemberReset member in members)
        {
            text.AppendLine($"- {member.Name}: {Count(member.Conversations, "conversation")}, its memory, {Count(member.Memories, "stored memory", "stored memories")}, " +
                $"{(member.CompanionState ? "its companion state" : "no companion state")}, {Count(member.Routines, "self-made routine")}, {Count(member.Proposals, "upgrade proposal")}");
        }
        if (life)
        {
            text.AppendLine(lifeProfile ? "- your life profile (mood, concerns, health days, work log, study), keeping the time zone" : "- no life profile to reset");
        }
        text.AppendLine();
        text.Append(done
            ? "Kept: the graph (cards, behaviors, graph routines, global lore), checkpoints, and everything from calls (analyses, people, Japanese diagnosis)."
            : "Nothing was changed. Kept either way: the graph (cards, behaviors, graph routines, global lore), checkpoints, and everything from calls." +
              (life ? string.Empty : " Add --life to also empty your life profile."));
        return text.ToString();
    }

    private static string Count(int count, string one, string? many = null) =>
        $"{count} {(count == 1 ? one : many ?? one + "s")}";
}
