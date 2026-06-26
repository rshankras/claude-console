#!/usr/bin/env bash
# Claude Console — leftover-data cleanup. (This is NOT the plugin uninstaller.)
#
# To uninstall the PLUGIN, do it in Logi Options+: right-click the Claude Console
# plugin -> Uninstall (or run: logiplugintool uninstall ClaudeConsole), and delete the
# imported "Claude Console — Keypad" profile there too. That is the actual uninstall.
#
# This script only clears the app-level leftovers that Logi Options+ can't see and never
# removes: the voice runtime + ~142 MB speech model, the /tmp IPC files, the Microphone
# permission, any crash-disable marker, and a dev plugin .link. If you never used Voice,
# there may be nothing here to clean.
#
# It does NOT touch:
#   • the plugin or profile in Logi Options+ (remove those in the GUI — see above)
#   • the statusLine + hook lines in ~/.claude/settings.json (hand-edit)
#
# Usage:
#   bash scripts/uninstall.sh            # show targets, confirm, then remove
#   bash scripts/uninstall.sh --dry-run  # preview only, change nothing
#   bash scripts/uninstall.sh --yes      # skip the confirmation prompt
set -u

DRY=0; YES=0
for a in "$@"; do
  case "$a" in
    --dry-run) DRY=1 ;;
    --yes|-y)  YES=1 ;;
    -h|--help) grep '^#' "$0" | grep -v '^#!' | sed 's/^#\{1,\} \{0,1\}//'; exit 0 ;;
    *) echo "unknown option: $a (try --help)" >&2; exit 2 ;;
  esac
done

RUNTIME="$HOME/.claude/claude-console"
LOGI="$HOME/Library/Application Support/Logi/LogiPluginService"
LINK="$LOGI/Plugins/ClaudeConsolePlugin.link"
MARKER="$LOGI/Logs/plugin_crashes/ClaudeConsolePlugin.dll"
HELPER_ID="com.rshankar.claudeconsole.voicehelper"

sz() { du -sh "$1" 2>/dev/null | awk '{print $1}'; }

echo "Claude Console — leftover-data cleanup."
echo "This does NOT remove the plugin (uninstall that in Logi Options+). It clears the"
echo "app-level leftovers Logi can't see. These will be removed:"
echo
if [ -d "$RUNTIME" ]; then
  echo "  • voice runtime + model   $RUNTIME  ($(sz "$RUNTIME"), incl. your prompts.json)"
else
  echo "  • voice runtime           (not present)"
fi
echo "  • IPC temp files          /tmp/claude-console-*"
echo "  • Microphone permission   tccutil reset Microphone $HELPER_ID"
[ -f "$MARKER" ] && echo "  • crash-disable marker    $MARKER"
[ -f "$LINK" ]   && echo "  • dev plugin link         $LINK  (+ restart LogiPluginService)"
echo
echo "To remove the plugin itself: Logi Options+ → right-click Claude Console → Uninstall"
echo "(and delete the 'Claude Console — Keypad' profile there). NOT touched by this script:"
echo "  • the plugin + profile in Logi Options+  (or: logiplugintool uninstall ClaudeConsole)"
echo "  • the statusLine + hooks in ~/.claude/settings.json"
echo

if [ "$DRY" -eq 1 ]; then echo "(dry run — nothing removed)"; exit 0; fi

if [ "$YES" -ne 1 ]; then
  printf 'Proceed? [y/N] '
  read -r ans
  case "$ans" in y|Y|yes|YES) ;; *) echo "Aborted."; exit 1 ;; esac
fi

[ -d "$RUNTIME" ] && rm -rf "$RUNTIME" && echo "removed $RUNTIME"
rm -rf /tmp/claude-console-* 2>/dev/null && echo "cleared /tmp/claude-console-* IPC files"
tccutil reset Microphone "$HELPER_ID" >/dev/null 2>&1 && echo "reset Microphone permission for $HELPER_ID"
[ -f "$MARKER" ] && rm -f "$MARKER" && echo "removed crash-disable marker"
if [ -f "$LINK" ]; then
  rm -f "$LINK" && echo "removed dev plugin link"
  killall LogiPluginService 2>/dev/null && echo "restarted LogiPluginService"
fi

echo
echo "Leftovers cleared. If you haven't already, uninstall the plugin + profile in"
echo "Logi Options+ (right-click Claude Console → Uninstall) — that's the actual removal —"
echo "and delete the statusLine/hooks lines from ~/.claude/settings.json if you added them."
