# internal-resource-store

Universal internal resource storage service for domain applications. The first version stores and serves images only.

## Architecture

The solution follows DDD and Clean Architecture:

```text
src/InternalResourceStore.Domain
src/InternalResourceStore.Application
src/InternalResourceStore.Infrastructure
src/InternalResourceStore.Api
```

Dependency direction:

```text
Api -> Application
Api -> Infrastructure
Infrastructure -> Application -> Domain
Infrastructure -> Domain
Domain -> no dependencies
```

Layer responsibilities:

- `Api`: HTTP contract, DTO boundary validation, headers, request/response mapping.
- `Application`: use cases and business checks, including API key ownership and system variable validation.
- `Domain`: entity invariants and state transitions.
- `Infrastructure`: EF Core/Postgres, file storage, ImageSharp, hashing, background worker.

The service does not know application domains, domain entity types, entity ids, fields, guilds, owners, users, or permissions.

## Access Model

External users must never call this service directly. Domain applications expose their own endpoints, validate users and domain access, then call this service internally.

There are two key types:

- `X-Internal-Api-Key`: configured key for `/internal/*` administration endpoints.
- `X-Api-Key`: generated application key for resource endpoints.

Application API keys are stored as hashes only. A resource stores `owner_api_key_hash`, and the service allows access only when the request key hash matches the resource owner hash.

## Data

PostgreSQL schema:

```text
internal_resource_store
```

Tables:

- `api_keys`
- `resources`
- `system_variables`

Resources store only:

- internal resource id
- storage key
- owner API key hash
- MIME type
- file size
- image width/height
- created/deleted/purged timestamps

## Images

Supported MIME types:

- `image/png`
- `image/jpeg`

Upload flow:

- API validates multipart boundary cases.
- Application validates API key and scenario rules.
- Infrastructure decodes the image with ImageSharp.
- The image is re-encoded to remove metadata.
- No resize or width/height limit is applied.
- The sanitized file is saved in the API container file volume.

## Soft Delete Cleanup

`DELETE /resources/{resourceId}` sets `deleted_at`. The file is not removed immediately.

The cleanup worker runs inside the API process. It reads runtime settings from `system_variables` on each cycle:

- `resource_soft_delete_retention_days`, default `30`
- `resource_cleanup_interval_minutes`, default `60`

When a soft-deleted resource is older than retention, the worker deletes the physical file and sets `purged_at`. The DB row remains.

## Docker

Copy `.env.example` to `.env` and change secrets.

```bash
docker compose up --build
```

Docker volumes for development compose:

- `internal_resource_store_files`: API resource files at `/data/resources`
- `internal_resource_store_postgres`: Postgres data at `/var/lib/postgresql/data`

The deployment compose in `deployment/docker-compose.deploy.yml` does not start Postgres. Deployment uses an external PostgreSQL connection string from the secrets file.

Deployment applies EF Core migrations through a separate executable program/container, `internal-resource-store-migrations`, before starting the API container.

## Deployment Script

For deployment with secrets stored outside the repository, use:

```sh
sh ./deployment/deploy.sh --secrets-file /opt/secrets/internal-resource-store.secrets.json
```

Remote deployment over SSH:

```sh
sh ./deployment/deploy.sh \
  --target remote \
  --host deploy@example.com \
  --ssh-key ~/.ssh/id_ed25519 \
  --remote-dir /opt/internal-resource-store \
  --secrets-file /opt/secrets/internal-resource-store.secrets.json
```

The script generates ignored runtime config in `deployment/.generated/` and mounts `appsettings.Production.json` into the API container. See `deployment/README.md`.

## Endpoints

Internal administration:

```text
POST /internal/api-keys
GET  /internal/system-variables
PUT  /internal/system-variables/{key}
```

Resources:

```text
POST   /resources/images
GET    /resources/{resourceId}
GET    /resources/{resourceId}/metadata
DELETE /resources/{resourceId}
```

Health:

```text
GET /health
```

## Examples

Create application API key:

```bash
curl -X POST http://localhost:8080/internal/api-keys \
  -H "X-Internal-Api-Key: change-me-internal-key" \
  -H "Content-Type: application/json" \
  -d '{"name":"extranet"}'
```

Upload image:

```bash
curl -X POST http://localhost:8080/resources/images \
  -H "X-Api-Key: <application-api-key>" \
  -F "file=@image.png;type=image/png"
```

Get metadata:

```bash
curl http://localhost:8080/resources/<resource-id>/metadata \
  -H "X-Api-Key: <application-api-key>"
```

Get file bytes:

```bash
curl http://localhost:8080/resources/<resource-id> \
  -H "X-Api-Key: <application-api-key>" \
  --output image.png
```

Soft-delete resource:

```bash
curl -X DELETE http://localhost:8080/resources/<resource-id> \
  -H "X-Api-Key: <application-api-key>"
```

Update retention without restart:

```bash
curl -X PUT http://localhost:8080/internal/system-variables/resource_soft_delete_retention_days \
  -H "X-Internal-Api-Key: change-me-internal-key" \
  -H "Content-Type: application/json" \
  -d '{"value":"14"}'
```
