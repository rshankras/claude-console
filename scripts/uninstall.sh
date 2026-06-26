#!/usr/bin/env bash
# Claude Console — uninstall / clean-reinstall helper.
#
# Removes the APP-LEVEL footprint: the voice runtime + speech model, the /tmp IPC files,
# the Microphone permission, any crash-disable marker, and a dev plugin .link.
#
# It does NOT touch (do these yourself — see README "Uninstall / clean reinstall"):
#   • the plugin in Logi Options+ (GUI) — or run: logiplugintool uninstall ClaudeConsole
#   • the imported "Claude Console — Keypad" profile (GUI)
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
    -h|--help) grep '^#' "$0" | sed 's/^#\{1,\} \{0,1\}//'; exit 0 ;;
    *) echo "unknown option: $a (try --help)" >&2; exit 2 ;;
  esac
done

RUNTIME="$HOME/.claude/claude-console"
LOGI="$HOME/Library/Application Support/Logi/LogiPluginService"
LINK="$LOGI/Plugins/ClaudeConsolePlugin.link"
MARKER="$LOGI/Logs/plugin_crashes/ClaudeConsolePlugin.dll"
HELPER_ID="com.rshankar.claudeconsole.voicehelper"

sz() { du -sh "$1" 2>/dev/null | awk '{print $1}'; }

echo "Claude Console uninstall — these will be removed:"
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
echo "NOT touched (manual — see README 'Uninstall / clean reinstall'):"
echo "  • the plugin in Logi Options+  (or: logiplugintool uninstall ClaudeConsole)"
echo "  • the imported 'Claude Console — Keypad' profile"
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
echo "Done. Still manual: remove the plugin + profile in Logi Options+, and delete the"
echo "statusLine/hooks lines from ~/.claude/settings.json if you added them."
