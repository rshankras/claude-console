#!/usr/bin/env bash
# Repair the vanished Claude Console icon after an uninstall → reinstall.
#
# Uninstalling removes the application registration from the running service's MEMORY but keeps
# it on DISK (that is how profiles survive reinstalls); the reinstall then sees the on-disk entry
# and silently skips re-registering it. The Claude Console icon disappears from Options+ and the
# keypad drops to the default layout — while every file on disk is correct and the plugin loads
# fine. The service rebuilds its application list from the disk scan at startup, so restarting
# the service (and then the Options+ UI, so it reconnects) is the entire fix.
set -euo pipefail

APPDIR="$HOME/Library/Application Support/Logi/LogiPluginService/Applications/Loupedeck70/@_claudeconsole"

if [ ! -d "$APPDIR" ]; then
  echo "No Claude Console registration on disk ($APPDIR)." >&2
  echo "This script only fixes the vanished-icon-after-reinstall case. For other symptoms," >&2
  echo "see the Troubleshooting section of the README." >&2
  exit 1
fi

echo ">>> registration is present on disk — restarting the Logi Plugin Service so it re-reads it"
killall LogiPluginService 2>/dev/null || echo "    (service was not running)"
sleep 8
if ! pgrep -x LogiPluginService >/dev/null; then
  echo "error: LogiPluginService did not come back on its own — open Logi Options+ to start it." >&2
  exit 1
fi

echo ">>> restarting the Options+ UI so it reconnects to the healed service"
killall logioptionsplus_agent 2>/dev/null || echo "    (Options+ UI was not running)"
sleep 5
open "/Library/Application Support/Logitech.localized/LogiOptionsPlus/logioptionsplus_agent.app" 2>/dev/null || true

echo "done — the Claude Console icon should be back in the Options+ top strip."
