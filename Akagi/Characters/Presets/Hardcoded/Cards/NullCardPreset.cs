using Akagi.Characters.Cards;
using Akagi.Data;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Akagi.Characters.Presets.Hardcoded.Cards;

internal class NullCardPreset : Preset
{
    // A simple 1x1 black PNG image with a null character embedded as a base64 string
    private static readonly string Data =
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAAAXNSR0IArs4c6QAAAARnQU1BAACxjwv8YQUAAAAJcEhZcwAADsMAAA7DAcdvqGQAAAANSURBVBhXY/D09PwPAAOVAds+rnHYAAABrnRFWHRjaGFyYQBleUp6Y0dWaklqb2lZMmhoY21GZlkyRnlaRjkyTWlJc0luTndaV05mZG1WeWMybHZiaUk2SWpJdU1DSXNJbVJoZEdFaU9uc2libUZ0WlNJNklrNTFiR3dpTENKa1pYTmpjbWx3ZEdsdmJpSTZJazUxYkd3aUxDSndaWEp6YjI1aGJHbDBlU0k2SWlJc0luTmpaVzVoY21sdklqb2lJaXdpWm1seWMzUmZiV1Z6SWpvaVRuVnNiQ0lzSW0xbGMxOWxlR0Z0Y0d4bElqb2lJaXdpWTNKbFlYUnZjbDl1YjNSbGN5STZJaUlzSW5ONWMzUmxiVjl3Y205dGNIUWlPaUlpTENKd2IzTjBYMmhwYzNSdmNubGZhVzV6ZEhKMVkzUnBiMjV6SWpvaUlpd2lZV3gwWlhKdVlYUmxYMmR5WldWMGFXNW5jeUk2VzEwc0luUmhaM01pT2x0ZExDSmpjbVZoZEc5eUlqb2lUblZzYkNJc0ltTm9ZWEpoWTNSbGNsOTJaWEp6YVc5dUlqb2lJaXdpWlhoMFpXNXphVzl1Y3lJNmUzMTlmUT09eFR2ZAAAAABJRU5ErkJggg==";

    private string cardId = string.Empty;

    [BsonRepresentation(BsonType.ObjectId)]
    public string CardId
    {
        get => cardId;
        set => SetProperty(ref cardId, value);
    }

    protected override async Task CreateInnerAsync(IDatabaseFactory databaseFactory)
    {
        if (string.IsNullOrEmpty(CardId) == false)
        {
            return; // TODO: Maybe do an overwrite in the future instead
        }

        byte[] imageBytes = Convert.FromBase64String(Data);
        using MemoryStream imageStream = new(imageBytes);

        Card? card = await databaseFactory.GetDatabase<CardDatabase>().SaveCardFromImage(imageStream);

        if (card == null)
        {
            throw new Exception("Failed to create NullCardPreset card.");
        }

        CardId = card.Id!;
    }
}
