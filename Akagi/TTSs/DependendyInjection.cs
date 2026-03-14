using Akagi.TTSs.Inworld;
using Microsoft.Extensions.DependencyInjection;

namespace Akagi.TTSs;

static class DependendyInjection
{
    public static void AddTTSs(this IServiceCollection services)
    {
        services.AddOptions<InworldTTSClient.Options>()
            .BindConfiguration("InworldTTS");
        services.AddSingleton<IInworldTTSClient, InworldTTSClient>();
    }
}
