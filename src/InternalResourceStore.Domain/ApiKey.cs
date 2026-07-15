namespace InternalResourceStore.Domain;

public sealed class ApiKey
{
    private ApiKey()
    {
        Name = string.Empty;
        KeyHash = string.Empty;
    }

    private ApiKey(Guid id, string name, string keyHash, DateTimeOffset createdAt)
    {
        if (id == Guid.Empty) throw new ArgumentException("API key id is required.", nameof(id));
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("API key name is required.", nameof(name));
        if (string.IsNullOrWhiteSpace(keyHash)) throw new ArgumentException("API key hash is required.", nameof(keyHash));

        Id = id;
        Name = name.Trim();
        KeyHash = keyHash;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }
    public string Name { get; private set; }
    public string KeyHash { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }

    public bool IsActive => RevokedAt is null;

    public static ApiKey Create(string name, string keyHash, DateTimeOffset createdAt) =>
        new(Guid.NewGuid(), name, keyHash, createdAt);

    public void Revoke(DateTimeOffset revokedAt)
    {
        RevokedAt ??= revokedAt;
    }
}
