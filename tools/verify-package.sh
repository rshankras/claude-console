#!/bin/bash
# verify-package.sh — refuse a .lplug4 that would fail on a user's machine.
#
# The last step of tools/voice/pack-release.sh, and runnable on its own against any package:
#
#   bash tools/verify-package.sh ClaudeConsole_2.2.2.lplug4
#
# Every check is a bug that shipped once, or a rule Marketplace QA enforces:
#   PluginApi.dll bundled                     Marketplace rejection (CLAUDE.md)
#   DLL version != package yaml != csproj     the crash-disable marker keys on the assembly version
#   a Windows helper that needs a runtime     #83 — focus/shot failed on every clean install
#   a whisper bundle with no compute backend  #24 — aborted on every user machine, worked here
#   no Windows whisper bundle                 #47
#   TRANSCRIPTION_SMOKE_OK shipped            #64 — QA read the asymmetry as "tested on one OS"
#   a build-machine path in the DLL or PDB    #26, #62
#   a link in a card that does not resolve    #68, #71
#   a voice helper that fails codesign        #66 — was a warning; macOS only
#
# Errors reject the package; warnings print and pass. CC_VERIFY_OFFLINE=1 skips the link check
# and says so. Needs python3, as pack-release.sh already does.
set -uo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"
ROOT="$(cd "$HERE/.." && pwd)"
PKG="${1:-}"

if [ -z "$PKG" ] || [ ! -f "$PKG" ]; then
  echo "usage: bash tools/verify-package.sh <package.lplug4>" >&2
  exit 2
fi
command -v python3 >/dev/null 2>&1 || { echo "error: python3 is required." >&2; exit 2; }

echo ">>> verifying $PKG"
python3 - "$PKG" "$ROOT" "$(basename "$HOME")" "$(basename "$ROOT")" "${CC_VERIFY_OFFLINE:-0}" <<'PY'
import os, re, sys, zipfile
import urllib.error, urllib.request

pkg, root, user, checkout, offline = sys.argv[1], sys.argv[2], sys.argv[3], sys.argv[4], sys.argv[5] == "1"
errors, warnings = [], []
def err(m): errors.append(m)
def warn(m): warnings.append(m)

z = zipfile.ZipFile(pkg)
names = {}   # path with forward slashes -> ZipInfo (a Windows-built package stores backslashes)
for info in z.infolist():
    n = info.filename.replace("\\", "/")
    if not n.endswith("/"):
        names[n] = info
def read(n): return z.read(names[n].filename)
def has(n): return n in names
def under(prefix, suffix=""): return [n for n in names if n.startswith(prefix) and n.endswith(suffix)]
def direct(prefix, suffix=""): return [n for n in under(prefix, suffix) if "/" not in n[len(prefix):]]

# --- layout, product, version ------------------------------------------------------------------
yaml = read("metadata/LoupedeckPackage.yaml").decode("utf-8", "replace") if has("metadata/LoupedeckPackage.yaml") else ""
if not yaml:
    err("no metadata/LoupedeckPackage.yaml in the package")
m = re.search(r"(?m)^version:\s*(\S+)", yaml)
yaml_version = m.group(1).strip().strip("\"'") if m else None
if yaml and not yaml_version:
    err("LoupedeckPackage.yaml carries no version")

plugin_dlls = [n for n in direct("bin/", "Plugin.dll") if "PluginApi" not in n]
product = None
if len(plugin_dlls) != 1:
    err(f"expected exactly one <Product>Plugin.dll directly under bin/, found {plugin_dlls or 'none'}")
else:
    product = os.path.basename(plugin_dlls[0])[:-len("Plugin.dll")]
print(f"    product       {product}")
print(f"    yaml version  {yaml_version}")

# --- never bundle PluginApi.dll ----------------------------------------------------------------
for n in names:
    if os.path.basename(n).lower() == "pluginapi.dll":
        err(f"{n} is bundled — the host provides PluginApi.dll; Marketplace QA rejected a submission over exactly this")

# --- version agreement: DLL FileVersion, package yaml, csproj, CHANGELOG ------------------------
def file_version(data):
    key = "FileVersion".encode("utf-16-le")
    i = data.find(key)
    if i < 0:
        return None
    i += len(key)
    while i + 1 < len(data) and data[i:i + 2] == b"\x00\x00":
        i += 2
    out = []
    while i + 1 < len(data) and data[i:i + 2] != b"\x00\x00":
        out.append(data[i:i + 2].decode("utf-16-le", "replace"))
        i += 2
    return "".join(out).strip()

