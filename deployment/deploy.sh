#!/usr/bin/env sh
set -eu

usage() {
  cat <<'EOF'
Usage:
  deployment/deploy.sh --secrets-file /path/to/secrets.json [options]

Options:
  --secrets-file, -s   Path to external secrets JSON file. Required.
  --target             Deployment target: local or remote. Default: local.
  --host               SSH host for remote deployment, for example user@example.com.
  --ssh-key            Path to SSH key file for remote deployment.
  --remote-dir         Remote project directory. Default: /opt/internal-resource-store.
  --project-name       Docker Compose project name. Default: internal-resource-store.
  --no-build           Run compose up without --build.
  --pull               Pull postgres image before deployment.
  --down               Stop and remove containers.
  --help, -h           Show help.
EOF
}

SECRETS_FILE=""
TARGET="local"
REMOTE_HOST=""
SSH_KEY_FILE=""
REMOTE_DIR="/opt/internal-resource-store"
PROJECT_NAME="internal-resource-store"
NO_BUILD=0
PULL=0
DOWN=0

while [ "$#" -gt 0 ]; do
  case "$1" in
    --secrets-file|-s)
      if [ "$#" -lt 2 ]; then
        echo "Missing value for $1" >&2
        exit 2
      fi
      SECRETS_FILE="$2"
      shift 2
      ;;
    --target)
      if [ "$#" -lt 2 ]; then
        echo "Missing value for $1" >&2
        exit 2
      fi
      TARGET="$2"
      shift 2
      ;;
    --host)
      if [ "$#" -lt 2 ]; then
        echo "Missing value for $1" >&2
        exit 2
      fi
      REMOTE_HOST="$2"
      shift 2
      ;;
    --ssh-key)
      if [ "$#" -lt 2 ]; then
        echo "Missing value for $1" >&2
        exit 2
      fi
      SSH_KEY_FILE="$2"
      shift 2
      ;;
    --remote-dir)
      if [ "$#" -lt 2 ]; then
        echo "Missing value for $1" >&2
        exit 2
      fi
      REMOTE_DIR="$2"
      shift 2
      ;;
    --project-name)
      if [ "$#" -lt 2 ]; then
        echo "Missing value for $1" >&2
        exit 2
      fi
      PROJECT_NAME="$2"
      shift 2
      ;;
    --no-build)
      NO_BUILD=1
      shift
      ;;
    --pull)
      PULL=1
      shift
      ;;
    --down)
      DOWN=1
      shift
      ;;
    --help|-h)
      usage
      exit 0
      ;;
    *)
      echo "Unknown argument: $1" >&2
      usage >&2
      exit 2
      ;;
  esac
done

if [ -z "$SECRETS_FILE" ]; then
  echo "--secrets-file is required." >&2
  usage >&2
  exit 2
fi

if [ "$TARGET" != "local" ] && [ "$TARGET" != "remote" ]; then
  echo "--target must be either local or remote." >&2
  exit 2
fi

if [ "$TARGET" = "remote" ]; then
  if [ -z "$REMOTE_HOST" ]; then
    echo "--host is required for remote deployment." >&2
    exit 2
  fi

  if [ -z "$SSH_KEY_FILE" ]; then
    echo "--ssh-key is required for remote deployment." >&2
    exit 2
  fi

  if [ ! -f "$SSH_KEY_FILE" ]; then
    echo "SSH key file does not exist: $SSH_KEY_FILE" >&2
    exit 1
  fi
fi

if [ ! -f "$SECRETS_FILE" ]; then
  echo "Secrets file does not exist: $SECRETS_FILE" >&2
  exit 1
fi

if ! command -v python3 >/dev/null 2>&1; then
  echo "python3 is required to parse the secrets JSON." >&2
  exit 1
fi

if [ "$TARGET" = "local" ] && ! command -v docker >/dev/null 2>&1; then
  echo "docker is required for local deployment." >&2
  exit 1
fi

if [ "$TARGET" = "remote" ]; then
  if ! command -v ssh >/dev/null 2>&1; then
    echo "ssh is required for remote deployment." >&2
    exit 1
  fi

  if ! command -v tar >/dev/null 2>&1; then
    echo "tar is required for remote deployment." >&2
    exit 1
  fi
fi

SCRIPT_DIR=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd -P)
REPO_ROOT=$(CDPATH= cd -- "$SCRIPT_DIR/.." && pwd -P)
SECRETS_DIR=$(CDPATH= cd -- "$(dirname -- "$SECRETS_FILE")" && pwd -P)
SECRETS_BASENAME=$(basename -- "$SECRETS_FILE")
SECRETS_ABS="$SECRETS_DIR/$SECRETS_BASENAME"

