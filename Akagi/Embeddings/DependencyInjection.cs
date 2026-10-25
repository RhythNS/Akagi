using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Akagi.Embeddings;

internal static class DependencyInjection
{
    public static void AddEmbeddings(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<EmbeddingOptions>()
            .BindConfiguration("Embedding");

        services.AddHttpClient<IEmbeddingService, OpenRouterEmbeddingService>();
    }
}
