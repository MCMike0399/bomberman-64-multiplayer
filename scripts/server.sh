#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
exec dotnet run --project src/Bomber.Server/Bomber.Server.csproj -- "$@"
