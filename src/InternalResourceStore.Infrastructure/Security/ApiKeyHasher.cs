using System.Security.Cryptography;
using System.Text;
using InternalResourceStore.Application;
using InternalResourceStore.Configuration;
using Microsoft.Extensions.Options;

namespace InternalResourceStore.Infrastructure.Security;

public sealed class ApiKeyHasher(IOptions<ApiKeyHashOptions> options) : IApiKeyHasher
{
    public string Hash(string apiKey)
    {
        if (string.IsNullOrWhiteSpace(options.Value.HashPepper))
            throw new InvalidOperationException("ApiKeys:HashPepper must be configured.");

        var bytes = Encoding.UTF8.GetBytes($"{options.Value.HashPepper}:{apiKey}");
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }
}
