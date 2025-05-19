using Akagi.Characters;
using Akagi.Data;
using Akagi.Users;
using Microsoft.Extensions.Options;

namespace Akagi.Puppeteers.SystemProcessors;

internal class SystemProcessorDatabase : Database<SystemProcessor>, ISystemProcessorDatabase
{
    public SystemProcessorDatabase(IOptionsMonitor<DatabaseOptions> options) : base(options, "SystemProcessor")
    {
    }

    public async Task<SystemProcessor> GetSystemProcessor(User user, Character character)
    {
        SystemProcessor? systemProcessor = await GetDocumentByIdAsync(character.SystemProcessorId);
        return systemProcessor ?? throw new Exception($"SystemProcessor with ID {character.SystemProcessorId} not found.");
    }
}
