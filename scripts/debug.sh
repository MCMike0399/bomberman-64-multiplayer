#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
# Debug sessions use a window; normal client.sh play still starts fullscreen.
exec ./scripts/client.sh --windowed -- --display-windowed "$@"
