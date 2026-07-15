namespace InternalResourceStore.Domain;

public sealed class Resource
{
    private Resource()
    {
        StorageKey = string.Empty;
        OwnerApiKeyHash = string.Empty;
        MimeType = string.Empty;
    }

    private Resource(
        Guid id,
        string storageKey,
        string ownerApiKeyHash,
        string mimeType,
        long sizeBytes,
        int imageWidth,
        int imageHeight,
        DateTimeOffset createdAt)
    {
        if (id == Guid.Empty) throw new ArgumentException("Resource id is required.", nameof(id));
        if (string.IsNullOrWhiteSpace(storageKey)) throw new ArgumentException("Storage key is required.", nameof(storageKey));
        if (string.IsNullOrWhiteSpace(ownerApiKeyHash)) throw new ArgumentException("Owner API key hash is required.", nameof(ownerApiKeyHash));
        if (mimeType is not ("image/png" or "image/jpeg")) throw new ArgumentException("Unsupported resource MIME type.", nameof(mimeType));
        if (sizeBytes < 0) throw new ArgumentOutOfRangeException(nameof(sizeBytes), "Resource size cannot be negative.");
        if (imageWidth <= 0) throw new ArgumentOutOfRangeException(nameof(imageWidth), "Image width must be positive.");
        if (imageHeight <= 0) throw new ArgumentOutOfRangeException(nameof(imageHeight), "Image height must be positive.");

        Id = id;
        StorageKey = storageKey;
        OwnerApiKeyHash = ownerApiKeyHash;
        MimeType = mimeType;
        SizeBytes = sizeBytes;
        ImageWidth = imageWidth;
        ImageHeight = imageHeight;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }
    public string StorageKey { get; private set; }
    public string OwnerApiKeyHash { get; private set; }
    public string MimeType { get; private set; }
    public long SizeBytes { get; private set; }
    public int ImageWidth { get; private set; }
    public int ImageHeight { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }
    public DateTimeOffset? PurgedAt { get; private set; }

    public bool IsAvailableForRead => DeletedAt is null && PurgedAt is null;

    public static Resource Create(
        string storageKey,
        string ownerApiKeyHash,
        string mimeType,
        long sizeBytes,
        int imageWidth,
        int imageHeight,
        DateTimeOffset createdAt) =>
        new(Guid.NewGuid(), storageKey, ownerApiKeyHash, mimeType, sizeBytes, imageWidth, imageHeight, createdAt);

    public void SoftDelete(DateTimeOffset deletedAt)
    {
        if (PurgedAt is not null) return;
        DeletedAt ??= deletedAt;
    }

    public void MarkPurged(DateTimeOffset purgedAt)
    {
        if (DeletedAt is null) throw new InvalidOperationException("Only soft-deleted resources can be purged.");
        PurgedAt ??= purgedAt;
    }
}
