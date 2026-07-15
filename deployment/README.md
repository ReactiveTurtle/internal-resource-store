# Deployment

Deployment secrets are passed as a separate file argument and are not stored in the project.

The script generates runtime files under `deployment/.generated/`. This folder is ignored by git and excluded from Docker build context.

## Secrets File

Create a secrets file outside the repository, for example:

```powershell
C:\secrets\internal-resource-store.secrets.json
```

Format:

```json
{
  "ConnectionStrings": {
    "Postgres": "Host=postgres.example.internal;Port=5432;Database=internal_resource_store;Username=internal_resource_store;Password=strong-password"
  },
  "InternalApi": {
    "Key": "strong-internal-api-key"
  },
  "ApiKeys": {
    "HashPepper": "strong-long-random-pepper"
  },
  "PublicPort": 8080
}
```

Postgres is not deployed as a container by the deployment compose file. The service receives only `ConnectionStrings:Postgres` from the external secrets file.

## Deploy With sh

Local deploy from repository root:

```sh
sh ./deployment/deploy.sh \
  --target local \
  --secrets-file /opt/secrets/internal-resource-store.secrets.json
```

From repository root:

```sh
sh ./deployment/deploy.sh --secrets-file /opt/secrets/internal-resource-store.secrets.json
```

Deploy without rebuilding the API image:

```sh
sh ./deployment/deploy.sh --secrets-file /opt/secrets/internal-resource-store.secrets.json --no-build
```

Stop containers:

```sh
sh ./deployment/deploy.sh --secrets-file /opt/secrets/internal-resource-store.secrets.json --down
```

Remote deploy over SSH:

```sh
sh ./deployment/deploy.sh \
  --target remote \
  --host deploy@example.com \
  --ssh-key ~/.ssh/id_ed25519 \
  --remote-dir /opt/internal-resource-store \
  --secrets-file /opt/secrets/internal-resource-store.secrets.json
```

Remote stop:

```sh
sh ./deployment/deploy.sh \
  --target remote \
  --host deploy@example.com \
  --ssh-key ~/.ssh/id_ed25519 \
  --remote-dir /opt/internal-resource-store \
  --secrets-file /opt/secrets/internal-resource-store.secrets.json \
  --down
```

Remote mode requires on the local machine:

- `sh`
- `python3`
- `ssh`
- `tar`

Remote mode requires on the server:

- Docker
- Docker Compose plugin
- network access to the external PostgreSQL host from the API container

The `--ssh-key` value is the SSH identity file used by `ssh -i`. In typical SSH setups this is the private key file that matches a public key installed on the server.

If the file has executable permissions:

```sh
./deployment/deploy.sh --secrets-file /opt/secrets/internal-resource-store.secrets.json
```

## Deploy With PowerShell

From repository root:

```powershell
.\deployment\deploy.ps1 -SecretsFile "C:\secrets\internal-resource-store.secrets.json"
```

Deploy without rebuilding the API image:

```powershell
.\deployment\deploy.ps1 -SecretsFile "C:\secrets\internal-resource-store.secrets.json" -NoBuild
```

Stop containers:

```powershell
.\deployment\deploy.ps1 -SecretsFile "C:\secrets\internal-resource-store.secrets.json" -Down
```

## How Secrets Are Applied

The script reads the external secrets file and generates:

- `deployment/.generated/appsettings.Production.json`
- `deployment/.generated/deploy.env`

`appsettings.Production.json` is mounted into the API container as:

```text
/app/appsettings.Production.json
```

The generated file is mounted at runtime, not baked into the Docker image.

In remote mode the generated deployment files are copied to `--remote-dir` together with the project files, then Docker Compose is executed on the remote host.

## Migrations

Deployment runs database migrations as a separate executable container before the API starts:

```text
internal-resource-store-migrations
```

The deploy script sequence is:

```text
build images
run internal-resource-store-migrations with --force-recreate
start internal-resource-store-api
```

The API service does not apply migrations on startup in deployment mode.
