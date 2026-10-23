#!/bin/bash
# One verified update transaction. All paths arrive as positional arguments, never shell code.
set -eu
trap '' HUP
app=$1
stage=$2
version=$3
token=$4
old_pid=$5
history=$6
exec > "$history.log" 2>&1 < /dev/null
windowed=${7:-normal}
parent=$(dirname "$app")
[[ "$token" =~ ^[a-f0-9]{32}$ && "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ && "$old_pid" =~ ^[1-9][0-9]*$ ]] || exit 0
[[ "$stage" == "$parent/.blast-update-$token" && "$app" == *.app && -d "$app" && ! -L "$app" && ! -L "$stage" ]] || exit 0
new_app="$stage/extracted/Blast Circuit.app"
backup="$stage/previous.app"
marker="$stage/healthy"
lock="$parent/.blast-update-lock"
if ! mkdir "$lock" 2>/dev/null; then exit 0; fi
trap 'rmdir "$lock" 2>/dev/null || true' EXIT
for ((i=0; i<60; i++)); do
  if ! kill -0 "$old_pid" 2>/dev/null; then break; fi
  sleep 1
done
if kill -0 "$old_pid" 2>/dev/null; then
  printf 'cancelled %s app-still-running\n' "$version" > "$history"
  exit 0
fi
if ! /usr/bin/codesign --verify --deep --strict "$new_app"; then
  printf 'failed %s invalid-code-signature\n' "$version" > "$history"
  exit 0
fi
if ! mv "$app" "$backup"; then printf 'failed %s cannot-back-up\n' "$version" > "$history"; exit 0; fi
if ! mv "$new_app" "$app"; then
  mv "$backup" "$app"
  printf 'failed %s cannot-install\n' "$version" > "$history"
  exit 0
fi
launch_args=(-- --update-token="$token")
if [[ "$windowed" == windowed ]]; then launch_args=(--windowed -- --display-windowed --update-token="$token"); fi
/usr/bin/open -n "$app" --args "${launch_args[@]}" || true
new_pid=""
healthy=false
for ((i=0; i<45; i++)); do
  if [[ -f "$stage/newpid" ]]; then new_pid=$(cat "$stage/newpid"); fi
  if [[ "$new_pid" =~ ^[1-9][0-9]*$ && -f "$marker" && "$(cat "$marker")" == "$version" ]] && kill -0 "$new_pid" 2>/dev/null; then healthy=true; break; fi
  if [[ "$new_pid" =~ ^[1-9][0-9]*$ ]] && ! kill -0 "$new_pid" 2>/dev/null; then break; fi
  sleep 1
done
if [[ "$healthy" == true ]]; then
  printf 'installed %s\n' "$version" > "$history"
  rm -rf -- "$backup"
else
  new_pid=$(/usr/bin/pgrep -f -- "--update-token=$token" | head -1 || true)
  if [[ "$new_pid" =~ ^[1-9][0-9]*$ ]] && kill -0 "$new_pid" 2>/dev/null; then
    kill "$new_pid" 2>/dev/null || true
    sleep 2
    kill -KILL "$new_pid" 2>/dev/null || true
  fi
  wait "$new_pid" 2>/dev/null || true
  mv "$app" "$stage/failed.app"
  mv "$backup" "$app"
  printf 'failed %s launch-rollback\n' "$version" > "$history"
  if [[ "$windowed" == windowed ]]; then /usr/bin/open -n "$app" --args --windowed -- --display-windowed; else /usr/bin/open -n "$app"; fi
  rm -rf -- "$stage/failed.app"
fi
# Keep the small launch log/receipt; remove installers and verified download payload.
rm -rf -- "$stage/extracted"
rm -f -- "$stage/update.zip" "$stage/install.sh" "$marker"
exit 0
