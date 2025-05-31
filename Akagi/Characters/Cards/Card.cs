using Akagi.Data;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Akagi.Characters.Cards;

internal class Card : Savable
{
    private string _name = string.Empty;
    private string _description = string.Empty;
    private string _personality = string.Empty;
    private string _firstMes = string.Empty;
    private string _mesExample = string.Empty;
    private string _scenario = string.Empty;
    private string _creatorNotes = string.Empty;
    private string _systemPrompt = string.Empty;
    private string _postHistoryInstructions = string.Empty;
    private string[] _alternateGreetings = [];
    private string[] _tags = [];
    private string _creator = string.Empty;
    private string _characterVersion = string.Empty;
    private string _imageId = string.Empty;
    private string _rawCardBase64 = string.Empty;

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }
    public string Description
    {
        get => _description;
        set => SetProperty(ref _description, value);
    }
    public string Personality
    {
        get => _personality;
        set => SetProperty(ref _personality, value);
    }
    public string FirstMes
    {
        get => _firstMes;
        set => SetProperty(ref _firstMes, value);
    }
    public string MesExample
    {
        get => _mesExample;
        set => SetProperty(ref _mesExample, value);
    }
    public string Scenario
    {
        get => _scenario;
        set => SetProperty(ref _scenario, value);
    }
    public string CreatorNotes
    {
        get => _creatorNotes;
        set => SetProperty(ref _creatorNotes, value);
    }
    public string SystemPrompt
    {
        get => _systemPrompt;
        set => SetProperty(ref _systemPrompt, value);
    }
    public string PostHistoryInstructions
    {
        get => _postHistoryInstructions;
        set => SetProperty(ref _postHistoryInstructions, value);
    }
    public string[] AlternateGreetings
    {
        get => _alternateGreetings;
        set => SetProperty(ref _alternateGreetings, value);
    }
    public string[] Tags
    {
        get => _tags;
        set => SetProperty(ref _tags, value);
    }
    public string Creator
    {
        get => _creator;
        set => SetProperty(ref _creator, value);
    }
    public string CharacterVersion
    {
        get => _characterVersion;
        set => SetProperty(ref _characterVersion, value);
    }
    [BsonRepresentation(BsonType.ObjectId)]
    public string ImageId
    {
        get => _imageId;
        set => SetProperty(ref _imageId, value);
    }
    public string RawCardBase64
    {
        get => _rawCardBase64;
        set => SetProperty(ref _rawCardBase64, value);
    }

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
