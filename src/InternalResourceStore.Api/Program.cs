using InternalResourceStore.Application;
using InternalResourceStore.Configuration;
using InternalResourceStore.Infrastructure;
using InternalResourceStore.Infrastructure.Persistence;
using InternalResourceStore.Infrastructure.SystemVariables;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddResourceStoreAppSettings(builder.Environment, args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Internal Resource Store API",
        Version = "v1",
        Description = "Internal API for storing and serving private application-owned resources."
    });

    options.AddSecurityDefinition("InternalApiKey", new OpenApiSecurityScheme
    {
        Name = "X-Internal-Api-Key",
        Type = SecuritySchemeType.ApiKey,
        In = ParameterLocation.Header,
        Description = "Configured internal API key for /internal endpoints."
    });

    options.AddSecurityDefinition("ApplicationApiKey", new OpenApiSecurityScheme
    {
        Name = "X-Api-Key",
        Type = SecuritySchemeType.ApiKey,
        In = ParameterLocation.Header,
        Description = "Generated application API key for resource endpoints."
    });
});
builder.Services.AddHealthChecks();
builder.Services.AddResourceStoreConfiguration(builder.Configuration);
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "Internal Resource Store API v1");
    options.RoutePrefix = "swagger";
});

await InitializeDatabaseAsync(app);

app.MapHealthChecks("/health");

app.MapPost("/internal/api-keys", CreateApiKey)
.WithTags("Internal")
.WithName(nameof(CreateApiKey));

app.MapGet("/internal/system-variables", GetSystemVariables)
.WithTags("Internal")
.WithName(nameof(GetSystemVariables));

app.MapPut("/internal/system-variables/{key}", UpdateSystemVariable)
.WithTags("Internal")
.WithName(nameof(UpdateSystemVariable));

app.MapPost("/resources/images", UploadImageResource)
.WithTags("Resources")
.WithName(nameof(UploadImageResource))
.Accepts<IFormFile>("multipart/form-data");

app.MapGet("/resources/{resourceId:guid}", GetResourceFile)
.WithTags("Resources")
.WithName(nameof(GetResourceFile))
.Produces(StatusCodes.Status200OK, contentType: "image/png")
.Produces(StatusCodes.Status200OK, contentType: "image/jpeg");

app.MapGet("/resources/{resourceId:guid}/metadata", GetResourceMetadata)
.WithTags("Resources")
.WithName(nameof(GetResourceMetadata));

app.MapDelete("/resources/{resourceId:guid}", SoftDeleteResource)
.WithTags("Resources")
.WithName(nameof(SoftDeleteResource));

app.Run();

static async Task InitializeDatabaseAsync(WebApplication app)
{
    using var scope = app.Services.CreateScope();
    var options = scope.ServiceProvider.GetRequiredService<IOptions<DatabaseOptions>>().Value;
    var dbContext = scope.ServiceProvider.GetRequiredService<InternalResourceStoreDbContext>();

    if (options.ApplyMigrationsOnStartup)
        await dbContext.Database.MigrateAsync();

    await scope.ServiceProvider.GetRequiredService<SystemVariablesSeeder>().SeedAsync(CancellationToken.None);
}

static async Task<IResult> CreateApiKey(
    HttpRequest request,
    CreateApiKeyRequest body,
    IConfiguration configuration,
    ApiKeyService service,
    CancellationToken cancellationToken)
{
    var internalAuth = ValidateInternalApiKey(request, configuration);
    if (internalAuth is not null) return internalAuth;

    if (string.IsNullOrWhiteSpace(body.Name))
        return Results.BadRequest(new ErrorResponse("api_key_name_required", "API key name is required."));

    if (body.Name.Length > 200)
        return Results.BadRequest(new ErrorResponse("api_key_name_too_long", "API key name must be 200 characters or less."));

    var result = await service.CreateAsync(new CreateApiKeyCommand(body.Name), cancellationToken);
    return ToTypedHttpResult(result);
}

static async Task<IResult> GetSystemVariables(
    HttpRequest request,
    IConfiguration configuration,
    SystemVariableService service,
    CancellationToken cancellationToken)
{
    var internalAuth = ValidateInternalApiKey(request, configuration);
    if (internalAuth is not null) return internalAuth;

    return Results.Ok(await service.GetAllAsync(cancellationToken));
}

static async Task<IResult> UpdateSystemVariable(
    HttpRequest request,
    string key,
    UpdateSystemVariableRequest body,
    IConfiguration configuration,
    SystemVariableService service,
    CancellationToken cancellationToken)
{
    var internalAuth = ValidateInternalApiKey(request, configuration);
    if (internalAuth is not null) return internalAuth;

    if (string.IsNullOrWhiteSpace(key) || key.Length > 200)
        return Results.BadRequest(new ErrorResponse("invalid_system_variable_key", "System variable key is required and must be 200 characters or less."));

    if (string.IsNullOrWhiteSpace(body.Value) || body.Value.Length > 1000)
        return Results.BadRequest(new ErrorResponse("invalid_system_variable_value", "System variable value is required and must be 1000 characters or less."));

    var result = await service.UpdateAsync(new UpdateSystemVariableCommand(key, body.Value), cancellationToken);
    return ToTypedHttpResult(result);
}

