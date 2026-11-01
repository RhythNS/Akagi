using Microsoft.Extensions.DependencyInjection;

namespace Akagi.Learning;

static class DependencyInjection
{
    public static void AddLearning(this IServiceCollection services)
    {
        services.AddSingleton<ILessonDatabase, LessonDatabase>();
        services.AddSingleton<ILessonAuthor, LessonAuthor>();
        services.AddSingleton<ITutorProvider, TutorProvider>();
        services.AddSingleton<ILearningService, LearningService>();
    }
}
