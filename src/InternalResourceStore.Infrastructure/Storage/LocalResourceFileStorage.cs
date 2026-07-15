using InternalResourceStore.Application;
using InternalResourceStore.Configuration;
using Microsoft.Extensions.Options;

namespace InternalResourceStore.Infrastructure.Storage;

public sealed class LocalResourceFileStorage(IOptions<StorageOptions> options) : IResourceFileStorage
{
    public async Task<string> SaveAsync(Stream content, string extension, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(options.Value.RootPath);
        var normalizedExtension = extension.TrimStart('.').ToLowerInvariant();
        var fileName = $"{DateTimeOffset.UtcNow:yyyyMMdd}/{Guid.NewGuid():N}.{normalizedExtension}";
        var fullPath = GetFullPath(fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        await using var output = File.Create(fullPath);
        content.Position = 0;
        await content.CopyToAsync(output, cancellationToken);
        return fileName.Replace('\\', '/');
    }

    public Task<Stream> OpenReadAsync(string storageKey, CancellationToken cancellationToken)
    {
        var path = GetFullPath(storageKey);
        if (!File.Exists(path)) throw new FileNotFoundException("Resource file was not found.", storageKey);
        return Task.FromResult<Stream>(File.OpenRead(path));
    }

    public Task DeleteIfExistsAsync(string storageKey, CancellationToken cancellationToken)
    {
        var path = GetFullPath(storageKey);
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }

    private string GetFullPath(string storageKey)
    {
        var root = Path.GetFullPath(options.Value.RootPath);
        var fullPath = Path.GetFullPath(Path.Combine(root, storageKey));
        if (!fullPath.StartsWith(root, StringComparison.Ordinal))
            throw new InvalidOperationException("Invalid storage key path.");

        return fullPath;
    }
}
