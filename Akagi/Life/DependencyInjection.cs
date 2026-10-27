using Akagi.Characters.Routines;
using Akagi.Utils;
using Akagi.Utils.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace Akagi.Life;

static class DependencyInjection
{
    public static void AddLife(this IServiceCollection services)
    {
        services.AddSingleton<ILifeProfileDatabase, LifeProfileDatabase>();
        services.AddSingleton<IRoutineDatabase, RoutineDatabase>();
        services.AddHostedService<RoutineService>();

        Type[] routineTypes = TypeUtils.GetNonAbstractTypesExtendingFrom<Routine>();
        Array.ForEach(routineTypes, BsonExtensions.Register);
    }
}
