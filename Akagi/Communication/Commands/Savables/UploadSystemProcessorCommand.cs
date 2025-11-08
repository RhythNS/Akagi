using Akagi.Data;
using Akagi.Receivers.SystemProcessors;

namespace Akagi.Communication.Commands.Savables;

internal class UploadSystemProcessorCommand : UploadDocumentCommand<SystemProcessor>
{
    public override string Name => "/uploadSystemProcessor";

    protected override IDatabase<SystemProcessor> Database => _systemProcessorDatabase;

    public override string Description => "Uploads a system processor document. Usage: /uploadSystemProcessor <file>";

    private readonly ISystemProcessorDatabase _systemProcessorDatabase;

    public UploadSystemProcessorCommand(ISystemProcessorDatabase systemProcessorDatabase)
    {
        _systemProcessorDatabase = systemProcessorDatabase;
    }
}
