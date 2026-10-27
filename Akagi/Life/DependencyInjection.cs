using Akagi.Characters.Groups;
using Akagi.Characters.Routines;
using Akagi.Characters.Upgrades;
using Akagi.Life.Japanese;
using Akagi.Life.Japanese.Calls;
using Akagi.Utils;
using Akagi.Utils.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Akagi.Life;

static class DependencyInjection
{
    public static void AddLife(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<ILifeProfileDatabase, LifeProfileDatabase>();
        services.AddSingleton<IRoutineDatabase, RoutineDatabase>();
        services.AddSingleton<IUpgradeProposalDatabase, UpgradeProposalDatabase>();
        services.AddSingleton<IJapaneseDiagnosisDatabase, JapaneseDiagnosisDatabase>();
        services.AddSingleton<ICallJobDatabase, CallJobDatabase>();
        services.AddSingleton<IProactiveDelivery, ProactiveDelivery>();
        services.AddHostedService<RoutineService>();

        services.Configure<CallAnalysisOptions>(configuration.GetSection("CallAnalysis"));
        services.AddHttpClient<ITranscriber, OpenAiTranscriber>(client => client.Timeout = TimeSpan.FromMinutes(10));
        services.AddHostedService<CallAnalysisService>();

        Type[] routineTypes = TypeUtils.GetNonAbstractTypesExtendingFrom<Routine>();
        Array.ForEach(routineTypes, BsonExtensions.Register);
    }
}