if product:
    fv = file_version(read(plugin_dlls[0]))
    print(f"    dll version   {fv}")
    if not fv:
        warn(f"could not read FileVersion from {plugin_dlls[0]}")
    elif yaml_version and fv != yaml_version and not fv.startswith(yaml_version + "."):
        err(f"version mismatch: DLL FileVersion {fv}, LoupedeckPackage.yaml {yaml_version}")
    csproj = os.path.join(root, "src", "Products", product, product + "Plugin.csproj")
    if os.path.exists(csproj):
        cm = re.search(r"<Version>\s*([^<\s]+)\s*</Version>", open(csproj, encoding="utf-8").read())
        if cm and yaml_version and cm.group(1) != yaml_version:
            err(f"version mismatch: {os.path.relpath(csproj, root)} says {cm.group(1)}, the package yaml says {yaml_version}")
    changelog = os.path.join(root, "CHANGELOG.md")
    if yaml_version and os.path.exists(changelog):
        if not re.search(r"(?m)^## \[" + re.escape(yaml_version) + r"\]", open(changelog, encoding="utf-8").read()):
            warn(f"CHANGELOG.md has no '## [{yaml_version}]' section — is [Unreleased] still waiting to be renamed?")

# --- Windows helpers: present, self-contained, no sidecars (#83) --------------------------------
# The package yaml decides whether Windows is a target. A pluginFolderWin declaration means the
# service will load this package on Windows, so every helper must be there and self-contained. A
# package that declares no Windows folder must ship no helper at all: an exe that is never
# launched is dead weight, and one that would fail on a clean install (#83) is a trap for whoever
# later adds the declaration without rebuilding. Vizhi Desktop is macOS-only until its UI
# Automation helper bundles its runtime.
ships_windows = bool(re.search(r"(?m)^pluginFolderWin:\s*\S", yaml))
helpers = direct("bin/claude-console-", ".exe")
print(f"    windows       {'declared (pluginFolderWin)' if ships_windows else 'not declared: a macOS-only package'}")
if not ships_windows:
    for n in helpers:
        err(f"{n} shipped but the yaml declares no pluginFolderWin — drop the inert helper or declare Windows")
else:
    if not has("bin/claude-console-hook.exe"):
        err("bin/claude-console-hook.exe missing — live status cannot work on Windows")
    toolkit = "bin/claude-console-tools.exe"
    standalone = ["bin/claude-console-" + t + ".exe" for t in ("inject", "voice", "focus", "shot")]
    if has(toolkit):
        contract = "claude-console-tools/v1 inject focus voice shot"
        payload = read(toolkit)
        if contract.encode("utf-16-le") not in payload:
            err("the toolkit has no supported command contract — rebuild the shared helper")
        for n in standalone:
            if has(n):
                err(f"{n} duplicates the shared toolkit's runtime — remove stale staged helpers")
    else:
        # Older release packages remain verifiable; all four functions must be represented.
        for n in standalone:
            if not has(n):
                err(f"{n} missing and no shared toolkit is present")
    for n in helpers:
        size = names[n].file_size
        if size < 1_000_000:
            err(f"{n} is {size:,} bytes — a framework-dependent stub that needs a runtime clean machines lack (#83)")
        stem = n[:-4]
        for sidecar in (stem + ".dll", stem + ".runtimeconfig.json", stem + ".deps.json"):
            if has(sidecar):
                err(f"{sidecar} shipped beside {n} — the helper is not self-contained")
print(f"    helpers       {len(helpers)}: " + ", ".join(os.path.basename(h) for h in helpers))

# --- voice payload (#24, #47, #64) -------------------------------------------------------------
if not under("bin/voice/"):
    err("no bin/voice/ payload — every product ships offline voice")
