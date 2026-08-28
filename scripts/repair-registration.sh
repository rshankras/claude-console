#!/usr/bin/env bash
# Repair the vanished Claude Console icon after an uninstall → reinstall.
#
# Uninstalling removes the application registration from the running service's MEMORY but keeps
# it on DISK (that is how profiles survive reinstalls); the reinstall then sees the on-disk entry
# and silently skips re-registering it. The Claude Console icon disappears from Options+ and the
# keypad drops to the default layout — while every file on disk is correct and the plugin loads
# fine. The service rebuilds its application list from the disk scan at startup, so restarting
# the service (and then the Options+ UI, so it reconnects) is the entire fix.
#
# This file is installed to ~/.claude/claude-console/scripts/ by the plugin on every load, so a
# Marketplace user has it without the repo (#45):
#
#   bash ~/.claude/claude-console/scripts/repair-registration.sh
#
# Takes the registration name as its only argument for the other products (default: claudeconsole).
set -euo pipefail

NAME="${1:-claudeconsole}"
APPDIR="$HOME/Library/Application Support/Logi/LogiPluginService/Applications/Loupedeck70/@_$NAME"

if [ ! -d "$APPDIR" ]; then
  echo "No @_$NAME registration on disk ($APPDIR)." >&2
  echo "This script only fixes the vanished-icon-after-reinstall case. For other symptoms," >&2
  echo "see the Troubleshooting section of the README." >&2
  exit 1
fi

echo ">>> registration is present on disk — restarting the Logi Plugin Service so it re-reads it"
killall LogiPluginService 2>/dev/null || echo "    (service was not running)"
sleep 8

# The service is normally relaunched by Logi's own agents. Where those agents are absent it stays
# down, and this script used to stop here and tell the user to open Options+ — which left the
# keypad dark in the middle of the very repair meant to fix it. Start it ourselves and only give
# up if that fails too.
if ! pgrep -x LogiPluginService >/dev/null; then
  echo "    service did not come back on its own — starting it"
  open -a "/Applications/Utilities/LogiPluginService.app" 2>/dev/null || true
  for _ in $(seq 1 15); do
    pgrep -x LogiPluginService >/dev/null && break
    sleep 1
  done
fi

if ! pgrep -x LogiPluginService >/dev/null; then
  echo "error: LogiPluginService could not be started — open Logi Options+ to start it." >&2
  exit 1
fi
echo "    service up as $(pgrep -x LogiPluginService)"

echo ">>> restarting the Options+ UI so it reconnects to the healed service"
killall logioptionsplus_agent 2>/dev/null || echo "    (Options+ UI was not running)"
sleep 5
open "/Library/Application Support/Logitech.localized/LogiOptionsPlus/logioptionsplus_agent.app" 2>/dev/null || true

echo "done — the @_$NAME icon should be back in the Options+ top strip."
