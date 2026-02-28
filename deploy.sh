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

if [[ -f "$ENV_FILE" ]]; then
  # Load env vars from infra/docker/.env so OLLAMA_MODEL and others are available
  # shellcheck disable=SC1090
  set -a
  # Use a subshell to avoid exporting local shell functions
  . "$ENV_FILE"
  set +a
else
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

# If an Ollama model is configured, try to pull it into the Ollama container
if [[ -n "${OLLAMA_MODEL:-}" ]]; then
  echo "OLLAMA_MODEL is set to '$OLLAMA_MODEL' — will wait 20s then pull model into Ollama..."

  echo "Sleeping 20s to allow Ollama to start..."
  sleep 20

  echo "Attempting to pull Ollama model: $OLLAMA_MODEL"
  max_pull_attempts=5
  pull_attempt=0
  pulled=false
  while [[ $pull_attempt -lt $max_pull_attempts ]]; do
    pull_attempt=$((pull_attempt+1))
    echo "  pull attempt $pull_attempt/$max_pull_attempts..."
    if docker compose -f "$COMPOSE_FILE" exec ollama ollama pull "$OLLAMA_MODEL"; then
      echo "Model $OLLAMA_MODEL pulled successfully."
      pulled=true
      break
    else
      echo "  pull attempt $pull_attempt failed. Showing recent Ollama logs (tail 30):"
      docker compose -f "$COMPOSE_FILE" logs --no-color --tail=30 ollama || true
      sleep $((5 * pull_attempt))
    fi
  done

  if [[ "$pulled" != true ]]; then
    echo "Failed to pull model $OLLAMA_MODEL after $max_pull_attempts attempts." >&2
    echo "You can try manually: docker compose -f infra/docker/compose.prod.yml exec ollama ollama pull $OLLAMA_MODEL" >&2
    exit 2
  fi
fi

exit 0
