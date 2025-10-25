namespace Akagi.Web.Models.Chat;

public class Character
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string CardId { get; set; } = string.Empty;
    public DateTime? LastMessageTime { get; set; } = DateTime.MinValue;

    public static Character FromBridge(Bridge.Chat.Models.Character character)
    {
        return new Character
        {
            Id = character.Id,
            Name = character.Name,
            CardId = character.CardId,
            LastMessageTime = character.LastMessageTime,
        };
    }
}