else:
    win = "bin/voice/whisper-bin-win/"
    if not ships_windows:
        if under(win):
            err("bin/voice/whisper-bin-win/ shipped but the yaml declares no pluginFolderWin — inert payload")
    else:
        if not has(win + "whisper-cli.exe"):
            err("no Windows whisper-cli.exe in the package (#47)")
        if not any(n.startswith(win + "ggml-cpu") for n in names):
            err("the Windows whisper bundle ships no ggml-cpu backend (#24)")
    mac = "bin/voice/whisper-bin/"
    if not has(mac + "whisper-cli"):
        err("no macOS whisper-cli in the package")
    if not any(n.startswith(mac + "libggml-cpu") or (n.startswith(mac) and n.endswith(".so")) for n in names):
        err("the macOS whisper bundle ships no ggml compute backend (#24)")
    if not any(n.startswith("bin/voice/ClaudeVoiceHelper.app/Contents/MacOS/") for n in names):
        err("no ClaudeVoiceHelper.app in the voice payload")
    for n in names:
        if os.path.basename(n) == "TRANSCRIPTION_SMOKE_OK":
            err(f"{n} shipped — the marker is proof for pack-release, not payload (#64)")

# --- build-machine paths (#26, #62) ------------------------------------------------------------
path_pat = re.compile(rb"(?:/Users/|[A-Za-z]:\\Users\\)[^\x00]{0,160}")
for n in under("bin/", ".dll"):
    for hit in path_pat.findall(read(n)):
        err(f"{n} embeds a build-machine path: {hit.decode(errors='replace')}")
for n in under("bin/", ".pdb"):
    data = read(n)
    for needle in (user.encode(), checkout.encode(), b"raw.githubusercontent.com"):
        if needle and needle in data:
            err(f"{n} contains '{needle.decode(errors='replace')}' (#62)")

# --- links a user can click (#68, #71) ----------------------------------------------------------
urls = set()
if product:
    dll = read(plugin_dlls[0])
    for text in (dll.decode("utf-16-le", "ignore"), dll.decode("utf-8", "ignore")):
        for u in re.findall(r"https?://[A-Za-z0-9./#?=_%:-]+", text):
            urls.add(u.split("#", 1)[0].rstrip(".,)"))
if offline:
    print(f"    links         {len(urls)} found, NOT checked (CC_VERIFY_OFFLINE=1)")
else:
    def fetch(u, method):
        req = urllib.request.Request(u, method=method, headers={"User-Agent": "claude-console-verify-package"})
        with urllib.request.urlopen(req, timeout=15) as r:
            return r.status
    dead = []
    for u in sorted(urls):
        try:
            code = fetch(u, "HEAD")
        except urllib.error.HTTPError as e:
            code = e.code
            if code in (403, 405):   # HEAD refused — judge on GET
                try:
                    code = fetch(u, "GET")
                except urllib.error.HTTPError as e2:
                    code = e2.code
                except Exception as e2:
                    code = str(e2)
        except Exception as e:
            code = str(e)
        if code != 200:
            dead.append(f"{u} -> {code}")
    for d in dead:
        err("dead link in the package: " + d)
    print(f"    links         {len(urls)} checked, {len(dead)} dead")

for w in warnings:
    print("    warn  " + w)
for e in errors:
    print("    FAIL  " + e)
sys.exit(1 if errors else 0)
PY
STATUS=$?

# --- signatures (#66): macOS only, and only on what a user's Options+ will actually extract ------
# ditto keeps the symlinks and resource forks a .app needs to verify; a plain unzip would not, and
# a helper that fails here fails Gatekeeper on the user's machine the same way.
if [ "$(uname)" = "Darwin" ] && command -v codesign >/dev/null 2>&1; then
  TMP="$(mktemp -d)"
  trap 'rm -rf "$TMP"' EXIT
  if ditto -x -k "$PKG" "$TMP" 2>/dev/null; then
    for target in "$TMP/bin/voice/ClaudeVoiceHelper.app" "$TMP/bin/voice/whisper-bin/whisper-cli"; do
      [ -e "$target" ] || continue
      if codesign --verify --deep --strict "$target" >/dev/null 2>&1; then
        echo "    signed        $(basename "$target")"
      else
        echo "    FAIL  $(basename "$target") fails codesign --verify --deep --strict (#66)"
        STATUS=1
      fi
    done
  else
    echo "    warn  ditto could not extract the package — signature check skipped"
  fi
fi

if [ "$STATUS" -eq 0 ]; then
  echo "✅ package verified"
else
  echo "❌ package REJECTED" >&2
fi
exit "$STATUS"
