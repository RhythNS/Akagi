using Akagi.Data;
using Microsoft.Extensions.Options;

namespace Akagi.Characters.CharacterBehaviors.Reflectors;

internal class ReflectorDatabase : Database<Reflector>, IReflectorDatabase
{
    public ReflectorDatabase(IOptionsMonitor<DatabaseOptions> options) : base(options, "reflector")
    {
    }
    
    public override bool CanSave(Savable savable) => savable is Reflector;

    public override Task SaveAsync(Savable savable) => SaveDocumentAsync((Reflector)savable);
}
