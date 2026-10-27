using Akagi.Web.Data;
using Akagi.Web.Models;
using Akagi.Web.Models.TimeTrackers;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;
using System.Globalization;
using System.Text.Json;

namespace Akagi.Web.Controllers;

[ApiController]
[Route("TimeTracker")]
public class TimeTrackerController : ControllerBase
{
    private readonly IEntryDatabase _entryDatabase;
    private readonly IDefinitionDatabase _definitionDatabase;
    private readonly IUserDatabase _userDatabase;
    private readonly IConfiguration _configuration;

    public TimeTrackerController(
        IEntryDatabase entryDatabase,
        IDefinitionDatabase definitionDatabase,
        IUserDatabase userDatabase,
        IConfiguration configuration)
    {
        _entryDatabase = entryDatabase;
        _definitionDatabase = definitionDatabase;
        _userDatabase = userDatabase;
        _configuration = configuration;
    }

    private bool IsValidApiKey()
    {
        string? providedKey = Request.Headers["X-Api-Key"].FirstOrDefault();
        if (string.IsNullOrEmpty(providedKey))
        {
            return false;
        }

        string? validKey = _configuration["TimeTracker:ApiKey"];
        return !string.IsNullOrEmpty(validKey) && providedKey == validKey;
    }

    private async Task<User?> GetUserByGoogleIdAsync(string googleId)
    {
        FilterDefinition<User> filter = Builders<User>.Filter.Eq(u => u.GoogleId, googleId);
        List<User> users = await _userDatabase.GetDocumentsByPredicateAsync(filter);
        return users.FirstOrDefault();
    }

    [HttpGet("Definitions")]
    public async Task<ActionResult<List<Definition>>> GetDefinitions([FromQuery] string googleId)
    {
        if (!IsValidApiKey())
        {
            return Unauthorized("Invalid or missing API key.");
        }

        if (string.IsNullOrEmpty(googleId))
        {
            return BadRequest("GoogleId is required.");
        }

        User? user = await GetUserByGoogleIdAsync(googleId);
        if (user == null || string.IsNullOrEmpty(user.Id))
        {
            return NotFound("User not found.");
        }

        FilterDefinition<Definition> filter = Builders<Definition>.Filter.Eq(d => d.UserId, user.Id);
        List<Definition> definitions = await _definitionDatabase.GetDocumentsByPredicateAsync(filter);

        return Ok(definitions);
    }

    [HttpGet("Entries")]
    public async Task<ActionResult<List<Entry>>> GetEntries([FromQuery] string googleId, [FromQuery] string? definitionId = null)
    {
        if (!IsValidApiKey())
        {
            return Unauthorized("Invalid or missing API key.");
        }

        if (string.IsNullOrEmpty(googleId))
        {
            return BadRequest("GoogleId is required.");
        }

        User? user = await GetUserByGoogleIdAsync(googleId);
        if (user == null || string.IsNullOrEmpty(user.Id))
        {
            return NotFound("User not found.");
        }

        // First get all definitions for this user
        FilterDefinition<Definition> definitionFilter = Builders<Definition>.Filter.Eq(d => d.UserId, user.Id);
        List<Definition> definitions = await _definitionDatabase.GetDocumentsByPredicateAsync(definitionFilter);
        List<string> definitionIds = definitions.Select(d => d.Id!).ToList();

        if (definitionIds.Count == 0)
        {
            return Ok(new List<Entry>());
        }

        // If a specific definitionId is provided, verify it belongs to the user
        if (!string.IsNullOrEmpty(definitionId))
        {
            if (!definitionIds.Contains(definitionId))
            {
                return Forbid("The specified definition does not belong to the user.");
            }

            FilterDefinition<Entry> entryFilter = Builders<Entry>.Filter.Eq(e => e.DefinitionId, definitionId);
            List<Entry> entries = await _entryDatabase.GetDocumentsByPredicateAsync(entryFilter);
            return Ok(entries);
        }

        // Otherwise, get all entries for all user definitions
        FilterDefinition<Entry> filter = Builders<Entry>.Filter.In(e => e.DefinitionId, definitionIds);
        List<Entry> allEntries = await _entryDatabase.GetDocumentsByPredicateAsync(filter);

        return Ok(allEntries);
    }

    public class CreateDefinitionRequest
    {
        public string Name { get; set; } = string.Empty;
        public List<FieldDefinition> Fields { get; set; } = [];
    }

    public class CreateEntryRequest
    {
        public string DefinitionId { get; set; } = string.Empty;
        public DateTime? CreatedAt { get; set; }
        public Dictionary<string, JsonElement> Values { get; set; } = [];
    }

    [HttpPost("Definitions")]
    public async Task<ActionResult<Definition>> CreateDefinition([FromQuery] string googleId, [FromBody] CreateDefinitionRequest request)
    {
        if (!IsValidApiKey())
        {
            return Unauthorized("Invalid or missing API key.");
        }

        User? user = string.IsNullOrEmpty(googleId) ? null : await GetUserByGoogleIdAsync(googleId);
        if (user == null || string.IsNullOrEmpty(user.Id))
        {
            return NotFound("User not found.");
        }

        if (string.IsNullOrWhiteSpace(request.Name) || request.Fields.Count == 0)
        {
            return BadRequest("A definition needs a name and at least one field.");
        }

        FilterDefinition<Definition> filter = Builders<Definition>.Filter.Eq(d => d.UserId, user.Id);
        List<Definition> existing = await _definitionDatabase.GetDocumentsByPredicateAsync(filter);
        if (existing.Any(d => string.Equals(d.Name, request.Name.Trim(), StringComparison.OrdinalIgnoreCase)))
        {
            return Conflict("A definition with this name already exists.");
        }

        Definition definition = new()
        {
            Name = request.Name.Trim(),
            UserId = user.Id,
            Fields = request.Fields
        };
        return Ok(await _definitionDatabase.SaveDocumentAsync(definition));
    }

