#!/usr/bin/env bash
# Remove Options+ icon-editor customizations that freeze live keys.
#
# THE TRAP: opening a key in the Options+ icon editor saves a .ict — a SNAPSHOT of that key
# (a baked image plus the literal text that was on it at that moment) — into the profile's
# ActionIcons/. From then on the service renders the .ict and never asks the plugin for an
# image, so live keys stop updating: session keys freeze on "Session 2" forever, the context %
# stops moving, and the Yes/No approval badge stops lighting. Nothing is wrong with the plugin;
# a profile created fresh renders live because it has no .ict files.
#
# This deletes only ClaudeConsole .ict files (yours are backed up first), which restores the
# plugin's own rendering. Deliberate icon customizations are lost — that's the point.
#
# Usage: bash scripts/unfreeze-keys.sh [--apply]      (default: dry run)
set -euo pipefail

APPS="$HOME/Library/Application Support/Logi/LogiPluginService/Applications"
APPLY="${1:-}"
BACKUP="$HOME/Desktop/claude-console-ict-backup-$(date +%Y%m%d-%H%M%S)"

# Plain while-read rather than `mapfile`: macOS ships bash 3.2, which has neither mapfile nor
# readarray, and this script's whole audience is on macOS.
ICTS=()
while IFS= read -r line; do
  [ -n "$line" ] && ICTS+=("$line")
done < <(find "$APPS" -path "*@_claudeconsole*/ActionIcons/*" -name '*ClaudeConsole*.ict' 2>/dev/null || true)

if [ "${#ICTS[@]}" -eq 0 ]; then
  echo "✅ No frozen keys: no ClaudeConsole icon customizations found."
  exit 0
fi

echo "Found ${#ICTS[@]} icon customization(s) overriding live rendering:"
for f in "${ICTS[@]}"; do echo "   • $(basename "$f")"; done

if [ "$APPLY" != "--apply" ]; then
  echo
  echo "Dry run. Re-run with --apply to back them up and remove them:"
  echo "   bash scripts/unfreeze-keys.sh --apply"
  exit 0
fi

# The service caches icons in memory, so it must be stopped BEFORE the files move — otherwise it
# rewrites them on the way out. launchd respawns it.
echo "▶ stopping the Logi Plugin Service…"
killall LogiPluginService 2>/dev/null || true
sleep 3

mkdir -p "$BACKUP"
for f in "${ICTS[@]}"; do
  mv "$f" "$BACKUP/"
done
echo "▶ moved ${#ICTS[@]} file(s) to $BACKUP"

sleep 3
if ! pgrep -x LogiPluginService >/dev/null; then
  echo "▶ service hasn't respawned yet; giving it a moment…"
  sleep 5
fi

echo "▶ restarting Logi Options+ so the UI reconnects…"
killall logioptionsplus_agent 2>/dev/null || true
sleep 4
open "/Library/Application Support/Logitech.localized/LogiOptionsPlus/logioptionsplus_agent.app" 2>/dev/null || true

echo "✅ Done. The session keys, context %, and approval badges should be live again."
echo "   To undo: stop the service and move the files back from $BACKUP"
