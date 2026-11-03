#!/usr/bin/env bash
set -e
# One-command public repo creation + push
# Usage: ./scripts/push-to-github.sh YOUR_GITHUB_USERNAME [repo-name]
# Requires: gh CLI authenticated (gh auth login) OR git + manual GitHub repo creation

USER="${1:-}"
REPO="${2:-ScreenTimeSync}"
if [ -z "$USER" ]; then
  echo "Usage: $0 YOUR_GITHUB_USERNAME [repo-name]"
  echo "Example: $0 octocat"
  exit 1
fi
cd "$(dirname "$0")/.."

# Ensure git repo
if [ ! -d .git ]; then
  git init
  git branch -M main
  git add .
  git commit -m "Initial commit: ScreenTimeSync Windows+Android Bluetooth screen time"
fi

if command -v gh >/dev/null; then
  echo "Creating public repo $USER/$REPO via gh..."
  gh repo create "$USER/$REPO" --public --source=. --remote=origin --push || {
    echo "Repo may exist; pushing..."
    git remote add origin "https://github.com/$USER/$REPO.git" 2>/dev/null || true
    git push -u origin main
  }
else
  echo "gh CLI not found. Create repo manually:"
  echo "  1. Go to https://github.com/new → Name: $REPO → Public → Create"
  echo "  2. Then run:"
  echo "     git remote add origin https://github.com/$USER/$REPO.git"
  echo "     git push -u origin main"
  echo ""
  echo "Attempting push if remote exists..."
  git push -u origin main || true
fi
echo "Done. Repo: https://github.com/$USER/$REPO"
