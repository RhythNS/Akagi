namespace Akagi.Web.Data;

static class DependendyInjection
{
    public static void AddData(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<DatabaseOptions>(configuration.GetSection("MongoDB"));
        services.AddSingleton<IUserDatabase, UserDatabase>();
        services.AddSingleton<IEntryDatabase, EntryDatabase>();
        services.AddSingleton<IDefinitionDatabase, DefinitionDatabase>();
    }
}
