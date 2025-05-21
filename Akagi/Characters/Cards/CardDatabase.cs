using Akagi.Data;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using System.Text;
using System.Text.Json;

namespace Akagi.Characters.Cards;

internal class CardDatabase : Database<Card>, ICardDatabase
{
    private readonly IFileDatabase fileDatabase;

    public CardDatabase(IFileDatabase fileDatabase, IOptionsMonitor<DatabaseOptions> options) : base(options, "cards")
    {
        this.fileDatabase = fileDatabase;
    }

    public async Task<bool> SaveCardFromImage(MemoryStream stream)
    {
        string? character = GetPngTextChunk(stream, "chara");
        if (character == null)
        {
            return false;
        }

        byte[] bytes = Convert.FromBase64String(character);
        string decodedString = Encoding.UTF8.GetString(bytes);

        RawCard? rawCard = JsonSerializer.Deserialize<RawCard>(decodedString);
        if (rawCard == null)
        {
            return false;
        }

        string fileName = Guid.NewGuid().ToString() + ".png";
        stream.Seek(0, SeekOrigin.Begin);
        ObjectId objectId = await fileDatabase.UploadFileAsync(stream, fileName, "image/png");

        Card card = Card.FromRawCard(rawCard, character, objectId.ToString());
        await SaveDocumentAsync(card);

        return true;
    }

    private static string? GetPngTextChunk(MemoryStream stream, string keyword)
    {
        stream.Seek(0, SeekOrigin.Begin);

        byte[] signature = new byte[8];
        stream.Read(signature, 0, 8);

        while (stream.Position < stream.Length)
        {
            byte[] lengthBytes = new byte[4];
            if (stream.Read(lengthBytes, 0, 4) != 4)
            {
                break;
            }
            int length = BitConverter.ToInt32(lengthBytes.Reverse().ToArray(), 0);

            byte[] typeBytes = new byte[4];
            if (stream.Read(typeBytes, 0, 4) != 4)
            {
                break;
            }
            string chunkType = Encoding.ASCII.GetString(typeBytes);

            byte[] data = new byte[length];
            if (stream.Read(data, 0, length) != length)
            {
                break;
            }

            stream.Seek(4, SeekOrigin.Current);

            if (chunkType != "tEXt")
            {
                continue;
            }
            int nullIndex = Array.IndexOf(data, (byte)0);
            if (nullIndex <= 0)
            {
                continue;
            }
            string foundKeyword = Encoding.ASCII.GetString(data, 0, nullIndex);
            if (foundKeyword != keyword)
            {
                continue;
            }
            string text = Encoding.ASCII.GetString(data, nullIndex + 1, data.Length - nullIndex - 1);
            return text;
        }

        return null;
    }
}