    [HttpPost("Entries")]
    public async Task<ActionResult<Entry>> CreateEntry([FromQuery] string googleId, [FromBody] CreateEntryRequest request)
    {
        if (!IsValidApiKey())
        {
            return Unauthorized("Invalid or missing API key.");
        }

        User? user = string.IsNullOrEmpty(googleId) ? null : await GetUserByGoogleIdAsync(googleId);
        if (user == null || string.IsNullOrEmpty(user.Id))
        {
            return NotFound("User not found.");
        }

        Definition? definition = string.IsNullOrEmpty(request.DefinitionId) ? null : await _definitionDatabase.GetDocumentByIdAsync(request.DefinitionId);
        if (definition == null || definition.UserId != user.Id)
        {
            return NotFound("Definition not found.");
        }

        Dictionary<string, object> values = [];
        foreach (KeyValuePair<string, JsonElement> kvp in request.Values)
        {
            FieldDefinition? field = definition.Fields.FirstOrDefault(f => string.Equals(f.Name, kvp.Key, StringComparison.OrdinalIgnoreCase));
            if (field == null)
            {
                return BadRequest($"Definition '{definition.Name}' has no field '{kvp.Key}'.");
            }
            if (!TryConvert(field.Type, kvp.Value, out object? value))
            {
                return BadRequest($"Value for '{field.Name}' is not a valid {field.Type}.");
            }
            if (value != null)
            {
                values[field.Name] = value;
            }
        }

        Entry entry = new()
        {
            DefinitionId = definition.Id!,
            CreatedAt = request.CreatedAt?.ToUniversalTime() ?? DateTime.UtcNow,
            Values = values
        };
        return Ok(await _entryDatabase.SaveDocumentAsync(entry));
    }

    private static bool TryConvert(FieldType type, JsonElement element, out object? value)
    {
        value = null;
        if (element.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return true;
        }

        string? text = element.ValueKind == JsonValueKind.String ? element.GetString() : element.GetRawText();
        switch (type)
        {
            case FieldType.Boolean when element.ValueKind is JsonValueKind.True or JsonValueKind.False:
                value = element.GetBoolean();
                return true;
            case FieldType.Int when element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out int number):
                value = number;
                return true;
            case FieldType.Float when element.ValueKind == JsonValueKind.Number:
                value = element.GetDouble();
                return true;
            case FieldType.Text:
                value = text ?? string.Empty;
                return true;
            case FieldType.DateTime when DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset dateTime):
                value = dateTime.UtcDateTime;
                return true;
            case FieldType.Time when TimeOnly.TryParse(text, CultureInfo.InvariantCulture, out TimeOnly time):
                value = time;
                return true;
            case FieldType.Date when DateOnly.TryParse(text, CultureInfo.InvariantCulture, out DateOnly date):
                value = date;
                return true;
            default:
                return false;
        }
    }

    [HttpGet("Definitions/{definitionId}")]
    public async Task<ActionResult<Definition>> GetDefinition(string definitionId, [FromQuery] string googleId)
    {
        if (!IsValidApiKey())
        {
            return Unauthorized("Invalid or missing API key.");
        }

        if (string.IsNullOrEmpty(googleId))
        {
            return BadRequest("GoogleId is required.");
        }

        User? user = await GetUserByGoogleIdAsync(googleId);
        if (user == null || string.IsNullOrEmpty(user.Id))
        {
            return NotFound("User not found.");
        }

        Definition? definition = await _definitionDatabase.GetDocumentByIdAsync(definitionId);
        if (definition == null)
        {
            return NotFound("Definition not found.");
        }

        if (definition.UserId != user.Id)
        {
            return Forbid("The specified definition does not belong to the user.");
        }

        return Ok(definition);
    }

    [HttpGet("Entries/{entryId}")]
    public async Task<ActionResult<Entry>> GetEntry(string entryId, [FromQuery] string googleId)
    {
        if (!IsValidApiKey())
        {
            return Unauthorized("Invalid or missing API key.");
        }

        if (string.IsNullOrEmpty(googleId))
        {
            return BadRequest("GoogleId is required.");
        }

        User? user = await GetUserByGoogleIdAsync(googleId);
        if (user == null || string.IsNullOrEmpty(user.Id))
        {
            return NotFound("User not found.");
        }

        // First get all definitions for this user
        FilterDefinition<Definition> definitionFilter = Builders<Definition>.Filter.Eq(d => d.UserId, user.Id);
        List<Definition> definitions = await _definitionDatabase.GetDocumentsByPredicateAsync(definitionFilter);
        List<string> definitionIds = definitions.Select(d => d.Id!).ToList();

        if (definitionIds.Count == 0)
        {
            return NotFound("Entry not found.");
        }

        Entry? entry = await _entryDatabase.GetDocumentByIdAsync(entryId);
        if (entry == null)
        {
            return NotFound("Entry not found.");
        }

        if (!definitionIds.Contains(entry.DefinitionId))
        {
            return Forbid("The specified entry does not belong to the user.");
        }

        return Ok(entry);
    }
}