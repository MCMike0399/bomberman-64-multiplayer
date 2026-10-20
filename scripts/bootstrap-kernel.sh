#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
revision=b0758339984bbeead1b3d1236f31e980aaa5f20e
repository="${KERNEL_REPOSITORY:-}"
if [ -e burbuja-kernel ]; then
  actual="$(git -C burbuja-kernel rev-parse HEAD 2>/dev/null || true)"
  if [ "$actual" != "$revision" ]; then
    printf 'Existing burbuja-kernel is not pinned revision %s. Kept it unchanged; inspect it before replacing.\n' "$revision" >&2
    exit 1
  fi
  # Keep the external source out of Godot's asset importer and exported resources.
  if [ ! -e burbuja-kernel/.gdignore ]; then : > burbuja-kernel/.gdignore; fi
  printf 'Kernel checkout already present at %s; source files kept unchanged.\n' "$revision"
  exit 0
fi
if [ -z "$repository" ]; then
  printf 'Kernel source is not bundled. Place an authorized checkout at burbuja-kernel/, or set KERNEL_REPOSITORY to an accessible Git origin.\n' >&2
  exit 1
fi
# Authentication uses the caller's normal Git credentials; never put tokens in this file.
git clone --no-checkout "$repository" burbuja-kernel
git -C burbuja-kernel checkout --detach "$revision"
: > burbuja-kernel/.gdignore
printf 'Kernel source ready at %s (ignored by the product repository).\n' "$revision"
