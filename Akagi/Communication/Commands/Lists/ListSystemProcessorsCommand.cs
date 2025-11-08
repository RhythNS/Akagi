using Akagi.Receivers.SystemProcessors;

namespace Akagi.Communication.Commands.Lists;

internal class ListSystemProcessorsCommand : ListCommand
{
    public override string Name => "/listSystemProcessors";

    public override string Description => "Lists all system processors. Usage: /listSystemProcessors";

    private readonly ISystemProcessorDatabase _systemProcessorDatabase;

    public ListSystemProcessorsCommand(ISystemProcessorDatabase systemProcessorDatabase)
    {
        _systemProcessorDatabase = systemProcessorDatabase;
    }

    public override async Task ExecuteAsync(Context context, string[] _)
    {
        List<SystemProcessor> systemProcessors = await _systemProcessorDatabase.GetDocumentsAsync();
        if (systemProcessors.Count == 0)
        {
            await Communicator.SendMessage(context.User, "No system processors found");
            return;
        }
        string[] ids = [.. systemProcessors.Select(x => x.Id!)];
        string[] names = [.. systemProcessors.Select(x => x.Name)];
        string choices = GetIdList(ids, names);
        await Communicator.SendMessage(context.User, $"Available system processors:\n{choices}");
    }
}
