using Microsoft.Extensions.Options;

namespace ITask.Api.Infrastructure.Storage;

public static class StorageServiceExtensions
{
    /// <summary>
    /// Registers <see cref="IBlobStorage"/>. The provider is chosen when first resolved, so test
    /// host configuration applies.
    /// </summary>
    public static IServiceCollection AddBlobStorage(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<StorageOptions>(config.GetSection(StorageOptions.Section));
        services.AddSingleton<LocalBlobStorage>();
        services.AddSingleton<AzureBlobStorage>();
        services.AddSingleton<IBlobStorage>(sp => sp.GetRequiredService<IOptions<StorageOptions>>().Value.IsAzure
            ? sp.GetRequiredService<AzureBlobStorage>()
            : sp.GetRequiredService<LocalBlobStorage>());
        return services;
    }
}
