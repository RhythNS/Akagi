using Akagi.Data;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Akagi.Characters.Cards;

internal class Card : Savable
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Personality { get; set; } = string.Empty;
    public string FirstMes { get; set; } = string.Empty;
    public string MesExample { get; set; } = string.Empty;
    public string Scenario { get; set; } = string.Empty;
    public string CreatorNotes { get; set; } = string.Empty;
    public string SystemPrompt { get; set; } = string.Empty;
    public string PostHistoryInstructions { get; set; } = string.Empty;
    public string[] AlternateGreetings { get; set; } = [];
    public string[] Tags { get; set; } = [];
    public string Creator { get; set; } = string.Empty;
    public string CharacterVersion { get; set; } = string.Empty;

    [BsonRepresentation(BsonType.ObjectId)]
    public string ImageId { get; set; } = string.Empty;

    public string RawCardBase64 { get; set; } = string.Empty;

    public static Card FromRawCard(RawCard rawCard, string rawCardBase64, string imageId)
    {
        return new Card
        {
            Name = rawCard.Data.Name,
            Description = rawCard.Data.Description,
            Personality = rawCard.Data.Personality,
            FirstMes = rawCard.Data.FirstMes,
            MesExample = rawCard.Data.MesExample,
            Scenario = rawCard.Data.Scenario,
            CreatorNotes = rawCard.Data.CreatorNotes,
            SystemPrompt = rawCard.Data.SystemPrompt,
            PostHistoryInstructions = rawCard.Data.PostHistoryInstructions,
            AlternateGreetings = rawCard.Data.AlternateGreetings,
            Tags = rawCard.Data.Tags,
            Creator = rawCard.Data.Creator,
            CharacterVersion = rawCard.Data.CharacterVersion,
            ImageId = imageId,
            RawCardBase64 = rawCardBase64
        };
    }

    public string[] GetGreetings()
    {
        if (AlternateGreetings.Length == 0)
        {
            return [FirstMes];
        }
        string[] greetings = new string[AlternateGreetings.Length + 1];
        greetings[0] = FirstMes;
        for (int i = 0; i < AlternateGreetings.Length; i++)
        {
            greetings[i + 1] = AlternateGreetings[i];
        }
        return greetings;
    }

    public bool TryGetGreeting(out string greeting, int index)
    {
        greeting = string.Empty;
        if (index < 0)
        {
            return false;
        }
        else if (index == 0)
        {
            greeting = FirstMes;
            return true;
        }
        else if (index <= AlternateGreetings.Length)
        {
            greeting = AlternateGreetings[index - 1];
            return true;
        }
        else
        {
            return false;
        }
    }
}
