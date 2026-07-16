using InternalResourceStore.Domain;

namespace InternalResourceStore.Application;

public sealed class ApiKeyService(IApiKeyGenerator generator, IApiKeyHasher hasher, IApiKeyRepository repository, IUnitOfWork unitOfWork, IClock clock)
{
    public async Task<IReadOnlyList<ApiKeyDto>> GetAllAsync(CancellationToken cancellationToken) =>
        (await repository.GetAllAsync(cancellationToken))
            .Select(apiKey => new ApiKeyDto(apiKey.Id, apiKey.Name, apiKey.CreatedAt, apiKey.RevokedAt, apiKey.IsActive))
            .ToArray();

    public async Task<Result<CreateApiKeyResult>> CreateAsync(CreateApiKeyCommand command, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.Name))
            return Result<CreateApiKeyResult>.Failure(new ApplicationError(ErrorType.Validation, "api_key_name_required", "API key name is required."));

        var rawKey = generator.Generate();
        var hash = hasher.Hash(rawKey);
        var apiKey = ApiKey.Create(command.Name, hash, clock.UtcNow);

        await repository.AddAsync(apiKey, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<CreateApiKeyResult>.Success(new CreateApiKeyResult(apiKey.Id, apiKey.Name, rawKey, apiKey.CreatedAt));
    }
}

