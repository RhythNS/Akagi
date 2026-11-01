namespace Akagi.Web.Models.Chat;

public class Character
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string CardId { get; set; } = string.Empty;
    public DateTime? LastMessageTime { get; set; } = DateTime.MinValue;
    public bool IsGroup { get; set; }
    public string[] MemberIds { get; set; } = [];
    public bool HasImage { get; set; }
    public bool IsTutor { get; set; }

    /// <summary>
    /// Whether a message tagged with <paramref name="characterId"/> belongs to this chat. Group members answer with their own ids.
    /// </summary>
    public bool Owns(string characterId) =>
        string.Equals(characterId, Id, StringComparison.OrdinalIgnoreCase) ||
        (IsGroup && MemberIds.Contains(characterId, StringComparer.OrdinalIgnoreCase));

    public static Character FromBridge(Bridge.Chat.Models.Character character)
    {
        return new Character
        {
            Id = character.Id,
            Name = character.Name,
            CardId = character.CardId,
            LastMessageTime = character.LastMessageTime,
            IsGroup = character.IsGroup,
            MemberIds = character.MemberIds ?? [],
            HasImage = character.HasImage,
            IsTutor = character.IsTutor,
        };
    }
}
