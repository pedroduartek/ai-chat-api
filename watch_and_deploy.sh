
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

while true; do
  # determine current branch
  if ! BRANCH=$(git rev-parse --abbrev-ref HEAD 2>/dev/null); then
    echo "$(date +'%Y-%m-%d %H:%M:%S') - Unable to determine current branch; sleeping"
    sleep "$SLEEP_SECONDS"
    continue
  fi

  # fetch remote updates for this branch
  if ! git fetch origin "$BRANCH" >/dev/null 2>&1; then
    echo "$(date +'%Y-%m-%d %H:%M:%S') - git fetch failed for branch $BRANCH; sleeping"
    sleep "$SLEEP_SECONDS"
    continue
  fi

  # Count commits the remote is ahead of local (we only care if remote is ahead).
  behind=$(git rev-list --count HEAD..origin/"$BRANCH" 2>/dev/null || echo 0)

  if [ "${behind:-0}" -gt 0 ]; then
    echo "$(date +'%Y-%m-%d %H:%M:%S') - Branch $BRANCH is behind origin/$BRANCH by $behind commit(s) — running deploy"
    if [ -f "$REPO_DIR/deploy.sh" ]; then
      chmod +x "$REPO_DIR/deploy.sh" && ("$REPO_DIR/deploy.sh" || echo "$(date +'%Y-%m-%d %H:%M:%S') - Deploy script failed")
    else
      echo "$(date +'%Y-%m-%d %H:%M:%S') - Deploy script not found at $REPO_DIR/deploy.sh"
    fi
  else
    echo "$(date +'%Y-%m-%d %H:%M:%S') - No changes to deploy (branch $BRANCH up-to-date)"
  fi

  sleep "$SLEEP_SECONDS"
done
