
#!/usr/bin/env bash
set -euo pipefail

# Simple watcher: every 5s check if current branch is behind its remote and
# run `chmod +x deploy.sh && ./deploy.sh` when it is. Logs each check.

# Determine repository root: prefer git top-level, fall back to script dir
if git_root=$(git rev-parse --show-toplevel 2>/dev/null); then
  REPO_DIR="$git_root"
else
  REPO_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
fi
cd "$REPO_DIR" || exit 1

SLEEP_SECONDS=5
PAUSE_AFTER_DEPLOY=300 # seconds to pause after a deploy (5 minutes)

while true; do
  # determine current branch
  if ! BRANCH=$(git rev-parse --abbrev-ref HEAD 2>/dev/null); then
    # ensure we start a new line if a status was being updated in-place
    printf "\n"
    echo "$(date +'%Y-%m-%d %H:%M:%S') - Unable to determine current branch; sleeping"
    sleep "$SLEEP_SECONDS"
    continue
  fi


  # Stash infra/docker/.env if it exists before fetch/reset
  if [ -f "infra/docker/.env" ]; then
    cp infra/docker/.env /tmp/.env.stash.$$ || true
  fi

  # fetch remote updates for this branch
  if ! git fetch origin "$BRANCH" >/dev/null 2>&1; then
    printf "\n"
    echo "$(date +'%Y-%m-%d %H:%M:%S') - git fetch failed for branch $BRANCH; sleeping"
    # Restore .env if it was stashed
    if [ -f /tmp/.env.stash.$$ ]; then
      mv /tmp/.env.stash.$$ infra/docker/.env
    fi
    sleep "$SLEEP_SECONDS"
    continue
  fi

  # Count commits the remote is ahead of local (we only care if remote is ahead).
  behind=$(git rev-list --count HEAD..origin/"$BRANCH" 2>/dev/null || echo 0)

  if [ "${behind:-0}" -gt 0 ]; then
    # Restore .env if it was stashed before deploy
    if [ -f /tmp/.env.stash.$$ ]; then
      mv /tmp/.env.stash.$$ infra/docker/.env
    fi
    # print a newline to finalize the inline status line, then log and run deploy
    printf "\n"
    echo "$(date +'%Y-%m-%d %H:%M:%S') - Branch $BRANCH is behind origin/$BRANCH by $behind commit(s) — running deploy"
    if [ -f "$REPO_DIR/deploy.sh" ]; then
      chmod +x "$REPO_DIR/deploy.sh" && ("$REPO_DIR/deploy.sh" || echo "$(date +'%Y-%m-%d %H:%M:%S') - Deploy script failed")
    else
      echo "$(date +'%Y-%m-%d %H:%M:%S') - Deploy script not found at $REPO_DIR/deploy.sh"
    fi
    # pause checks for a while after triggering deploy to avoid immediate re-checks
    echo "$(date +'%Y-%m-%d %H:%M:%S') - Pausing checks for $PAUSE_AFTER_DEPLOY seconds"
    sleep "$PAUSE_AFTER_DEPLOY"
  else
    # update same line in-place with the new timestamp (no newline)
    echo -ne "\rNo changes to deploy (branch $BRANCH up-to-date) - $(date +'%Y-%m-%d %H:%M:%S')"
  fi

  sleep "$SLEEP_SECONDS"
done
