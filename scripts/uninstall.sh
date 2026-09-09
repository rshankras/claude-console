#!/usr/bin/env bash
# Claude Console — leftover-data cleanup, and the no-keypad way to turn live status off.
# (This is NOT the plugin uninstaller.)
#
# To uninstall the PLUGIN, do it in Logi Options+: right-click the Claude Console
# plugin -> Uninstall (or run: logiplugintool uninstall ClaudeConsole), and delete the
# imported "Claude Console — Keypad" profile there too. That is the actual uninstall.
#
# This script clears the app-level leftovers that Logi Options+ can't see and never removes:
#   • our statusLine + hooks in ~/.claude/settings.json (present only if you pressed Enable Live
#     Status) — removed SURGICALLY (#31): only the entries that name claude-console go, your own
#     hooks stay, and a status line we chained is put back to what it was. A rolling backup is
#     written first (settings.json.claude-console.bak).
#   • the voice runtime + ~142 MB speech model, the /tmp IPC files, the Microphone permission,
#     any crash-disable marker, and a dev plugin .link.
#
# Installed to ~/.claude/claude-console/scripts/ by the plugin on every load, so a Marketplace
# user can run it without the repo:
#
#   bash ~/.claude/claude-console/scripts/uninstall.sh
#
# It does NOT touch the plugin or profile in Logi Options+ (remove those in the GUI — see above).
#
# Usage:
#   bash scripts/uninstall.sh            # show targets, confirm, then remove everything
#   bash scripts/uninstall.sh --dry-run  # preview only, change nothing
#   bash scripts/uninstall.sh --yes      # skip the confirmation prompt
#   bash scripts/uninstall.sh --unwire   # the same as a long press on a live key: take the wiring
#                                        # out of settings.json and leave the Off marker, keeping the
#                                        # plugin and voice — the live keys read Off; nothing else changes
set -u

DRY=0; YES=0; UNWIRE_ONLY=0
for a in "$@"; do
  case "$a" in
    --dry-run) DRY=1 ;;
    --yes|-y)  YES=1 ;;
    --unwire)  UNWIRE_ONLY=1 ;;
    -h|--help) grep '^#' "$0" | grep -v '^#!' | sed 's/^#\{1,\} \{0,1\}//'; exit 0 ;;
    *) echo "unknown option: $a (try --help)" >&2; exit 2 ;;
  esac
done

RUNTIME="$HOME/.claude/claude-console"
SETTINGS="$HOME/.claude/settings.json"
BACKUP="$HOME/.claude/settings.json.claude-console.bak"
CHAIN="$RUNTIME/statusline-chain"
OPT_OUT="$RUNTIME/no-autowire"
LOGI="$HOME/Library/Application Support/Logi/LogiPluginService"
LINK="$LOGI/Plugins/ClaudeConsolePlugin.link"
MARKER="$LOGI/Logs/plugin_crashes/ClaudeConsolePlugin.dll"
HELPER_ID="com.rshankar.claudeconsole.voicehelper"

sz() { du -sh "$1" 2>/dev/null | awk '{print $1}'; }

# Remove our wiring from settings.json. Same recognition rule as the plugin (BridgeWiring.IsOurs /
# IsOurHook): a command naming statusline-handler.sh, activity-hook.sh or claude-console-hook is
# ours; anything else is the user's and is not touched. Prints what it did (or would do, with
# "report"). Exit 0 always — a missing or unparseable settings.json is "nothing to do", not an error.
unwire_settings() { # mode: report | apply
  python3 - "$1" "$SETTINGS" "$BACKUP" "$CHAIN" <<'PY'
import json, os, shutil, sys
mode, settings, backup, chain = sys.argv[1:5]
MARKERS = ("statusline-handler.sh", "activity-hook.sh", "claude-console-hook")
ours = lambda cmd: isinstance(cmd, str) and any(m in cmd.lower() for m in MARKERS)
if not os.path.exists(settings):
    print("  settings.json: not present — nothing wired"); sys.exit(0)
try:
    root = json.load(open(settings))
except Exception as e:
    print(f"  settings.json: not valid JSON ({e}) — left untouched"); sys.exit(0)
if not isinstance(root, dict):
    print("  settings.json: not a JSON object — left untouched"); sys.exit(0)

removed = 0
hooks = root.get("hooks")
if isinstance(hooks, dict):
    for event in list(hooks):
        entries = hooks[event]
        if not isinstance(entries, list): continue
        emptied = False
        for entry in list(entries):
            inner = entry.get("hooks") if isinstance(entry, dict) else None
            if not isinstance(inner, list): continue
            before = len(inner)
            inner[:] = [h for h in inner if not (isinstance(h, dict) and ours(h.get("command")))]
            removed += before - len(inner)
            if before != len(inner) and not inner:
                entries.remove(entry); emptied = True
        if emptied and not entries:
            del hooks[event]
    if removed and not hooks:
        del root["hooks"]

restored = None
sl = root.get("statusLine")
if isinstance(sl, dict) and ours(sl.get("command")):
    chained = open(chain).read().strip() if os.path.exists(chain) else ""
    if chained:
        sl["command"] = chained; sl["type"] = "command"; restored = chained
    else:
        del root["statusLine"]; restored = ""

if not removed and restored is None:
    print("  settings.json: carries none of our wiring"); sys.exit(0)

what = f"{removed} claude-console hook(s)" + ("" if restored is None else
       (f", statusLine restored to: {restored}" if restored else ", statusLine removed (nothing was chained)"))
if mode == "report":
    print(f"  settings.json: would remove {what}"); sys.exit(0)

shutil.copy2(settings, backup)
tmp = settings + ".cc.tmp"
with open(tmp, "w") as f:
    json.dump(root, f, indent=2, ensure_ascii=False); f.write("\n")
os.replace(tmp, settings)
print(f"  settings.json: removed {what}  (backup: {backup})")
PY
}

