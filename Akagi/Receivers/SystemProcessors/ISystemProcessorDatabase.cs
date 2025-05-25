using Akagi.Data;

namespace Akagi.Receivers.SystemProcessors;

internal interface ISystemProcessorDatabase : IDatabase<SystemProcessor>
{
    public Task<SystemProcessor[]> GetSystemProcessor(string[] ids);
    public Task<SystemProcessor> GetSystemProcessor(string id);
    public Task<bool> SaveSystemProcessorFromFile(MemoryStream stream);
}
