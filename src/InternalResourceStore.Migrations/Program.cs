using InternalResourceStore.Configuration;
using InternalResourceStore.Infrastructure;
using InternalResourceStore.Infrastructure.Persistence;
using InternalResourceStore.Infrastructure.SystemVariables;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = Host.CreateApplicationBuilder(args);
builder.Configuration.AddResourceStoreAppSettings(builder.Environment, args);
builder.Services.AddResourceStoreConfiguration(builder.Configuration);
builder.Services.AddInfrastructure(builder.Configuration, includeHostedServices: false);

using var host = builder.Build();
using var scope = host.Services.CreateScope();

var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("InternalResourceStore.Migrations");
var dbContext = scope.ServiceProvider.GetRequiredService<InternalResourceStoreDbContext>();
var seeder = scope.ServiceProvider.GetRequiredService<SystemVariablesSeeder>();

logger.LogInformation("Applying database migrations.");
await dbContext.Database.MigrateAsync();

logger.LogInformation("Seeding system variables.");
await seeder.SeedAsync(CancellationToken.None);

logger.LogInformation("Database migrations completed.");
