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
# "report"). Failed writes/parses retain recovery data and return nonzero for a later retry.
unwire_settings() { # mode: report | apply
  python3 - "$1" "$SETTINGS" "$BACKUP" "$CHAIN" <<'PY'
import fcntl, json, os, shutil, sys, tempfile
mode, settings, backup, chain = sys.argv[1:5]
runtime = os.path.dirname(chain)
# All cleanup entry points share this lock. Keep it outside the runtime tree: full cleanup
# removes that tree, and deleting a lock file while another process holds it defeats flock.
# Nonblocking acquisition keeps status-line hooks responsive; the next hook retries on failure.
if mode == "apply":
    os.makedirs(os.path.dirname(settings), exist_ok=True)
    lock = open(os.path.join(os.path.dirname(settings), ".claude-console-unwire.lock"), "a")
    try:
        fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
    except BlockingIOError:
        print("  cleanup already running; retry later", file=sys.stderr); sys.exit(75)

def finish():
    # Still under the lock, and only after the settings transaction succeeded.
    if mode == "apply":
        os.makedirs(runtime, exist_ok=True)
        with open(os.path.join(runtime, "no-autowire"), "a"):
            pass
        if os.path.exists(chain):
            os.unlink(chain)

def json_source(text):
    # Match the plugin reader: comments and trailing commas are accepted. Replace them with
    # whitespace for parsing while keeping every offset aligned with the original source.
    chars = list(text)
    i = 0
    while i < len(text):
        if text[i] == '"':
            i += 1
            while i < len(text):
                if text[i] == '\\': i += 2; continue
                if text[i] == '"': i += 1; break
                i += 1
            continue
        if text.startswith('//', i) or text.startswith('/*', i):
            if text.startswith('//', i):
                end = text.find('\n', i + 2)
                if end < 0: end = len(text)
            else:
                end = text.find('*/', i + 2)
                if end < 0: raise ValueError('unterminated settings comment')
                end += 2
            for j in range(i, end):
                if chars[j] not in '\r\n': chars[j] = ' '
            i = end
        else: i += 1
    clean = ''.join(chars)
    i = 0
    while i < len(clean):
        if clean[i] == '"':
            i += 1
            while i < len(clean):
                if clean[i] == '\\': i += 2; continue
                if clean[i] == '"': i += 1; break
                i += 1
            continue
        if clean[i] == ',':
            j = i + 1
            while j < len(clean) and clean[j].isspace(): j += 1
            if j < len(clean) and clean[j] in '}]': chars[i] = ' '
        i += 1
    return ''.join(chars)

MARKERS = ("statusline-handler.sh", "activity-hook.sh", "claude-console-hook")
ours = lambda cmd: isinstance(cmd, str) and any(m in cmd.lower() for m in MARKERS)
if not os.path.exists(settings):
    print("  settings.json: not present — nothing wired"); finish(); sys.exit(0)
try:
    with open(settings, "rb") as source:
        original = source.read()
    root = json.loads(json_source(original.decode("utf-8-sig")))
except Exception as e:
    print(f"  settings.json: not valid JSON ({e}) — left untouched"); sys.exit(1)
if not isinstance(root, dict):
    print("  settings.json: not a JSON object — left untouched"); sys.exit(1)

# Retain source slices for untouched values, including foreign hooks nested in our arrays.
# The cleanup is intentionally self-contained: it must survive deletion of the plugin.
text = original.decode("utf-8-sig")
clean_text = json_source(text)
decoder = json.JSONDecoder()
def whitespace(pos):
    while pos < len(text) and clean_text[pos].isspace(): pos += 1
    return pos

def parse(pos):
    start = whitespace(pos)
    value, end = decoder.raw_decode(clean_text, start)
    children = []
    if isinstance(value, (dict, list)):
        pos = whitespace(start + 1)
        while pos < end - 1:
            member = pos
            name = None
            if isinstance(value, dict):
                name, pos = decoder.raw_decode(clean_text, pos)
                pos = whitespace(pos)
                pos = whitespace(pos + 1)  # colon
            child = parse(pos)
            child['member'], child['name'] = member, name
            children.append(child)
            pos = whitespace(child['end'])
            if clean_text[pos] == ',': pos = whitespace(pos + 1)
            else: break
    return dict(start=start, end=end, member=start, name=None, value=value, children=children)

newline = "\r\n" if "\r\n" in text else "\n"
indent = "  "
for line in text.splitlines():
    if line.strip() and line[:1].isspace():
        indent = line[:len(line) - len(line.lstrip())]
        break

def fresh(value, depth):
    return json.dumps(value, ensure_ascii=False, indent=indent).replace("\n", newline + indent * depth)

def remove_comma(trivia):
    clean = json_source(trivia)
    at = clean.find(',')
    return trivia[:at] + trivia[at+1:] if at >= 0 else trivia

def render(node, value, depth=0):
    old = node['value']
    if old == value: return text[node['start']:node['end']]
    if not ((isinstance(old, dict) and isinstance(value, dict)) or
            (isinstance(old, list) and isinstance(value, list))):
        return fresh(value, depth)
    children = node['children']
    if isinstance(value, dict):
        by_name = {c['name']: c for c in children}
        items = [(by_name.get(k), k, v) for k, v in value.items()]
    else:
        used = set()
        items = []
        for item in value:
            child = next((c for c in children if id(c) not in used and c.get('identity') is item), None)
            if child is None:
                child = next((c for c in children if id(c) not in used and c['value'] == item), None)
            if child is not None: used.add(id(child))
            items.append((child, None, item))
    pieces = []
    for child, name, v in items:
        if child is not None:
            index = children.index(child)
            previous = children[index-1]['end'] if index else node['start'] + 1
            leading = text[previous:child['member']]
            if index: leading = remove_comma(leading)
            pieces.append(leading + text[child['member']:child['start']] + render(child, v, depth+1))
        else:
            leading = newline + indent * (depth+1) if '\n' in text[node['start']:node['end']] or not children else ' '
            header = json.dumps(name, ensure_ascii=False) + ': ' if name is not None else ''
            pieces.append(leading + header + fresh(v, depth+1))
    previous = children[-1]['end'] if children else node['start'] + 1
    suffix = text[previous:node['end']-1]
    if not items: suffix = remove_comma(suffix)
    if not children and items and not suffix.strip(): suffix = newline + indent * depth
    return text[node['start']] + ','.join(pieces) + suffix + text[node['end']-1]

def bind(node, value):
    node['identity'] = value
    for index, child in enumerate(node['children']):
        bind(child, value[child['name']] if isinstance(value, dict) else value[index])

syntax = parse(0)
bind(syntax, root)

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
    print("  settings.json: carries none of our wiring"); finish(); sys.exit(0)

what = f"{removed} claude-console hook(s)" + ("" if restored is None else
       (f", statusLine restored to: {restored}" if restored else ", statusLine removed (nothing was chained)"))
if mode == "report":
    print(f"  settings.json: would remove {what}"); sys.exit(0)

rewritten = text[:syntax['start']] + render(syntax, root) + text[syntax['end']:]
# Validate the result before creating the temporary file or replacing user settings.
if json.loads(json_source(rewritten)) != root:
    raise RuntimeError("cleanup's source-preserving edit did not match the intended settings")
encoded = rewritten.encode('utf-8')
if original.startswith(b'\xef\xbb\xbf'): encoded = b'\xef\xbb\xbf' + encoded

fd, tmp = tempfile.mkstemp(prefix="settings.json.cc.", suffix=".tmp", dir=os.path.dirname(settings))
try:
    with os.fdopen(fd, "wb") as f:
        f.write(encoded)
    with open(settings, "rb") as source:
        if source.read() != original:
            raise RuntimeError("settings.json changed during cleanup; retry later")
    shutil.copy2(settings, backup)
    os.replace(tmp, settings)
finally:
    if os.path.exists(tmp):
        os.unlink(tmp)
finish()
print(f"  settings.json: removed {what}  (backup: {backup})")
PY
}

# --unwire: the wiring only — what a long press on a live key does. Leaves the Off marker, which
# is how the live keys know to read Off (and, for anyone still on a 2.2.0 pre-release that wired
# on load, what stops the next load wiring it back).
if [ "$UNWIRE_ONLY" -eq 1 ]; then
  echo "Claude Console — removing the live-status wiring from $SETTINGS"
  unwire_settings apply || exit $?
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
unwire_settings apply || exit $?
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
