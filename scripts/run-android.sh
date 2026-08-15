#!/usr/bin/env bash
set -euo pipefail
export ANDROID_HOME="${ANDROID_HOME:-$HOME/Library/Android/sdk}"
export PATH="$ANDROID_HOME/platform-tools:${PATH:-}"
root="$(cd "$(dirname "$0")/.." && pwd)"
app="${1:-resident}"
case "$app" in
  resident) project="$root/src/Apps/Avtomagazin.ResidentApp" ;;
  staff) project="$root/src/Apps/Avtomagazin.StaffApp" ;;
  *)
    echo "usage: $0 [resident|staff]" >&2
    exit 1
    ;;
esac
adb reverse tcp:5100 tcp:5100 >/dev/null 2>&1 || true
cd "$project"
dotnet build -f net10.0-android -t:Run -p:AndroidSdkDirectory="$ANDROID_HOME"
