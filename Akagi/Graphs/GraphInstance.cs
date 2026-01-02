using Akagi.Data;
using MongoDB.Bson.Serialization.Attributes;

namespace Akagi.Graphs;

internal class GraphInstance : Savable
{
    private string _graphId = string.Empty;
    private string _userId = string.Empty;
    private string[] _savableIds = [];

    public string GraphId
    {
        get => _graphId;
        set => SetProperty(ref _graphId, value);
    }

    [BsonRepresentation(MongoDB.Bson.BsonType.ObjectId)]
    public string UserId
    {
        get => _userId;
        set => SetProperty(ref _userId, value);
    }

    [BsonRepresentation(MongoDB.Bson.BsonType.ObjectId)]
    public string[] SavableIds
    {
        get => _savableIds;
        set => SetProperty(ref _savableIds, value);
    }
}
