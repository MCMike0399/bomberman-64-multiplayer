#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/../.."
# Debug sessions use a window; normal tools/dev/client.sh play still starts fullscreen.
exec ./tools/dev/client.sh --windowed -- --display-windowed "$@"
