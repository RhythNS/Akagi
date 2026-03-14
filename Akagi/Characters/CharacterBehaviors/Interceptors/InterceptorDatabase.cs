using Akagi.Data;
using Microsoft.Extensions.Options;

namespace Akagi.Characters.CharacterBehaviors.Interceptors;

internal interface IInterceptorDatabase : IDatabase<Interceptor>;

internal class InterceptorDatabase : Database<Interceptor>, IInterceptorDatabase
{
    public override string CollectionName => "interceptors";

    public InterceptorDatabase(IOptionsMonitor<DatabaseOptions> options) : base(options)
    {
    }

    public override bool CanSave(Savable savable) => savable is Interceptor;

    public override Task SaveAsync(Savable savable) => SaveDocumentAsync((Interceptor)savable);
}