static async Task<IResult> UploadImageResource(
    HttpRequest request,
    ResourceService service,
    CancellationToken cancellationToken)
{
    var apiKey = GetHeader(request, "X-Api-Key");
    if (string.IsNullOrWhiteSpace(apiKey))
        return Results.Unauthorized();

    if (!request.HasFormContentType)
        return Results.BadRequest(new ErrorResponse("multipart_required", "Request must be multipart/form-data."));

    var form = await request.ReadFormAsync(cancellationToken);
    var file = form.Files.GetFile("file");
    if (file is null)
        return Results.BadRequest(new ErrorResponse("file_required", "Multipart file field 'file' is required."));

    if (file.Length == 0)
        return Results.BadRequest(new ErrorResponse("file_empty", "File must not be empty."));

    var mimeType = NormalizeImageMimeType(file.ContentType);
    if (mimeType is null)
        return Results.BadRequest(new ErrorResponse("unsupported_content_type", "Only image/png and image/jpeg are supported."));

    await using var stream = file.OpenReadStream();
    var result = await service.UploadImageAsync(new UploadImageResourceCommand(apiKey, stream, mimeType), cancellationToken);
    return ToTypedHttpResult(result);
}

static async Task<IResult> GetResourceFile(
    HttpRequest request,
    Guid resourceId,
    ResourceService service,
    CancellationToken cancellationToken)
{
    var apiKey = GetHeader(request, "X-Api-Key");
    if (string.IsNullOrWhiteSpace(apiKey))
        return Results.Unauthorized();

    var result = await service.GetFileAsync(resourceId, apiKey, cancellationToken);
    if (!result.IsSuccess) return ToTypedHttpResult(result);

    var file = result.Value!;
    return Results.File(file.Content, file.MimeType, enableRangeProcessing: false);
}

static async Task<IResult> GetResourceMetadata(
    HttpRequest request,
    Guid resourceId,
    ResourceService service,
    CancellationToken cancellationToken)
{
    var apiKey = GetHeader(request, "X-Api-Key");
    if (string.IsNullOrWhiteSpace(apiKey))
        return Results.Unauthorized();

    var result = await service.GetMetadataAsync(resourceId, apiKey, cancellationToken);
    return ToTypedHttpResult(result);
}

static async Task<IResult> SoftDeleteResource(
    HttpRequest request,
    Guid resourceId,
    ResourceService service,
    CancellationToken cancellationToken)
{
    var apiKey = GetHeader(request, "X-Api-Key");
    if (string.IsNullOrWhiteSpace(apiKey))
        return Results.Unauthorized();

    var result = await service.SoftDeleteAsync(resourceId, apiKey, cancellationToken);
    return result.IsSuccess ? Results.NoContent() : ToHttpResult(result);
}

static IResult? ValidateInternalApiKey(HttpRequest request, IConfiguration configuration)
{
    var configuredKey = configuration["InternalApi:Key"];
    if (string.IsNullOrWhiteSpace(configuredKey))
        return Results.Problem("Internal API key is not configured.", statusCode: StatusCodes.Status500InternalServerError);

    var providedKey = GetHeader(request, "X-Internal-Api-Key");
    return string.Equals(providedKey, configuredKey, StringComparison.Ordinal)
        ? null
        : Results.Unauthorized();
}

static string? GetHeader(HttpRequest request, string name) =>
    request.Headers.TryGetValue(name, out var value) ? value.ToString() : null;

static string? NormalizeImageMimeType(string? contentType) =>
    contentType?.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)[0].ToLowerInvariant() switch
    {
        "image/png" => "image/png",
        "image/jpg" => "image/jpeg",
        "image/jpeg" => "image/jpeg",
        _ => null
    };

static IResult ToHttpResult(Result result) =>
    result.IsSuccess ? Results.Ok() : ToHttpError(result.Error!);

static IResult ToTypedHttpResult<T>(Result<T> result) =>
    result.IsSuccess ? Results.Ok(result.Value) : ToHttpError(result.Error!);

static IResult ToHttpError(ApplicationError error) =>
    error.Type switch
    {
        ErrorType.Validation => Results.BadRequest(new ErrorResponse(error.Code, error.Message)),
        ErrorType.Unauthorized => Results.Unauthorized(),
        ErrorType.Forbidden => Results.Forbid(),
        ErrorType.NotFound => Results.NotFound(new ErrorResponse(error.Code, error.Message)),
        ErrorType.Conflict => Results.Conflict(new ErrorResponse(error.Code, error.Message)),
        _ => Results.Problem(error.Message)
    };

public sealed record CreateApiKeyRequest(string Name);
public sealed record UpdateSystemVariableRequest(string Value);
public sealed record ErrorResponse(string Code, string Message);
