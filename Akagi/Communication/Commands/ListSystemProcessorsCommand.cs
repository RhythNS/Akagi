using Akagi.Puppeteers.SystemProcessors;
using Akagi.Users;

namespace Akagi.Communication.Commands;

internal class ListSystemProcessorsCommand : ListCommand
{
    public override string Name => "/listSystemProcessors";

    private readonly ISystemProcessorDatabase _systemProcessorDatabase;

    public ListSystemProcessorsCommand(ISystemProcessorDatabase systemProcessorDatabase)
    {
        _systemProcessorDatabase = systemProcessorDatabase;
    }

    public override async Task ExecuteAsync(User user, string[] _)
    {
        List<SystemProcessor> systemProcessors = await _systemProcessorDatabase.GetDocumentsAsync();
        if (systemProcessors.Count == 0)
        {
            await Communicator.SendMessage(user, "No system processors found");
            return;
        }
        string[] ids = systemProcessors.Select(x => x.Id).ToArray();
        string[] names = systemProcessors.Select(x => x.Name).ToArray();
        string choices = GetList(ids, names);
        await Communicator.SendMessage(user, $"Available system processors:\n{choices}");
    }
}
