param(
    [Parameter(Mandatory = $true)]
    [string]$SecretsFile,

    [string]$ProjectName = "internal-resource-store",

    [switch]$NoBuild,

    [switch]$Pull,

    [switch]$Down
)

$ErrorActionPreference = "Stop"

function Get-RequiredValue {
    param(
        [Parameter(Mandatory = $true)] [object]$Root,
        [Parameter(Mandatory = $true)] [string]$Path
    )

    $current = $Root
    foreach ($part in $Path.Split('.')) {
        if ($null -eq $current -or -not ($current.PSObject.Properties.Name -contains $part)) {
            throw "Required secret '$Path' is missing."
        }

        $current = $current.$part
    }

    if ($null -eq $current -or [string]::IsNullOrWhiteSpace([string]$current)) {
        throw "Required secret '$Path' is empty."
    }

    return [string]$current
}

function ConvertTo-EnvValue {
    param([Parameter(Mandatory = $true)] [string]$Value)

    $escaped = $Value.Replace("\", "\\").Replace('"', '\"')
    return '"' + $escaped + '"'
}

function Escape-JsonString {
    param([Parameter(Mandatory = $true)] [string]$Value)

    return $Value.Replace("\", "\\").Replace('"', '\"')
}

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Resolve-Path -LiteralPath (Join-Path $scriptRoot "..")
$resolvedSecretsFile = Resolve-Path -LiteralPath $SecretsFile

if ($resolvedSecretsFile.Path.StartsWith($repoRoot.Path, [StringComparison]::OrdinalIgnoreCase)) {
    Write-Warning "Secrets file is inside the project directory. Keep real deployment secrets outside the repository."
}

$secrets = Get-Content -LiteralPath $resolvedSecretsFile.Path -Raw | ConvertFrom-Json

$connectionString = Get-RequiredValue $secrets "ConnectionStrings.Postgres"
$internalApiKey = Get-RequiredValue $secrets "InternalApi.Key"
$apiKeysHashPepper = Get-RequiredValue $secrets "ApiKeys.HashPepper"

$publicPort = 8080
if ($secrets.PSObject.Properties.Name -contains "PublicPort" -and $null -ne $secrets.PublicPort) {
    $publicPort = [int]$secrets.PublicPort
}

$generatedDir = Join-Path $scriptRoot ".generated"
if (-not (Test-Path -LiteralPath $generatedDir)) {
    New-Item -ItemType Directory -Path $generatedDir | Out-Null
}

$templatePath = Join-Path $scriptRoot "appsettings.Production.template.json"
$appsettingsPath = Join-Path $generatedDir "appsettings.Production.json"
$envPath = Join-Path $generatedDir "deploy.env"
$composePath = Join-Path $scriptRoot "docker-compose.deploy.yml"

$appsettings = Get-Content -LiteralPath $templatePath -Raw
$appsettings = $appsettings.Replace("__POSTGRES_CONNECTION_STRING__", (Escape-JsonString $connectionString))
$appsettings = $appsettings.Replace("__INTERNAL_API_KEY__", (Escape-JsonString $internalApiKey))
$appsettings = $appsettings.Replace("__API_KEYS_HASH_PEPPER__", (Escape-JsonString $apiKeysHashPepper))
Set-Content -LiteralPath $appsettingsPath -Value $appsettings -Encoding UTF8

$envContent = @(
    "PUBLIC_PORT=$(ConvertTo-EnvValue ([string]$publicPort))"
) -join [Environment]::NewLine
Set-Content -LiteralPath $envPath -Value $envContent -Encoding UTF8

$composeArgs = @(
    "compose",
    "--project-name", $ProjectName,
    "--env-file", $envPath,
    "-f", $composePath
)

if ($Down) {
    & docker @composeArgs "down"
    exit $LASTEXITCODE
}

if ($Pull) {
    & docker @composeArgs "pull"
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

if (-not $NoBuild) {
    & docker @composeArgs "build" "internal-resource-store-migrations" "internal-resource-store-api"
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

& docker @composeArgs "up" "--force-recreate" "--abort-on-container-exit" "--exit-code-from" "internal-resource-store-migrations" "internal-resource-store-migrations"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

& docker @composeArgs "up" "-d" "--no-deps" "internal-resource-store-api"
exit $LASTEXITCODE
