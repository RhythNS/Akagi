using Akagi.Web.Data;
using System.Text.Json.Serialization;

namespace Akagi.Web.Models;

public class User : Savable
{
    [JsonPropertyName("googleId")]
    public required string GoogleId { get; set; }

    [JsonIgnore]
    public string? GoogleToken { get; set; }

    [JsonPropertyName("name")]
    public required string Name { get; set; }

    [JsonPropertyName("email")]
    public required string Email { get; set; }

    public bool Valid { get; set; } = false;
}
