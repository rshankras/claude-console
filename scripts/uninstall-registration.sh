#!/usr/bin/env bash
# Remove what a plugin uninstall leaves behind.
#
# Uninstalling through Logi Options+ removes the PLUGIN and nothing else. Because a sideloaded
# install never gets an application entry from the service, the plugin writes one itself — and that
# entry survives the uninstall. It is not inert: it keeps claiming the terminal, wins activation
# against a plugin that IS installed, and shows a keypad of unresolvable keys. The surviving
# product looks broken and the uninstalled one is nowhere in the plugin list to explain it.
#
# A running plugin sweeps these on load (RegistrationCleanup), which covers the common case of
# uninstalling ONE of two products. It cannot cover uninstalling the LAST one: with no plugin left
# to run, nothing sweeps and the orphan persists indefinitely. That is what this script is for.
#
#   bash scripts/uninstall-registration.sh              # report only
#   bash scripts/uninstall-registration.sh --remove     # actually remove
#
# Only touches entries whose plugin is no longer installed, and never another vendor's.
set -uo pipefail

APPS="$HOME/Library/Application Support/Logi/LogiPluginService/Applications"
PLUGINS="$HOME/Library/Application Support/Logi/LogiPluginService/Plugins"
REMOVE=0
[ "${1:-}" = "--remove" ] && REMOVE=1

[ -d "$APPS" ] || { echo "no Logi Applications directory — nothing to do"; exit 0; }

FOUND=0
for appdir in "$APPS"/*/@_*; do
    [ -d "$appdir" ] || continue
    info="$appdir/ApplicationInfo.json"
    [ -f "$info" ] || continue

    # Ours, identified by the stamp SelfRegistration writes. An unstamped entry belongs to someone
    # else and is never touched, however orphaned it may look.
    plugin="$(python3 - "$info" <<'PY' 2>/dev/null
import json, sys
try:
    d = json.load(open(sys.argv[1]))
except Exception:
    sys.exit(0)
if d.get("selfRegisteredBy"):
    print(d.get("nativePluginName") or "")
PY
)"
    [ -n "$plugin" ] || continue

    # Orphaned only when the plugin it names is gone.
    [ -d "$PLUGINS/$plugin" ] && continue

    FOUND=$((FOUND + 1))
    rel="${appdir#"$APPS"/}"
    if [ "$REMOVE" = "1" ]; then
        rm -rf "$appdir" && echo "removed  $rel  (plugin '$plugin' is not installed)"
    else
        echo "orphaned $rel  (plugin '$plugin' is not installed)"
    fi
done

if [ "$FOUND" -eq 0 ]; then
    echo "no orphaned registrations"
    exit 0
fi

if [ "$REMOVE" = "1" ]; then
    echo
    echo "Restart the Logi Plugin Service so Options+ forgets them:"
    echo "  killall LogiPluginService"
else
    echo
    echo "Re-run with --remove to delete them."
fi
