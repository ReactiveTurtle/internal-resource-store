using InternalResourceStore.Application;
using InternalResourceStore.Configuration;
using InternalResourceStore.Infrastructure.Images;
using InternalResourceStore.Infrastructure.Persistence;
using InternalResourceStore.Infrastructure.Security;
using InternalResourceStore.Infrastructure.Storage;
using InternalResourceStore.Infrastructure.SystemVariables;
using InternalResourceStore.Infrastructure.Workers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace InternalResourceStore.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration, bool includeHostedServices = true)
    {
        services.AddDbContext<InternalResourceStoreDbContext>(options =>
            options.UseNpgsql(
                configuration.GetConnectionString("Postgres"),
                npgsqlOptions => npgsqlOptions.MigrationsHistoryTable("__EFMigrationsHistory", "internal_resource_store")));

        services.AddScoped<IApiKeyRepository, EfApiKeyRepository>();
        services.AddScoped<IResourceRepository, EfResourceRepository>();
        services.AddScoped<ISystemVariableRepository, EfSystemVariableRepository>();
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();
        services.AddSingleton<IApiKeyGenerator, ApiKeyGenerator>();
        services.AddSingleton<IApiKeyHasher, ApiKeyHasher>();
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IImageProcessor, ImageSharpImageProcessor>();
        services.AddSingleton<IResourceFileStorage, LocalResourceFileStorage>();
        services.AddScoped<SystemVariablesSeeder>();

        if (includeHostedServices)
            services.AddHostedService<DeletedResourcesCleanupWorker>();

        return services;
    }
}
