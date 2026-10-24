#!/bin/bash
# Share only the game port with this LAN, through an existing SSH host alias.
set -euo pipefail
state="${TMPDIR:-/tmp}/blast-party-${UID}"
# Keep the Unix socket path below macOS's length limit even with a long TMPDIR.
socket="/tmp/blast-party-${UID}/ssh"
mkdir -p "$state" "$(dirname "$socket")"
chmod 700 "$state" "$(dirname "$socket")"
action="${1:-help}"
host="${BLAST_SSH_HOST:-DiveMac}"
label="io.github.mcmike0399.blastcircuit.party-relay"
case "$action" in
  start)
    address="${2:-$(/usr/sbin/ipconfig getifaddr en0)}"
    port="${BLAST_RELAY_PORT:-18497}"
    if [[ ! "$address" =~ ^[0-9]+\.[0-9]+\.[0-9]+\.[0-9]+$ ]] || [[ "$address" == "0.0.0.0" ]]; then
      echo 'Supply the current Wi-Fi IPv4 address, not a wildcard address.' >&2
      exit 1
    fi
    if launchctl list "$label" >/dev/null 2>&1; then
      echo 'Party relay is already running. Stop it before changing networks.'
      exec bash "$0" status
    fi
    # launchd owns the process after the launching terminal/tool exits and
    # restarts a dropped SSH connection. This is a current-login job only.
    launchctl submit -l "$label" -o "$state/relay.log" -e "$state/relay.log" -- \
      /usr/bin/ssh -M -S "$socket" -o ControlPersist=no -o BatchMode=yes \
      -o ExitOnForwardFailure=yes -o ServerAliveInterval=15 -o ServerAliveCountMax=3 \
      -NT -g -L "$address:$port:127.0.0.1:${BLAST_SERVER_PORT:-18496}" "$host"
    printf 'http://%s:%s\n' "$address" "$port" > "$state/url"
    ready=false
    for attempt in {1..10}; do
      if curl --fail --silent --max-time 2 "$(cat "$state/url")/realtime/v1/games" | grep -q '"blast"'; then
        ready=true
        break
      fi
      sleep 1
    done
    if [[ "$ready" != true ]]; then
      echo "Relay not ready. Inspect $state/relay.log; stop with: bash $0 stop" >&2
      exit 1
    fi
    echo 'Game server address for everyone on this Wi-Fi:'
    cat "$state/url"
    curl --fail --silent --show-error --max-time 10 "$(cat "$state/url")/realtime/v1/games"
    printf '\nKeep this Mac awake and connected during the party.\n'
    ;;
  status)
    ssh -S "$socket" -O check "$host"
    cat "$state/url"
    curl --fail --silent --show-error --max-time 10 "$(cat "$state/url")/realtime/v1/games"
    printf '\n'
    ;;
  stop)
    # Only the task-owned job, never the user's ordinary SSH connections.
    launchctl remove "$label"
    ;;
  *)
    echo 'Usage: bash tools/party/party-relay.sh start [LAN_IP] | status | stop'
    echo 'Optional env: BLAST_SSH_HOST, BLAST_SERVER_PORT, BLAST_RELAY_PORT'
    ;;
esac
