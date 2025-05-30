using Akagi.Data;
using Microsoft.Extensions.Options;

namespace Akagi.Receivers.Puppeteers;

internal class PuppeteerDatabase : Database<Puppeteer>, IPuppeteerDatabase
{
    public PuppeteerDatabase(IOptionsMonitor<DatabaseOptions> options) : base(options, "puppeteer")
    {
    }

    public override bool CanSave(Savable savable) => savable is Puppeteer;

    public override Task SaveAsync(Savable savable) => SaveDocumentAsync((Puppeteer)savable);
}
