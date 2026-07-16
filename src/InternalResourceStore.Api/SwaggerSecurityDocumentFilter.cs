using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

public sealed class SwaggerSecurityDocumentFilter : IDocumentFilter
{
    public void Apply(OpenApiDocument swaggerDoc, DocumentFilterContext context)
    {
        foreach (var path in swaggerDoc.Paths)
        {
            if (path.Value?.Operations is not { } operations) continue;

            var schemeId = path.Key.StartsWith("/internal/", StringComparison.Ordinal)
                ? "InternalApiKey"
                : path.Key == "/resources" || path.Key.StartsWith("/resources/", StringComparison.Ordinal)
                    ? "ApplicationApiKey"
                    : null;

            if (schemeId is null) continue;

            foreach (var operation in operations.Values)
            {
                operation.Security ??= [];
                operation.Security.Add(new OpenApiSecurityRequirement
                {
                    [new OpenApiSecuritySchemeReference(schemeId, swaggerDoc, null)] = []
                });
            }
        }
    }
}
