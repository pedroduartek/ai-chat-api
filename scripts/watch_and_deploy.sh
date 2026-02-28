#!/usr/bin/env bash
set -u

# Watch repository's main branch and run deploy when local is behind remote.
# Place this script in the repo (e.g. ./scripts/watch_and_deploy.sh) and run it in background.

REPO_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$REPO_DIR" || exit 1

BRANCH="main"
REMOTE="origin"
DEPLOY_SCRIPT="./deploy.sh"
SLEEP_SECONDS=5

while true; do
  # Fetch latest remote branch state; if fetch fails, wait and retry
  if ! git fetch "$REMOTE" "$BRANCH" >/dev/null 2>&1; then
    sleep "$SLEEP_SECONDS"
    continue
  fi

  # Resolve commits; if refs not available, wait
  if ! LOCAL=$(git rev-parse "$BRANCH" 2>/dev/null); then
    sleep "$SLEEP_SECONDS"
    continue
  fi
  if ! REMOTE_REF=$(git rev-parse "$REMOTE/$BRANCH" 2>/dev/null); then
    sleep "$SLEEP_SECONDS"
    continue
  fi

  BASE=$(git merge-base "$BRANCH" "$REMOTE/$BRANCH" 2>/dev/null || true)

  if [ "$LOCAL" = "$REMOTE_REF" ]; then
    # up-to-date
    :
  elif [ "$LOCAL" = "$BASE" ]; then
    # local is behind remote
    echo "$(date +'%Y-%m-%d %H:%M:%S') - Detected behind $REMOTE/$BRANCH, running deploy script"
    if [ -f "$DEPLOY_SCRIPT" ]; then
      chmod +x "$DEPLOY_SCRIPT" && ("$DEPLOY_SCRIPT" || echo "Deploy script exited with non-zero status")
    else
      echo "Deploy script not found: $DEPLOY_SCRIPT"
    fi
  else
    # local is ahead or diverged; do nothing
    :
  fi

  sleep "$SLEEP_SECONDS"
done
