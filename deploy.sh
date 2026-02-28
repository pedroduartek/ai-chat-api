#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
COMPOSE_FILE="$SCRIPT_DIR/infra/docker/compose.prod.yml"
ENV_FILE="$SCRIPT_DIR/infra/docker/.env"
BRANCH="${1:-main}"

echo "Checking prerequisites..."
command -v git >/dev/null 2>&1 || { echo "Git not found in PATH. Install Git or add it to PATH." >&2; exit 1; }
command -v docker >/dev/null 2>&1 || { echo "Docker not found in PATH. Install Docker or add it to PATH." >&2; exit 1; }

echo "Pulling latest code from origin/$BRANCH..."
git pull origin "$BRANCH"

if [[ ! -f "$COMPOSE_FILE" ]]; then
  echo "Compose file not found: $COMPOSE_FILE" >&2
  exit 1
fi

if [[ ! -f "$ENV_FILE" ]]; then
  echo "Warning: env file not found: $ENV_FILE" >&2
  echo "If you need environment variables, create $ENV_FILE on the VPS before running this script. Continuing..."
fi

echo "Bringing down existing containers (if any)..."
docker compose -f "$COMPOSE_FILE" down --volumes --remove-orphans || echo "docker compose down returned non-zero (continuing)"

echo "Building and starting services (detached)..."
docker compose -f "$COMPOSE_FILE" up -d --build

echo "Optional cleanup: pruning unused images and containers..."
docker container prune -f || true
docker image prune -f || true

echo "Deployment finished successfully."

exit 0
