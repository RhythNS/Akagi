using Akagi.Data;
using Microsoft.Extensions.Options;

namespace Akagi.Characters.CharacterBehaviors.Puppeteers;

internal interface IPuppeteerDatabase : IDatabase<Puppeteer>;

internal class PuppeteerDatabase : Database<Puppeteer>, IPuppeteerDatabase
{
    public override string CollectionName => "puppeteers";

    public PuppeteerDatabase(IOptionsMonitor<DatabaseOptions> options) : base(options)
    {
    }

    public override bool CanSave(Savable savable) => savable is Puppeteer;

    public override Task SaveAsync(Savable savable) => SaveDocumentAsync((Puppeteer)savable);
}
