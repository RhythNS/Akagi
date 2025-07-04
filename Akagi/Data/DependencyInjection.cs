﻿using Akagi.Utils;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Akagi.Data;

static class DependencyInjection
{
    public static void AddData(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<DatabaseOptions>(configuration.GetSection("MongoDB"));
        services.AddSingleton<IFileDatabase, FileDatabase>();
        services.AddTransient<IDatabaseFactory, DatabaseFactory>();

        Type[] databaseTypes = TypeUtils.GetNonAbstractTypesExtendingFrom<IDatabase>();
        foreach (Type? type in databaseTypes)
        {
            services.AddSingleton(typeof(IDatabase), type);
        }
    }
}