# --unwire: the wiring only — what a long press on a live key does. Leaves the Off marker, which
# is how the live keys know to read Off (and, for anyone still on a 2.2.0 pre-release that wired
# on load, what stops the next load wiring it back).
if [ "$UNWIRE_ONLY" -eq 1 ]; then
  echo "Claude Console — removing the live-status wiring from $SETTINGS"
  unwire_settings apply
  rm -f "$CHAIN"
  mkdir -p "$RUNTIME" && : > "$OPT_OUT"
  echo "  Off marker set: $OPT_OUT  (press a live key to turn it back on)"
  echo "Takes effect on your next Claude Code session. The plugin and voice are untouched;"
  echo "the Cost / Context / Activity keys read Off."
  exit 0
fi

echo "Claude Console — leftover-data cleanup."
echo "This does NOT remove the plugin (uninstall that in Logi Options+). It clears the"
echo "app-level leftovers Logi can't see. These will be removed:"
echo
echo "  • live-status wiring     our statusLine + hooks in $SETTINGS (yours stay)"
unwire_settings report
if [ -d "$RUNTIME" ]; then
  echo "  • voice runtime + model   $RUNTIME  ($(sz "$RUNTIME"), incl. your prompts.json)"
else
  echo "  • voice runtime           (not present)"
fi
echo "  • IPC temp files          /tmp/claude-console/ (and legacy /tmp/claude-console-*)"
echo "  • Microphone permission   tccutil reset Microphone $HELPER_ID"
[ -f "$MARKER" ] && echo "  • crash-disable marker    $MARKER"
[ -f "$LINK" ]   && echo "  • dev plugin link         $LINK  (+ restart LogiPluginService)"
echo
echo "To remove the plugin itself: Logi Options+ → right-click Claude Console → Uninstall"
echo "(and delete the 'Claude Console — Keypad' profile there). NOT touched by this script:"
echo "  • the plugin + profile in Logi Options+  (or: logiplugintool uninstall ClaudeConsole)"
echo

if [ "$DRY" -eq 1 ]; then echo "(dry run — nothing removed)"; exit 0; fi

if [ "$YES" -ne 1 ]; then
  printf 'Proceed? [y/N] '
  read -r ans
  case "$ans" in y|Y|yes|YES) ;; *) echo "Aborted."; exit 1 ;; esac
fi

# Unwire BEFORE the runtime home goes: the chain file that holds the user's original status line
# lives inside it, and the scripts the hooks point at are about to be deleted.
unwire_settings apply
[ -d "$RUNTIME" ] && rm -rf "$RUNTIME" && echo "removed $RUNTIME"
rm -rf /tmp/claude-console /tmp/claude-console-* 2>/dev/null && echo "cleared /tmp/claude-console IPC files"
tccutil reset Microphone "$HELPER_ID" >/dev/null 2>&1 && echo "reset Microphone permission for $HELPER_ID"
[ -f "$MARKER" ] && rm -f "$MARKER" && echo "removed crash-disable marker"
if [ -f "$LINK" ]; then
  rm -f "$LINK" && echo "removed dev plugin link"
  killall LogiPluginService 2>/dev/null && echo "restarted LogiPluginService"
fi

echo
echo "Leftovers cleared. If you haven't already, uninstall the plugin + profile in"
echo "Logi Options+ (right-click Claude Console → Uninstall) — that's the actual removal."
