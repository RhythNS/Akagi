using Akagi.Web.Data;
using MongoDB.Bson.Serialization.Attributes;
using System.ComponentModel.DataAnnotations;

namespace Akagi.Web.Models.TimeTrackers;

public class Definition : Savable
{
    public required string Name { get; set; }

    [BsonRepresentation(MongoDB.Bson.BsonType.ObjectId)]
    public required string UserId { get; set; }

    public List<FieldDefinition> Fields { get; set; } = [];
}

public class DefinitionModel
{
    [Required]
    [StringLength(100, MinimumLength = 1)]
    [UniqueDefinitionName]
    public string? Name { get; set; }
    [Required]
    [MinLength(1, ErrorMessage = "A definition must have at least one field.")]
    public List<FieldDefinition> Fields { get; set; } = [];
}

public class FieldDefinition
{
    public required string Name { get; set; }
    public FieldType Type { get; set; }
}

public enum FieldType
{
    Boolean,
    Int,
    Float,
    Text,
    DateTime,
    Time,
    Date
}
