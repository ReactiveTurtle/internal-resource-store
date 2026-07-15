using InternalResourceStore.Domain;
using Microsoft.EntityFrameworkCore;

namespace InternalResourceStore.Infrastructure.Persistence;

public sealed class InternalResourceStoreDbContext(DbContextOptions<InternalResourceStoreDbContext> options) : DbContext(options)
{
    public DbSet<ApiKey> ApiKeys => Set<ApiKey>();
    public DbSet<Resource> Resources => Set<Resource>();
    public DbSet<SystemVariable> SystemVariables => Set<SystemVariable>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("internal_resource_store");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(InternalResourceStoreDbContext).Assembly);
    }
}
