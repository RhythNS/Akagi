using Akagi.Characters.Cards;
using Akagi.Communication.Commands.ActiveCharacters;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;

namespace Akagi.Tests.Characters;

public class CardTests
{
    [Fact]
    public void CardWithoutImage_StoresNullAndReadsBackEmpty()
    {
        Card card = new() { Name = "Hana" };

        BsonDocument document = card.ToBsonDocument();

        Assert.True(document["ImageId"].IsBsonNull);
        Assert.Equal(string.Empty, BsonSerializer.Deserialize<Card>(document).ImageId);
    }

    [Fact]
    public void CardWithImage_StoresObjectId()
    {
        string imageId = ObjectId.GenerateNewId().ToString();
        Card card = new() { Name = "Akagi", ImageId = imageId };

        BsonDocument document = card.ToBsonDocument();

        Assert.Equal(BsonType.ObjectId, document["ImageId"].BsonType);
        Assert.Equal(imageId, BsonSerializer.Deserialize<Card>(document).ImageId);
    }

    [Fact]
    public void CardWithInvalidImageId_StillFails()
    {
        Card card = new() { ImageId = "not-an-id" };

        Assert.ThrowsAny<Exception>(() => card.ToBsonDocument());
    }

    [Theory]
    [InlineData(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, "image/png")]
    [InlineData(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10 }, "image/jpeg")]
    [InlineData(new byte[] { 0x47, 0x49, 0x46, 0x38, 0x39, 0x61 }, "image/gif")]
    [InlineData(new byte[] { 0x52, 0x49, 0x46, 0x46, 0, 0, 0, 0, 0x57, 0x45, 0x42, 0x50 }, "image/webp")]
    [InlineData(new byte[] { 0x7B, 0x22, 0x47, 0x72 }, null)]
    public void ImageContentType_RecognizesPicturesByTheirHeader(byte[] header, string? expected)
    {
        using MemoryStream stream = new(header);

        Assert.Equal(expected, CardImageCommand.ImageContentType(stream));
        Assert.Equal(0, stream.Position);
    }
}
