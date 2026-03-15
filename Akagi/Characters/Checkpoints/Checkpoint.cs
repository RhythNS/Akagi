using Akagi.Characters.Memories;
using Akagi.Data;

namespace Akagi.Characters.Checkpoints;

internal class Checkpoint : Savable
{
    private DateTime _createdOn;
    private string _characterId = string.Empty;
    private string _characterName = string.Empty;
    private List<Conversation> _conversations = [];
    private Memory _memory = new();

    public override bool Dirty
    {
        get => base.Dirty || _memory.Dirty || _conversations.Any(c => c.Dirty);
        set
        {
            base.Dirty = value;
            if (value == false)
            {
                _memory.Dirty = false;
                _conversations.ForEach(c => c.Dirty = false);
            }
        }
    }

    public DateTime CreatedOn
    {
        get => _createdOn;
        set => SetProperty(ref _createdOn, value);
    }
    public string CharacterId
    {
        get => _characterId;
        set => SetProperty(ref _characterId, value);
    }
    public string CharacterName
    {
        get => _characterName;
        set => SetProperty(ref _characterName, value);
    }
    public List<Conversation> Conversations
    {
        get => _conversations;
        set => SetProperty(ref _conversations, value);
    }
    public Memory Memory
    {
        get => _memory;
        set => SetProperty(ref _memory, value);
    }

    public static Checkpoint CreateFromCharacter(Character character)
    {
        return new Checkpoint
        {
            CreatedOn = DateTime.UtcNow,
            CharacterId = character.Id!,
            CharacterName = character.Name,
            Conversations = character.Conversations.Select(c => c.Copy()).ToList(),
            Memory = character.Memory.Copy(),
        };
    }

    public void ApplyToCharacter(Character character)
    {
        character.Conversations = Conversations.Select(c => c.Copy()).ToList();
        character.Memory = Memory.Copy();
    }
}
