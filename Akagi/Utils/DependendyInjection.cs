﻿using Microsoft.Extensions.DependencyInjection;

namespace Akagi.Utils;

static class DependendyInjection
{
    public static void AddUtils(this IServiceCollection services)
    {
        services.AddSingleton<ApplicationInformation>();
        services.AddSingleton<Globals>();

        Type[] systemInitializerTypes = TypeUtils.GetNonAbstractTypesExtendingFrom<ISystemInitializer>();
        Array.ForEach(systemInitializerTypes, type => services.AddScoped(typeof(ISystemInitializer), type));
    }
}
