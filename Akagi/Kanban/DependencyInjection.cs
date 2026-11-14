using Microsoft.Extensions.DependencyInjection;

namespace Akagi.Kanban;

static class DependencyInjection
{
    public static void AddKanban(this IServiceCollection services)
    {
        services.AddSingleton<IKanbanProjectDatabase, KanbanProjectDatabase>();
        services.AddSingleton<IKanbanTicketDatabase, KanbanTicketDatabase>();
        services.AddSingleton(provider => new KanbanService(
            provider.GetRequiredService<IKanbanProjectDatabase>(),
            provider.GetRequiredService<IKanbanTicketDatabase>()));
    }
}
