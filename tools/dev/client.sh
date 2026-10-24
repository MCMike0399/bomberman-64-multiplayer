#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/../.."
dotnet build Bomberman64Multiplayer.csproj --nologo
if [[ -n "${GODOT_BIN:-}" ]]; then
  game_editor="$GODOT_BIN"
elif [[ -x /Applications/Godot_mono.app/Contents/MacOS/Godot ]]; then
  game_editor=/Applications/Godot_mono.app/Contents/MacOS/Godot
else
  game_editor="$(command -v godot-mono || command -v godot || true)"
fi
if [[ -z "$game_editor" ]]; then
  echo 'Set GODOT_BIN to the Godot 4.7.2 .NET executable.' >&2
  exit 1
fi
mkdir -p artifacts
exec "$game_editor" --path . "$@"
