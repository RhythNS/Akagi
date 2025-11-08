using Akagi.Data;
using Akagi.Receivers.Puppeteers;

namespace Akagi.Communication.Commands.Savables;

internal class UploadPuppeteerCommand : UploadDocumentCommand<Puppeteer>
{
    public override string Name => "/uploadPuppeteer";

    protected override IDatabase<Puppeteer> Database => _puppeteerDatabase;
    private readonly IPuppeteerDatabase _puppeteerDatabase;

    protected override SaveType SaveMethod => SaveType.BSON;

    public override string Description => "Uploads a puppeteer document. Usage: /uploadPuppeteer <file>";

    public UploadPuppeteerCommand(IPuppeteerDatabase puppeteerDatabase)
    {
        _puppeteerDatabase = puppeteerDatabase;
    }
}
