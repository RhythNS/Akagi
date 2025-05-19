using Akagi.Data;

namespace Akagi.Characters.Cards;

internal interface ICardDatabase : IDatabase<Card>
{
    public Task<bool> SaveCardFromImage(MemoryStream memoryStream);
}
