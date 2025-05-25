using Akagi.Data;
using Microsoft.Extensions.Options;

namespace Akagi.Receivers.Puppeteers;

internal class PuppeteerDatabase : Database<Puppeteer>, IPuppeteerDatabase
{
    public PuppeteerDatabase(IOptionsMonitor<DatabaseOptions> options, string collectionName) : base(options, collectionName)
    {
    }
}
