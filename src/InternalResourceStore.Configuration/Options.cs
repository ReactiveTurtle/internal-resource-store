namespace InternalResourceStore.Configuration;

public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    public string RootPath { get; set; } = "/data/resources";
}

public sealed class ApiKeyHashOptions
{
    public const string SectionName = "ApiKeys";

    public string HashPepper { get; set; } = string.Empty;
}

public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    public bool ApplyMigrationsOnStartup { get; set; } = true;
}
