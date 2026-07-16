using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

const int DefaultPublicPort = 32546;

var options = ParseArguments(args);
if (options.ShowHelp)
{
    PrintUsage();
    return 0;
}

ValidateOptions(options);

var scriptPath = GetSourceFilePath();
var deploymentDirectory = Path.GetDirectoryName(scriptPath)
    ?? throw new InvalidOperationException("Cannot resolve deployment directory.");
var repositoryRoot = Directory.GetParent(deploymentDirectory)?.FullName
    ?? throw new InvalidOperationException("Cannot resolve repository root.");
var secretsPath = Path.GetFullPath(options.SecretsFile!);

if (secretsPath.StartsWith(repositoryRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
    Console.Error.WriteLine("Warning: keep real deployment secrets outside the repository.");

var secrets = ReadSecrets(secretsPath);
var generatedDirectory = Path.Combine(deploymentDirectory, ".generated");
Directory.CreateDirectory(generatedDirectory);

var appSettingsPath = Path.Combine(generatedDirectory, "appsettings.Production.json");
var envPath = Path.Combine(generatedDirectory, "deploy.env");
var composePath = Path.Combine(deploymentDirectory, "docker-compose.deploy.yml");

await File.WriteAllTextAsync(
    appSettingsPath,
    CreateAppSettings(secrets).ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine,
    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
await File.WriteAllTextAsync(
    envPath,
    $"PUBLIC_PORT={secrets.PublicPort}{Environment.NewLine}",
    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

if (options.Target == DeploymentTarget.Local)
    return await DeployLocalAsync(options, envPath, composePath, repositoryRoot);

return await DeployRemoteAsync(options, repositoryRoot);

static async Task<int> DeployLocalAsync(
    DeploymentOptions options,
    string envPath,
    string composePath,
    string repositoryRoot)
{
    EnsureCommandExists("docker");

    var composeArguments = new[]
    {
        "compose",
        "--project-name", options.ProjectName,
        "--env-file", envPath,
        "-f", composePath
    };

    if (options.Down)
        return await RunAsync("docker", [.. composeArguments, "down"], repositoryRoot);

    if (options.Pull)
        await RunRequiredAsync("docker", [.. composeArguments, "pull"], repositoryRoot);

    if (!options.NoBuild)
    {
        await RunRequiredAsync(
            "docker",
            [.. composeArguments, "build", "internal-resource-store-migrations", "internal-resource-store-api"],
            repositoryRoot);
    }

    await RunRequiredAsync(
        "docker",
        [
            .. composeArguments,
            "up",
            "--force-recreate",
            "--abort-on-container-exit",
            "--exit-code-from", "internal-resource-store-migrations",
            "internal-resource-store-migrations"
        ],
        repositoryRoot);

    return await RunAsync(
        "docker",
        [.. composeArguments, "up", "-d", "--no-deps", "internal-resource-store-api"],
        repositoryRoot);
}

static async Task<int> DeployRemoteAsync(DeploymentOptions options, string repositoryRoot)
{
    EnsureCommandExists("ssh");
    EnsureCommandExists("tar");

    var host = options.Host!;
    var sshKey = Path.GetFullPath(options.SshKey!);
    var remoteDirectory = options.RemoteDirectory;
    var quotedRemoteDirectory = ShellQuote(remoteDirectory);
    var remoteComposeArguments =
        $"--project-name {ShellQuote(options.ProjectName)} " +
        "--env-file deployment/.generated/deploy.env " +
        "-f deployment/docker-compose.deploy.yml";

    await RunRequiredAsync("ssh", ["-i", sshKey, host, $"mkdir -p {quotedRemoteDirectory}"], repositoryRoot);
    await TransferRepositoryAsync(repositoryRoot, host, sshKey, quotedRemoteDirectory);

    string RemoteCommand(string command) =>
        $"cd {quotedRemoteDirectory} && docker compose {remoteComposeArguments} {command}";

    if (options.Down)
        return await RunAsync("ssh", ["-i", sshKey, host, RemoteCommand("down")], repositoryRoot);

    if (options.Pull)
        await RunRequiredAsync("ssh", ["-i", sshKey, host, RemoteCommand("pull")], repositoryRoot);

    if (!options.NoBuild)
    {
        await RunRequiredAsync(
            "ssh",
            ["-i", sshKey, host, RemoteCommand("build internal-resource-store-migrations internal-resource-store-api")],
            repositoryRoot);
    }

    await RunRequiredAsync(
        "ssh",
        [
            "-i", sshKey, host,
            RemoteCommand(
                "up --force-recreate --abort-on-container-exit " +
                "--exit-code-from internal-resource-store-migrations internal-resource-store-migrations")
        ],
        repositoryRoot);

    return await RunAsync(
        "ssh",
        ["-i", sshKey, host, RemoteCommand("up -d --no-deps internal-resource-store-api")],
        repositoryRoot);
}

static async Task TransferRepositoryAsync(
    string repositoryRoot,
    string host,
    string sshKey,
    string quotedRemoteDirectory)
{
    var tar = CreateProcess(
        "tar",
        [
            "--exclude=.git",
            "--exclude=.vs",
            "--exclude=.vscode",
            "--exclude=*/bin",
            "--exclude=*/obj",
            "--exclude=data",
            "-czf", "-", "."
        ],
        repositoryRoot,
        redirectOutput: true,
        redirectInput: false);
    var ssh = CreateProcess(
        "ssh",
        ["-i", sshKey, host, $"tar -xzf - -C {quotedRemoteDirectory}"],
        repositoryRoot,
        redirectOutput: false,
        redirectInput: true);

    tar.Start();
    ssh.Start();

    var tarError = tar.StandardError.ReadToEndAsync();
    var sshError = ssh.StandardError.ReadToEndAsync();
    await tar.StandardOutput.BaseStream.CopyToAsync(ssh.StandardInput.BaseStream);
    ssh.StandardInput.Close();

    await Task.WhenAll(tar.WaitForExitAsync(), ssh.WaitForExitAsync());
    var errors = string.Join(Environment.NewLine, await tarError, await sshError).Trim();

    if (tar.ExitCode != 0 || ssh.ExitCode != 0)
        throw new InvalidOperationException($"Remote file transfer failed.{Environment.NewLine}{errors}");
}

static async Task RunRequiredAsync(string fileName, IReadOnlyList<string> arguments, string workingDirectory)
{
    var exitCode = await RunAsync(fileName, arguments, workingDirectory);
    if (exitCode != 0)
        throw new InvalidOperationException($"Command '{fileName}' exited with code {exitCode}.");
}

static async Task<int> RunAsync(string fileName, IReadOnlyList<string> arguments, string workingDirectory)
{
    using var process = CreateProcess(fileName, arguments, workingDirectory, redirectOutput: false, redirectInput: false);
    process.Start();
    await process.WaitForExitAsync();
    return process.ExitCode;
}

static Process CreateProcess(
    string fileName,
    IReadOnlyList<string> arguments,
    string workingDirectory,
    bool redirectOutput,
    bool redirectInput)
{
    var startInfo = new ProcessStartInfo
    {
        FileName = fileName,
        WorkingDirectory = workingDirectory,
        UseShellExecute = false,
        RedirectStandardOutput = redirectOutput,
        RedirectStandardInput = redirectInput,
        RedirectStandardError = redirectOutput || redirectInput
    };

    foreach (var argument in arguments)
        startInfo.ArgumentList.Add(argument);

    return new Process { StartInfo = startInfo };
}

static void EnsureCommandExists(string command)
{
    var locator = OperatingSystem.IsWindows() ? "where" : "which";
    using var process = CreateProcess(locator, [command], Environment.CurrentDirectory, redirectOutput: true, redirectInput: false);
    process.Start();
    process.WaitForExit();
    if (process.ExitCode != 0)
        throw new InvalidOperationException($"Required command '{command}' was not found.");
}

static DeploymentSecrets ReadSecrets(string path)
{
    if (!File.Exists(path))
        throw new FileNotFoundException("Secrets file was not found.", path);

    using var document = JsonDocument.Parse(File.ReadAllText(path));
    var root = document.RootElement;

    return new DeploymentSecrets(
        GetRequiredString(root, "ConnectionStrings", "Postgres"),
        GetRequiredString(root, "InternalApi", "Key"),
        GetRequiredString(root, "ApiKeys", "HashPepper"),
        GetOptionalInt(root, "PublicPort") ?? DefaultPublicPort);
}

static JsonObject CreateAppSettings(DeploymentSecrets secrets) => new()
{
    ["ConnectionStrings"] = new JsonObject { ["Postgres"] = secrets.PostgresConnectionString },
    ["Storage"] = new JsonObject { ["RootPath"] = "/data/resources" },
    ["InternalApi"] = new JsonObject { ["Key"] = secrets.InternalApiKey },
    ["ApiKeys"] = new JsonObject { ["HashPepper"] = secrets.ApiKeysHashPepper },
    ["Database"] = new JsonObject { ["ApplyMigrationsOnStartup"] = false },
    ["Logging"] = new JsonObject
    {
        ["LogLevel"] = new JsonObject
        {
            ["Default"] = "Information",
            ["Microsoft.AspNetCore"] = "Warning"
        }
    },
    ["AllowedHosts"] = "*"
};

static string GetRequiredString(JsonElement root, string section, string key)
{
    if (!root.TryGetProperty(section, out var sectionElement) ||
        !sectionElement.TryGetProperty(key, out var valueElement) ||
        valueElement.ValueKind != JsonValueKind.String ||
        string.IsNullOrWhiteSpace(valueElement.GetString()))
    {
        throw new InvalidOperationException($"Required secret '{section}.{key}' is missing or empty.");
    }

    return valueElement.GetString()!;
}

static int? GetOptionalInt(JsonElement root, string key)
{
    if (!root.TryGetProperty(key, out var value)) return null;
    if (!value.TryGetInt32(out var result) || result is < 1 or > 65535)
        throw new InvalidOperationException($"'{key}' must be an integer between 1 and 65535.");
    return result;
}

static DeploymentOptions ParseArguments(string[] arguments)
{
    var options = new DeploymentOptions();

    for (var index = 0; index < arguments.Length; index++)
    {
        var argument = arguments[index];
        string NextValue()
        {
            if (++index >= arguments.Length)
                throw new ArgumentException($"Missing value for '{argument}'.");
            return arguments[index];
        }

        switch (argument)
        {
            case "--secrets-file" or "-s": options.SecretsFile = NextValue(); break;
            case "--target": options.Target = ParseTarget(NextValue()); break;
            case "--host": options.Host = NextValue(); break;
            case "--ssh-key": options.SshKey = NextValue(); break;
            case "--remote-dir": options.RemoteDirectory = NextValue(); break;
            case "--project-name": options.ProjectName = NextValue(); break;
            case "--no-build": options.NoBuild = true; break;
            case "--pull": options.Pull = true; break;
            case "--down": options.Down = true; break;
            case "--help" or "-h": options.ShowHelp = true; break;
            default: throw new ArgumentException($"Unknown argument '{argument}'.");
        }
    }

    return options;
}

static DeploymentTarget ParseTarget(string value) => value.ToLowerInvariant() switch
{
    "local" => DeploymentTarget.Local,
    "remote" => DeploymentTarget.Remote,
    _ => throw new ArgumentException("--target must be 'local' or 'remote'.")
};

static void ValidateOptions(DeploymentOptions options)
{
    if (string.IsNullOrWhiteSpace(options.SecretsFile))
        throw new ArgumentException("--secrets-file is required.");

    if (options.Target != DeploymentTarget.Remote) return;
    if (string.IsNullOrWhiteSpace(options.Host))
        throw new ArgumentException("--host is required for remote deployment.");
    if (string.IsNullOrWhiteSpace(options.SshKey))
        throw new ArgumentException("--ssh-key is required for remote deployment.");
    if (!File.Exists(options.SshKey))
        throw new FileNotFoundException("SSH key file was not found.", options.SshKey);
}

static string ShellQuote(string value) => $"'{value.Replace("'", "'\\''", StringComparison.Ordinal)}'";

static void PrintUsage() => Console.WriteLine(
    """
    Usage:
      dotnet run deployment/deploy.cs -- --secrets-file /path/to/secrets.json [options]

    Options:
      --target local|remote   Deployment target. Default: local.
      --host user@host       SSH host for remote deployment.
      --ssh-key PATH         SSH private key file.
      --remote-dir PATH      Remote directory. Default: /opt/internal-resource-store.
      --project-name NAME    Docker Compose project name.
      --no-build             Do not rebuild images.
      --pull                 Pull referenced images first.
      --down                 Stop and remove deployment containers.
      --help, -h             Show help.
    """);

static string GetSourceFilePath([CallerFilePath] string path = "") => path;

file sealed class DeploymentOptions
{
    public string? SecretsFile { get; set; }
    public DeploymentTarget Target { get; set; } = DeploymentTarget.Local;
    public string? Host { get; set; }
    public string? SshKey { get; set; }
    public string RemoteDirectory { get; set; } = "/opt/internal-resource-store";
    public string ProjectName { get; set; } = "internal-resource-store";
    public bool NoBuild { get; set; }
    public bool Pull { get; set; }
    public bool Down { get; set; }
    public bool ShowHelp { get; set; }
}

file sealed record DeploymentSecrets(
    string PostgresConnectionString,
    string InternalApiKey,
    string ApiKeysHashPepper,
    int PublicPort);

file enum DeploymentTarget
{
    Local,
    Remote
}
