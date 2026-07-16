namespace InternalResourceStore.Application;

public sealed record CreateApiKeyCommand(string Name);
public sealed record CreateApiKeyResult(Guid Id, string Name, string ApiKey, DateTimeOffset CreatedAt);
public sealed record ApiKeyDto(Guid Id, string Name, DateTimeOffset CreatedAt, DateTimeOffset? RevokedAt, bool IsActive);

public sealed record UploadImageResourceCommand(string ApiKey, Stream File, string DeclaredMimeType);
public sealed record ResourceMetadataDto(Guid Id, string MimeType, long SizeBytes, int ImageWidth, int ImageHeight, DateTimeOffset CreatedAt, DateTimeOffset? DeletedAt, DateTimeOffset? PurgedAt);
public sealed record UploadImageResourceResult(Guid ResourceId, ResourceMetadataDto Metadata);
public sealed record ResourcePageDto(IReadOnlyList<ResourceMetadataDto> Items, int Total, int Limit, int Offset);

public sealed record ResourceFileDto(Stream Content, string MimeType, long SizeBytes);

public sealed record SystemVariableDto(string Key, string Value, string? Description, DateTimeOffset UpdatedAt);
public sealed record UpdateSystemVariableCommand(string Key, string Value);
public sealed record CleanupDeletedResourcesResult(int PurgedCount);
