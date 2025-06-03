using Microsoft.Extensions.DependencyInjection;

namespace Akagi.Utils;

static class DependendyInjection
{
    public static void AddUtils(this IServiceCollection services)
    {
        services.AddSingleton<ApplicationInformation>();
    }
}
