using Akagi.Data;

namespace Akagi.Characters.CharacterBehaviors.SystemProcessors;

internal interface ISystemProcessorDatabase : IDatabase<SystemProcessor>
{
    public Task<SystemProcessor[]> GetSystemProcessor(string[] ids);
    public Task<SystemProcessor> GetSystemProcessor(string id);
}
