using InternalResourceStore.Application;
using InternalResourceStore.Domain;
using Microsoft.EntityFrameworkCore;

namespace InternalResourceStore.Infrastructure.Persistence;

public sealed class EfApiKeyRepository(InternalResourceStoreDbContext dbContext) : IApiKeyRepository
{
    public async Task AddAsync(ApiKey apiKey, CancellationToken cancellationToken) =>
        await dbContext.ApiKeys.AddAsync(apiKey, cancellationToken);

    public async Task<ApiKey?> GetByHashAsync(string keyHash, CancellationToken cancellationToken) =>
        await dbContext.ApiKeys.FirstOrDefaultAsync(x => x.KeyHash == keyHash, cancellationToken);

    public async Task<IReadOnlyList<ApiKey>> GetAllAsync(CancellationToken cancellationToken) =>
        await dbContext.ApiKeys
            .AsNoTracking()
            .OrderBy(x => x.Name)
            .ThenBy(x => x.CreatedAt)
            .ToListAsync(cancellationToken);
}

public sealed class EfResourceRepository(InternalResourceStoreDbContext dbContext) : IResourceRepository
{
    public async Task AddAsync(Resource resource, CancellationToken cancellationToken) =>
        await dbContext.Resources.AddAsync(resource, cancellationToken);

    public async Task<Resource?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        await dbContext.Resources.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public async Task<ResourcePage> GetActiveByOwnerAsync(
        string ownerApiKeyHash,
        int limit,
        int offset,
        CancellationToken cancellationToken)
    {
        var query = dbContext.Resources
            .AsNoTracking()
            .Where(x =>
                x.OwnerApiKeyHash == ownerApiKeyHash &&
                x.DeletedAt == null &&
                x.PurgedAt == null);

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(x => x.CreatedAt)
            .ThenBy(x => x.Id)
            .Skip(offset)
            .Take(limit)
            .ToListAsync(cancellationToken);

        return new ResourcePage(items, total);
    }

    public async Task<IReadOnlyList<Resource>> GetResourcesReadyForPurgeAsync(DateTimeOffset purgeBefore, int take, CancellationToken cancellationToken) =>
        await dbContext.Resources
            .Where(x => x.DeletedAt != null && x.DeletedAt <= purgeBefore && x.PurgedAt == null)
            .OrderBy(x => x.DeletedAt)
            .Take(take)
            .ToListAsync(cancellationToken);
}

public sealed class EfSystemVariableRepository(InternalResourceStoreDbContext dbContext) : ISystemVariableRepository
{
    public async Task<SystemVariable?> GetByKeyAsync(string key, CancellationToken cancellationToken) =>
        await dbContext.SystemVariables.FirstOrDefaultAsync(x => x.Key == key, cancellationToken);

    public async Task<IReadOnlyList<SystemVariable>> GetAllAsync(CancellationToken cancellationToken) =>
        await dbContext.SystemVariables.OrderBy(x => x.Key).ToListAsync(cancellationToken);

    public async Task AddAsync(SystemVariable systemVariable, CancellationToken cancellationToken) =>
        await dbContext.SystemVariables.AddAsync(systemVariable, cancellationToken);
}

public sealed class EfUnitOfWork(InternalResourceStoreDbContext dbContext) : IUnitOfWork
{
    public async Task SaveChangesAsync(CancellationToken cancellationToken) =>
        await dbContext.SaveChangesAsync(cancellationToken);
}
