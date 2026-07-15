using InternalResourceStore.Application;
using InternalResourceStore.Domain;
using InternalResourceStore.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace InternalResourceStore.Infrastructure.SystemVariables;

public sealed class SystemVariablesSeeder(InternalResourceStoreDbContext dbContext, IClock clock)
{
    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        await SeedVariableAsync(SystemVariableKeys.ResourceSoftDeleteRetentionDays, "30", "Days before a soft-deleted resource file is purged.", cancellationToken);
        await SeedVariableAsync(SystemVariableKeys.ResourceCleanupIntervalMinutes, "60", "Cleanup worker interval in minutes.", cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task SeedVariableAsync(string key, string value, string description, CancellationToken cancellationToken)
    {
        var exists = await dbContext.SystemVariables.AnyAsync(x => x.Key == key, cancellationToken);
        if (!exists) await dbContext.SystemVariables.AddAsync(SystemVariable.Create(key, value, description, clock.UtcNow), cancellationToken);
    }
}
