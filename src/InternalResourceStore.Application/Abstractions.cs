using InternalResourceStore.Domain;

namespace InternalResourceStore.Application;

public interface IApiKeyRepository
{
    Task AddAsync(ApiKey apiKey, CancellationToken cancellationToken);
    Task<ApiKey?> GetByHashAsync(string keyHash, CancellationToken cancellationToken);
    Task<IReadOnlyList<ApiKey>> GetAllAsync(CancellationToken cancellationToken);
}

public interface IResourceRepository
{
    Task AddAsync(Resource resource, CancellationToken cancellationToken);
    Task<Resource?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<ResourcePage> GetActiveByOwnerAsync(string ownerApiKeyHash, int limit, int offset, CancellationToken cancellationToken);
    Task<IReadOnlyList<Resource>> GetResourcesReadyForPurgeAsync(DateTimeOffset purgeBefore, int take, CancellationToken cancellationToken);
}

public interface ISystemVariableRepository
{
    Task<SystemVariable?> GetByKeyAsync(string key, CancellationToken cancellationToken);
    Task<IReadOnlyList<SystemVariable>> GetAllAsync(CancellationToken cancellationToken);
    Task AddAsync(SystemVariable systemVariable, CancellationToken cancellationToken);
}

public interface IUnitOfWork
{
    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public interface IApiKeyGenerator
{
    string Generate();
}

public interface IApiKeyHasher
{
    string Hash(string apiKey);
}

public interface IImageProcessor
{
    Task<ProcessedImage> ProcessAsync(Stream input, string declaredMimeType, CancellationToken cancellationToken);
}

public interface IResourceFileStorage
{
    Task<string> SaveAsync(Stream content, string extension, CancellationToken cancellationToken);
    Task<Stream> OpenReadAsync(string storageKey, CancellationToken cancellationToken);
    Task DeleteIfExistsAsync(string storageKey, CancellationToken cancellationToken);
}

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

public sealed record ProcessedImage(Stream Content, string MimeType, long SizeBytes, int Width, int Height, string Extension);
public sealed record ResourcePage(IReadOnlyList<Resource> Items, int Total);
