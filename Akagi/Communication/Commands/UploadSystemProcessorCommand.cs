using Akagi.Data;
using Akagi.Receivers.SystemProcessors;

namespace Akagi.Communication.Commands;

internal class UploadSystemProcessorCommand : UploadDocumentCommand<SystemProcessor>
{
    public override string Name => "/uploadSystemProcessor";

    protected override IDatabase<SystemProcessor> Database => _systemProcessorDatabase;

    private readonly ISystemProcessorDatabase _systemProcessorDatabase;

    public UploadSystemProcessorCommand(ISystemProcessorDatabase systemProcessorDatabase)
    {
        _systemProcessorDatabase = systemProcessorDatabase;
    }
}
