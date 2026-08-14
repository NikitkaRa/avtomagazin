#!/usr/bin/env bash
set -euo pipefail
export ANDROID_HOME="${ANDROID_HOME:-$HOME/Library/Android/sdk}"
root="$(cd "$(dirname "$0")/.." && pwd)"
cd "$root/src/Apps/Avtomagazin.Mobile"
dotnet build -f net10.0-android -t:Run -p:AndroidSdkDirectory="$ANDROID_HOME"
