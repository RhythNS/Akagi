using Akagi.Web.Data;
using Akagi.Web.Models;
using Akagi.Web.Models.TimeTrackers;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;

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