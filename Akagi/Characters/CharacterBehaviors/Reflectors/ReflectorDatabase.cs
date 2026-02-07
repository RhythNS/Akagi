using Akagi.Data;
using Microsoft.Extensions.Options;

namespace Akagi.Characters.CharacterBehaviors.Reflectors;

internal interface IReflectorDatabase : IDatabase<Reflector>;

internal class ReflectorDatabase : Database<Reflector>, IReflectorDatabase
{
    public override string CollectionName => "reflectors";

    public ReflectorDatabase(IOptionsMonitor<DatabaseOptions> options) : base(options)
    {
    }

    public override bool CanSave(Savable savable) => savable is Reflector;

    public override Task SaveAsync(Savable savable) => SaveDocumentAsync((Reflector)savable);
}
