using Akagi.Characters;
using Akagi.Data;
using Akagi.Users;

namespace Akagi.Puppeteers.SystemProcessors;

internal interface ISystemProcessorDatabase : IDatabase<SystemProcessor>
{
    public Task<SystemProcessor> GetSystemProcessor(User user, Character character);
    public Task<bool> SaveSystemProcessorFromFile(MemoryStream stream);
}
