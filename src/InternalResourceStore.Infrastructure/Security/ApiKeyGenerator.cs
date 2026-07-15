using System.Security.Cryptography;
using InternalResourceStore.Application;

namespace InternalResourceStore.Infrastructure.Security;

public sealed class ApiKeyGenerator : IApiKeyGenerator
{
    public string Generate()
    {
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);
        return $"irs_{Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=')}";
    }
}