case "$SECRETS_ABS" in
  "$REPO_ROOT"/*)
    echo "Warning: secrets file is inside the project directory. Keep real deployment secrets outside the repository." >&2
    ;;
esac

GENERATED_DIR="$SCRIPT_DIR/.generated"
TEMPLATE_PATH="$SCRIPT_DIR/appsettings.Production.template.json"
APPSETTINGS_PATH="$GENERATED_DIR/appsettings.Production.json"
ENV_PATH="$GENERATED_DIR/deploy.env"
COMPOSE_PATH="$SCRIPT_DIR/docker-compose.deploy.yml"

mkdir -p "$GENERATED_DIR"

python3 - "$SECRETS_ABS" "$TEMPLATE_PATH" "$APPSETTINGS_PATH" "$ENV_PATH" <<'PY'
import json
import sys

secrets_path, template_path, appsettings_path, env_path = sys.argv[1:5]

with open(secrets_path, "r", encoding="utf-8") as file:
    secrets = json.load(file)

def required(path):
    current = secrets
    for part in path.split("."):
        if not isinstance(current, dict) or part not in current:
            raise SystemExit(f"Required secret '{path}' is missing.")
        current = current[part]
    if current is None or str(current).strip() == "":
        raise SystemExit(f"Required secret '{path}' is empty.")
    return str(current)

connection_string = required("ConnectionStrings.Postgres")
internal_api_key = required("InternalApi.Key")
api_keys_hash_pepper = required("ApiKeys.HashPepper")
public_port = str(secrets.get("PublicPort", 32546))

def json_string_content(value):
    return json.dumps(str(value))[1:-1]

with open(template_path, "r", encoding="utf-8") as file:
    appsettings = file.read()

appsettings = appsettings.replace("__POSTGRES_CONNECTION_STRING__", json_string_content(connection_string))
appsettings = appsettings.replace("__INTERNAL_API_KEY__", json_string_content(internal_api_key))
appsettings = appsettings.replace("__API_KEYS_HASH_PEPPER__", json_string_content(api_keys_hash_pepper))

with open(appsettings_path, "w", encoding="utf-8") as file:
    file.write(appsettings)
    file.write("\n")

def env_value(value):
    value = str(value).replace("\\", "\\\\").replace('"', '\\"')
    return f'"{value}"'

with open(env_path, "w", encoding="utf-8") as file:
    file.write(f"PUBLIC_PORT={env_value(public_port)}\n")
PY

COMPOSE_ARGS="--project-name $PROJECT_NAME --env-file $ENV_PATH -f $COMPOSE_PATH"

shell_quote() {
  printf "'"
  printf "%s" "$1" | sed "s/'/'\\''/g"
  printf "'"
}

if [ "$TARGET" = "remote" ]; then
  REMOTE_DIR_QUOTED=$(shell_quote "$REMOTE_DIR")
  REMOTE_COMPOSE_ARGS="--project-name $PROJECT_NAME --env-file deployment/.generated/deploy.env -f deployment/docker-compose.deploy.yml"

  ssh -i "$SSH_KEY_FILE" "$REMOTE_HOST" "mkdir -p $REMOTE_DIR_QUOTED"

  (
    cd "$REPO_ROOT"
    tar \
      --exclude='.git' \
      --exclude='.vs' \
      --exclude='.vscode' \
      --exclude='**/bin' \
      --exclude='**/obj' \
      --exclude='data' \
      -czf - .
  ) | ssh -i "$SSH_KEY_FILE" "$REMOTE_HOST" "tar -xzf - -C $REMOTE_DIR_QUOTED"

  if [ "$DOWN" -eq 1 ]; then
    ssh -i "$SSH_KEY_FILE" "$REMOTE_HOST" "cd $REMOTE_DIR_QUOTED && docker compose $REMOTE_COMPOSE_ARGS down"
    exit 0
  fi

  if [ "$PULL" -eq 1 ]; then
    ssh -i "$SSH_KEY_FILE" "$REMOTE_HOST" "cd $REMOTE_DIR_QUOTED && docker compose $REMOTE_COMPOSE_ARGS pull"
  fi

  if [ "$NO_BUILD" -eq 1 ]; then
    :
  else
    ssh -i "$SSH_KEY_FILE" "$REMOTE_HOST" "cd $REMOTE_DIR_QUOTED && docker compose $REMOTE_COMPOSE_ARGS build internal-resource-store-migrations internal-resource-store-api"
  fi

  ssh -i "$SSH_KEY_FILE" "$REMOTE_HOST" "cd $REMOTE_DIR_QUOTED && docker compose $REMOTE_COMPOSE_ARGS up --force-recreate --abort-on-container-exit --exit-code-from internal-resource-store-migrations internal-resource-store-migrations"
  ssh -i "$SSH_KEY_FILE" "$REMOTE_HOST" "cd $REMOTE_DIR_QUOTED && docker compose $REMOTE_COMPOSE_ARGS up -d --no-deps internal-resource-store-api"

  exit 0
fi

if [ "$DOWN" -eq 1 ]; then
  # shellcheck disable=SC2086
  docker compose $COMPOSE_ARGS down
  exit 0
fi

if [ "$PULL" -eq 1 ]; then
  # shellcheck disable=SC2086
  docker compose $COMPOSE_ARGS pull
fi

if [ "$NO_BUILD" -ne 1 ]; then
  # shellcheck disable=SC2086
  docker compose $COMPOSE_ARGS build internal-resource-store-migrations internal-resource-store-api
fi

# shellcheck disable=SC2086
docker compose $COMPOSE_ARGS up --force-recreate --abort-on-container-exit --exit-code-from internal-resource-store-migrations internal-resource-store-migrations

# shellcheck disable=SC2086
docker compose $COMPOSE_ARGS up -d --no-deps internal-resource-store-api
