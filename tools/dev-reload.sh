#!/usr/bin/env bash
# dev-reload.sh — rebuild a product and get the keypad ACTUALLY showing it again.
#
# Why this exists. `dotnet build` sends the service a plugin RELOAD, not a restart. A reload
# leaves the service's live application list without @_claudeconsole: the disk entry is still
# perfectly valid, so SelfRegistration.RegisterIfMissing() sees nothing missing and does nothing,
# and the service only rebuilds that list from a disk scan AT STARTUP. Net effect: after every
# rebuild the app disappears from the Options+ top strip and the keypad drops to the universal
# layout, while the plugin itself loads fine and its actions are all present. It looks like a
# broken plugin and it is a stale list.
#
# RegistrationHeal is meant to cover this by scheduling a service restart. On a machine whose Logi
# launch agents are missing — this one, since they were removed during 2.0.0 debugging — that
# restart kills the service and NOTHING BRINGS IT BACK, so the heal is worse than no heal: the
# keypad stays dark until somebody notices. Hence this script starts the service itself rather
# than assuming something else will.
#
# Usage:  bash tools/dev-reload.sh [Product]      (default: ClaudeConsole)
#         bash tools/dev-reload.sh --no-build     (just fix the strip, skip the rebuild)
set -euo pipefail

PRODUCT="ClaudeConsole"
BUILD=1
for arg in "$@"; do
  case "$arg" in
    --no-build) BUILD=0 ;;
    -*) echo "unknown option: $arg" >&2; exit 2 ;;
    *) PRODUCT="$arg" ;;
  esac
done

REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
SERVICE_APP="/Applications/Utilities/LogiPluginService.app"
AGENT_APP="/Library/Application Support/Logitech.localized/LogiOptionsPlus/logioptionsplus_agent.app"
DLL="$REPO/bin/$PRODUCT/Debug/bin/${PRODUCT}Plugin.dll"

start_service() {
  open -a "$SERVICE_APP" 2>/dev/null || true
  for _ in $(seq 1 15); do
    pgrep -x LogiPluginService >/dev/null && return 0
    sleep 1
  done
  return 1
}

if [ "$BUILD" = "1" ]; then
  echo ">>> building $PRODUCT"
  # NOT -t:Compile. It shares obj/ with a full build, and a full build that follows it can reuse an
  # output with no embedded resources: the plugin loads and every key renders as bare text. See
  # CLAUDE.md. If you have just run -t:Compile, clear obj/ and bin/ for this product first.
  ( cd "$REPO" && dotnet build "src/Products/$PRODUCT" ) | tail -3
fi

if [ -f "$DLL" ]; then
  size=$(stat -f %z "$DLL")
  echo ">>> $(basename "$DLL"): $size bytes"
  # ~1 MB healthy, ~140 KB means the embedded icons were dropped — the exact signature of an
  # obj/ poisoned by a preceding -t:Compile. Worth catching here rather than while staring at a
  # keypad full of bare text.
  if [ "$size" -lt 400000 ]; then
    echo "    WARNING: that is far too small — the icon resources are missing." >&2
    echo "    rm -rf src/Products/$PRODUCT/obj bin/$PRODUCT and build again." >&2
  fi
fi

echo ">>> restarting the plugin service so it re-reads the application registration"
killall LogiPluginService 2>/dev/null || echo "    (service was not running)"
sleep 2
if ! start_service; then
  echo "error: LogiPluginService did not start. Open Logi Options+ and try again." >&2
  exit 1
fi
echo "    service up as $(pgrep -x LogiPluginService)"

echo ">>> restarting the Options+ UI so it reconnects"
killall logioptionsplus_agent 2>/dev/null || echo "    (Options+ UI was not running)"
sleep 3
open "$AGENT_APP" 2>/dev/null || true
sleep 3

echo
echo "done — the $PRODUCT icon should be back in the Options+ top strip."
echo "if it is not, check the registration is still on disk:"
echo "  ls ~/Library/Application\\ Support/Logi/LogiPluginService/Applications/Loupedeck70/"
