#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
COMPOSE_FILE="$SCRIPT_DIR/infra/docker/compose.prod.yml"
OLLAMA_MODEL="llama3.2:1b"
BRANCH="${1:-main}"

echo "Checking prerequisites..."
command -v git >/dev/null 2>&1 || { echo "Git not found in PATH. Install Git or add it to PATH." >&2; exit 1; }
command -v docker >/dev/null 2>&1 || { echo "Docker not found in PATH. Install Docker or add it to PATH." >&2; exit 1; }

echo "Fetching latest code from origin and discarding local changes (branch: $BRANCH)..."
git fetch origin --prune

# Checkout branch (create if missing) and reset to remote state
if git rev-parse --verify "$BRANCH" >/dev/null 2>&1; then
  git checkout "$BRANCH"
else
  # try to create branch tracking remote, otherwise create new local branch
  git checkout -b "$BRANCH" "origin/$BRANCH" 2>/dev/null || git checkout -b "$BRANCH"
fi

echo "Resetting local branch to origin/$BRANCH"
git reset --hard "origin/$BRANCH"

echo "Removing untracked files and directories"
git clean -fd

echo "Fetching completed. Local repo now matches origin/$BRANCH."

if [[ ! -f "$COMPOSE_FILE" ]]; then
  echo "Compose file not found: $COMPOSE_FILE" >&2
  exit 1
fi

echo "Using embedded OLLAMA_MODEL=$OLLAMA_MODEL (no infra/docker/.env required)"

echo "Bringing down existing containers (if any) and removing local images/volumes..."
# remove containers, local images built by compose, and named volumes to ensure a fresh rebuild
docker compose -f "$COMPOSE_FILE" down --rmi local --volumes --remove-orphans || echo "docker compose down returned non-zero (continuing)"

echo "Building services with no cache (full rebuild)..."
docker compose -f "$COMPOSE_FILE" build --no-cache

echo "Starting services (detached), forcing recreation..."
docker compose -f "$COMPOSE_FILE" up -d --force-recreate

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
