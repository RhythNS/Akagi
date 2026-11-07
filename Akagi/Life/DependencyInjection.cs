using Akagi.Characters.Companions;
using Akagi.Characters.Groups;
using Akagi.Characters.Routines;
using Akagi.Characters.Upgrades;
using Akagi.Life.Japanese;
using Akagi.Life.Japanese.Calls;
using Akagi.Life.Japanese.Mining;
using Akagi.Life.Japanese.Vocabulary;
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
        services.AddSingleton<ICompanionStateDatabase, CompanionStateDatabase>();
        services.AddSingleton<IJapaneseDiagnosisDatabase, JapaneseDiagnosisDatabase>();
        services.AddSingleton<ICallJobDatabase, CallJobDatabase>();
        services.AddSingleton<IPersonProfileDatabase, PersonProfileDatabase>();
        services.AddSingleton<ICallMemory, CallMemory>();
        services.AddSingleton<IMinedSentenceDatabase, MinedSentenceDatabase>();
        services.AddSingleton<IStudyAdvisor, StudyAdvisor>();
        services.AddSingleton<IWordDictionary, JmdictWordDictionary>();
        services.AddSingleton<IVocabularyIndex, VocabularyIndex>();
        Array.ForEach(TypeUtils.GetNonAbstractTypesExtendingFrom<VocabularyReport>(), type => services.AddSingleton(typeof(VocabularyReport), type));
        services.AddSingleton<IProactiveDelivery, ProactiveDelivery>();
        services.AddHostedService<RoutineService>();

        services.Configure<CallAnalysisOptions>(configuration.GetSection("CallAnalysis"));
        services.AddHttpClient<ITranscriber, OpenRouterTranscriber>(client => client.Timeout = TimeSpan.FromMinutes(10));
        services.AddSingleton<ICallDelivery, CallDelivery>();
        services.AddHostedService<CallAnalysisService>();

        Type[] routineTypes = TypeUtils.GetNonAbstractTypesExtendingFrom<Routine>();
        Array.ForEach(routineTypes, BsonExtensions.Register);
    }
}
