using Akagi.Data;
using MongoDB.Bson.Serialization.Attributes;

namespace Akagi.Graphs;

internal class GraphInstance : Savable
{
    public class SavableInfo
    {
        [BsonRepresentation(MongoDB.Bson.BsonType.ObjectId)]
        public string SavableId { get; set; } = string.Empty;
        public required string CollectionName { get; set; }
    }

    private string _graphId = string.Empty;
    private string _userId = string.Empty;
    private string _name = string.Empty;
    private SavableInfo[] _savableInfos = [];

    public string GraphId
    {
        get => _graphId;
        set => SetProperty(ref _graphId, value);
    }

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    [BsonRepresentation(MongoDB.Bson.BsonType.ObjectId)]
    public string UserId
    {
        get => _userId;
        set => SetProperty(ref _userId, value);
    }

    public SavableInfo[] SavableInfos
    {
        get => _savableInfos;
        set => SetProperty(ref _savableInfos, value);
    }
}