public sealed class ResourceService(
    IApiKeyHasher apiKeyHasher,
    IApiKeyRepository apiKeyRepository,
    IResourceRepository resourceRepository,
    IResourceFileStorage fileStorage,
    IImageProcessor imageProcessor,
    IUnitOfWork unitOfWork,
    IClock clock)
{
    public async Task<Result<UploadImageResourceResult>> UploadImageAsync(UploadImageResourceCommand command, CancellationToken cancellationToken)
    {
        var ownerHashResult = await GetActiveApiKeyHashAsync(command.ApiKey, cancellationToken);
        if (!ownerHashResult.IsSuccess) return Result<UploadImageResourceResult>.Failure(ownerHashResult.Error!);

        ProcessedImage processed;
        try
        {
            processed = await imageProcessor.ProcessAsync(command.File, command.DeclaredMimeType, cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            return Result<UploadImageResourceResult>.Failure(new ApplicationError(ErrorType.Validation, "invalid_image", ex.Message));
        }

        await using (processed.Content)
        {
            var storageKey = await fileStorage.SaveAsync(processed.Content, processed.Extension, cancellationToken);
            var resource = Resource.Create(storageKey, ownerHashResult.Value!, processed.MimeType, processed.SizeBytes, processed.Width, processed.Height, clock.UtcNow);

            await resourceRepository.AddAsync(resource, cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);

            return Result<UploadImageResourceResult>.Success(new UploadImageResourceResult(resource.Id, ToMetadata(resource)));
        }
    }

    public async Task<Result<ResourcePageDto>> GetResourcesAsync(
        string apiKey,
        int limit,
        int offset,
        CancellationToken cancellationToken)
    {
        var ownerHashResult = await GetActiveApiKeyHashAsync(apiKey, cancellationToken);
        if (!ownerHashResult.IsSuccess) return Result<ResourcePageDto>.Failure(ownerHashResult.Error!);

        var page = await resourceRepository.GetActiveByOwnerAsync(ownerHashResult.Value!, limit, offset, cancellationToken);
        return Result<ResourcePageDto>.Success(new ResourcePageDto(
            page.Items.Select(ToMetadata).ToArray(),
            page.Total,
            limit,
            offset));
    }

    public async Task<Result<ResourceFileDto>> GetFileAsync(Guid resourceId, string apiKey, CancellationToken cancellationToken)
    {
        var resourceResult = await GetOwnedReadableResourceAsync(resourceId, apiKey, cancellationToken);
        if (!resourceResult.IsSuccess) return Result<ResourceFileDto>.Failure(resourceResult.Error!);

        var resource = resourceResult.Value!;
        var stream = await fileStorage.OpenReadAsync(resource.StorageKey, cancellationToken);
        return Result<ResourceFileDto>.Success(new ResourceFileDto(stream, resource.MimeType, resource.SizeBytes));
    }

    public async Task<Result<ResourceMetadataDto>> GetMetadataAsync(Guid resourceId, string apiKey, CancellationToken cancellationToken)
    {
        var resourceResult = await GetOwnedReadableResourceAsync(resourceId, apiKey, cancellationToken);
        return resourceResult.IsSuccess
            ? Result<ResourceMetadataDto>.Success(ToMetadata(resourceResult.Value!))
            : Result<ResourceMetadataDto>.Failure(resourceResult.Error!);
    }

    public async Task<Result> SoftDeleteAsync(Guid resourceId, string apiKey, CancellationToken cancellationToken)
    {
        var resourceResult = await GetOwnedResourceAsync(resourceId, apiKey, cancellationToken);
        if (!resourceResult.IsSuccess) return Result.Failure(resourceResult.Error!);

        resourceResult.Value!.SoftDelete(clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    private async Task<Result<string>> GetActiveApiKeyHashAsync(string apiKey, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            return Result<string>.Failure(new ApplicationError(ErrorType.Unauthorized, "api_key_required", "API key is required."));

        var hash = apiKeyHasher.Hash(apiKey);
        var storedKey = await apiKeyRepository.GetByHashAsync(hash, cancellationToken);
        if (storedKey is null || !storedKey.IsActive)
            return Result<string>.Failure(new ApplicationError(ErrorType.Unauthorized, "invalid_api_key", "API key is invalid."));

        return Result<string>.Success(hash);
    }

    private async Task<Result<Resource>> GetOwnedReadableResourceAsync(Guid resourceId, string apiKey, CancellationToken cancellationToken)
    {
        var resourceResult = await GetOwnedResourceAsync(resourceId, apiKey, cancellationToken);
        if (!resourceResult.IsSuccess) return resourceResult;

        if (!resourceResult.Value!.IsAvailableForRead)
            return Result<Resource>.Failure(new ApplicationError(ErrorType.NotFound, "resource_not_found", "Resource was not found."));

        return resourceResult;
    }

    private async Task<Result<Resource>> GetOwnedResourceAsync(Guid resourceId, string apiKey, CancellationToken cancellationToken)
    {
        var hashResult = await GetActiveApiKeyHashAsync(apiKey, cancellationToken);
        if (!hashResult.IsSuccess) return Result<Resource>.Failure(hashResult.Error!);

        var resource = await resourceRepository.GetByIdAsync(resourceId, cancellationToken);
        if (resource is null)
            return Result<Resource>.Failure(new ApplicationError(ErrorType.NotFound, "resource_not_found", "Resource was not found."));

        if (!string.Equals(resource.OwnerApiKeyHash, hashResult.Value, StringComparison.Ordinal))
            return Result<Resource>.Failure(new ApplicationError(ErrorType.Forbidden, "resource_owner_mismatch", "API key has no access to this resource."));

        return Result<Resource>.Success(resource);
    }

    private static ResourceMetadataDto ToMetadata(Resource resource) =>
        new(resource.Id, resource.MimeType, resource.SizeBytes, resource.ImageWidth, resource.ImageHeight, resource.CreatedAt, resource.DeletedAt, resource.PurgedAt);
}

public sealed class SystemVariableService(ISystemVariableRepository repository, IUnitOfWork unitOfWork, IClock clock)
{
    public async Task<IReadOnlyList<SystemVariableDto>> GetAllAsync(CancellationToken cancellationToken) =>
        (await repository.GetAllAsync(cancellationToken)).Select(ToDto).ToArray();

    public async Task<Result<SystemVariableDto>> UpdateAsync(UpdateSystemVariableCommand command, CancellationToken cancellationToken)
    {
        var validationError = ValidateKnownVariable(command.Key, command.Value);
        if (validationError is not null) return Result<SystemVariableDto>.Failure(validationError);

        var variable = await repository.GetByKeyAsync(command.Key, cancellationToken);
        if (variable is null)
        {
            variable = SystemVariable.Create(command.Key, command.Value, null, clock.UtcNow);
            await repository.AddAsync(variable, cancellationToken);
        }
        else
        {
            variable.UpdateValue(command.Value, clock.UtcNow);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result<SystemVariableDto>.Success(ToDto(variable));
    }

    private static ApplicationError? ValidateKnownVariable(string key, string value)
    {
        if (key is SystemVariableKeys.ResourceSoftDeleteRetentionDays && (!int.TryParse(value, out var days) || days < 0))
            return new ApplicationError(ErrorType.Validation, "invalid_retention_days", "Retention days must be a non-negative integer.");

        if (key is SystemVariableKeys.ResourceCleanupIntervalMinutes && (!int.TryParse(value, out var minutes) || minutes < 1))
            return new ApplicationError(ErrorType.Validation, "invalid_cleanup_interval", "Cleanup interval minutes must be a positive integer.");

        return null;
    }

    private static SystemVariableDto ToDto(SystemVariable variable) =>
        new(variable.Key, variable.Value, variable.Description, variable.UpdatedAt);
}

public sealed class CleanupDeletedResourcesService(
    IResourceRepository resourceRepository,
    ISystemVariableRepository systemVariableRepository,
    IResourceFileStorage fileStorage,
    IUnitOfWork unitOfWork,
    IClock clock)
{
    public async Task<CleanupDeletedResourcesResult> CleanupAsync(int take, CancellationToken cancellationToken)
    {
        var retentionDays = await GetIntVariableAsync(SystemVariableKeys.ResourceSoftDeleteRetentionDays, 30, cancellationToken);
        var purgeBefore = clock.UtcNow.AddDays(-retentionDays);
        var resources = await resourceRepository.GetResourcesReadyForPurgeAsync(purgeBefore, take, cancellationToken);

        foreach (var resource in resources)
        {
            await fileStorage.DeleteIfExistsAsync(resource.StorageKey, cancellationToken);
            resource.MarkPurged(clock.UtcNow);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new CleanupDeletedResourcesResult(resources.Count);
    }

    public async Task<int> GetCleanupIntervalMinutesAsync(CancellationToken cancellationToken) =>
        await GetIntVariableAsync(SystemVariableKeys.ResourceCleanupIntervalMinutes, 60, cancellationToken);

    private async Task<int> GetIntVariableAsync(string key, int defaultValue, CancellationToken cancellationToken)
    {
        var variable = await systemVariableRepository.GetByKeyAsync(key, cancellationToken);
        return variable is not null && int.TryParse(variable.Value, out var value) ? value : defaultValue;
    }
}
