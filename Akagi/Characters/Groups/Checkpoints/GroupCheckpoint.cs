using Akagi.Characters.Conversations;
using Akagi.Characters.Memories;
using Akagi.Data;

namespace Akagi.Characters.Groups.Checkpoints;

internal class MemberSnapshot
{
    public string CharacterId { get; set; } = string.Empty;
    public string CharacterName { get; set; } = string.Empty;
    public ConversationContainer Conversations { get; set; } = new();
    public Memory Memory { get; set; } = new();
}

internal class GroupCheckpoint : Savable
{
    private DateTime _createdOn;
    private string _groupId = string.Empty;
    private string _groupName = string.Empty;
    private ConversationContainer _groupConversations = new();
    private Memory _sharedMemory = new();
    private List<MemberSnapshot> _memberSnapshots = [];

    public override bool Dirty
    {
        get => base.Dirty || _sharedMemory.Dirty || _groupConversations.Dirty;
        set
        {
            base.Dirty = value;
            if (value == false)
            {
                _sharedMemory.Dirty = false;
                _groupConversations.Dirty = false;
            }
        }
    }

    public DateTime CreatedOn
    {
        get => _createdOn;
        set => SetProperty(ref _createdOn, value);
    }
    public string GroupId
    {
        get => _groupId;
        set => SetProperty(ref _groupId, value);
    }
    public string GroupName
    {
        get => _groupName;
        set => SetProperty(ref _groupName, value);
    }
    public ConversationContainer GroupConversations
    {
        get => _groupConversations;
        set => SetProperty(ref _groupConversations, value);
    }
    public Memory SharedMemory
    {
        get => _sharedMemory;
        set => SetProperty(ref _sharedMemory, value);
    }
    public List<MemberSnapshot> MemberSnapshots
    {
        get => _memberSnapshots;
        set => SetProperty(ref _memberSnapshots, value);
    }

    public static GroupCheckpoint CreateFromGroup(CharacterGroup group, IReadOnlyList<Character> members)
    {
        return new GroupCheckpoint
        {
            CreatedOn = DateTime.UtcNow,
            GroupId = group.Id!,
            GroupName = group.Name,
            GroupConversations = group.Conversations.Copy(),
            SharedMemory = group.SharedMemory.Copy(),
            MemberSnapshots = members.Select(m => new MemberSnapshot
            {
                CharacterId = m.Id!,
                CharacterName = m.Name,
                Conversations = m.Conversations.Copy(),
                Memory = m.Memory.Copy(),
            }).ToList(),
        };
    }

    public void ApplyToGroup(CharacterGroup group, IReadOnlyList<Character> members)
    {
        group.Conversations = GroupConversations.Copy();
        group.SharedMemory = SharedMemory.Copy();

        foreach (MemberSnapshot snapshot in MemberSnapshots)
        {
            Character? member = members.FirstOrDefault(m => m.Id == snapshot.CharacterId);
            if (member != null)
            {
                member.Conversations = snapshot.Conversations.Copy();
                member.Memory = snapshot.Memory.Copy();
            }
        }
    }
}
