using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace InternalResourceStore.Configuration;

public static class DependencyInjection
{
    public static IConfigurationBuilder AddResourceStoreAppSettings(
        this IConfigurationBuilder configuration,
        IHostEnvironment environment,
        string[] args)
    {
        configuration.Sources.Clear();

        configuration
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
            .AddJsonFile($"appsettings.{environment.EnvironmentName}.json", optional: true, reloadOnChange: true)
            .AddEnvironmentVariables();

        if (args.Length > 0)
            configuration.AddCommandLine(args);

        return configuration;
    }

    public static IServiceCollection AddResourceStoreConfiguration(this IServiceCollection services, IConfiguration configuration)
    {
        services
            .AddOptions<StorageOptions>()
            .Bind(configuration.GetSection(StorageOptions.SectionName))
            .Validate(options => !string.IsNullOrWhiteSpace(options.RootPath), "Storage:RootPath is required.")
            .ValidateOnStart();

        services
            .AddOptions<ApiKeyHashOptions>()
            .Bind(configuration.GetSection(ApiKeyHashOptions.SectionName))
            .Validate(options => !string.IsNullOrWhiteSpace(options.HashPepper), "ApiKeys:HashPepper is required.")
            .ValidateOnStart();

        services
            .AddOptions<DatabaseOptions>()
            .Bind(configuration.GetSection(DatabaseOptions.SectionName))
            .ValidateOnStart();

        return services;
    }
}
