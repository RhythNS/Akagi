using Akagi.Scheduling.Tasks;
using Akagi.Utils;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson.Serialization;

namespace Akagi.Scheduling;

static class DependendyInjection
{
    public static void AddScheduling(this IServiceCollection services)
    {
        services.AddSingleton<ITaskDatabase, TaskDatabase>();
        services.AddHostedService<SchedulerService>();

        Type[] taskTypes = TypeUtils.GetNonAbstractTypesExtendingFrom<BaseTask>();
        Array.ForEach(taskTypes, taskType => Register(services, taskType));

        Type[] cleanableTypes = TypeUtils.GetNonAbstractTypesExtendingFrom<ICleanable>();
        Array.ForEach(cleanableTypes, cleanableType => services.AddScoped(typeof(ICleanable), cleanableType));
    }

    private static void Register(IServiceCollection services, Type type)
    {
        services.AddTransient(typeof(BaseTask), type);

        if (!BsonClassMap.IsClassMapRegistered(type))
        {
            BsonClassMap.RegisterClassMap(new BsonClassMap(type));
        }
    }
}
