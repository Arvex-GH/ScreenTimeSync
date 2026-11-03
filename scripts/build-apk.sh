#!/usr/bin/env bash
set -e
# Build APK locally without installing Android SDK — uses Docker
# Requires: docker
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
if ! command -v docker >/dev/null; then
  echo "Docker not found. Alternative: cd android-app && ./gradlew assembleDebug"
  exit 1
fi
docker run --rm -v "$ROOT/android-app:/project" -w /project \
  ghcr.io/cirruslabs/android-sdk:34 \
  bash -c "gradle assembleDebug && cp app/build/outputs/apk/debug/app-debug.apk /project/../releases/ScreenTimeSync-v1.0.0.apk && ls -lh ../releases/"
echo "APK at releases/ScreenTimeSync-v1.0.0.apk"
