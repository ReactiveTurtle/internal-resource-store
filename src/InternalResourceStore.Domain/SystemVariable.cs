namespace InternalResourceStore.Domain;

public sealed class SystemVariable
{
    private SystemVariable()
    {
        Key = string.Empty;
        Value = string.Empty;
    }

    private SystemVariable(string key, string value, string? description, DateTimeOffset updatedAt)
    {
        if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("System variable key is required.", nameof(key));
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("System variable value is required.", nameof(value));

        Key = key.Trim();
        Value = value.Trim();
        Description = description;
        UpdatedAt = updatedAt;
    }

    public string Key { get; private set; }
    public string Value { get; private set; }
    public string? Description { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static SystemVariable Create(string key, string value, string? description, DateTimeOffset updatedAt) =>
        new(key, value, description, updatedAt);

    public void UpdateValue(string value, DateTimeOffset updatedAt)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("System variable value is required.", nameof(value));

        Value = value.Trim();
        UpdatedAt = updatedAt;
    }
}
