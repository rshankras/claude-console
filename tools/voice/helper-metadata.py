#!/usr/bin/env python3
"""Build or verify product-specific voice helper metadata."""
import plistlib
import sys
from pathlib import Path

PRODUCTS = {
    "ClaudeConsole": ("Claude Console", "com.rshankar.claudeconsole.voicehelper"),
    "VizhiCodex": ("Vizhi for Codex", "com.rshankar.vizhicodex.voicehelper"),
    "VizhiDesktop": ("Vizhi Desktop", "com.rshankar.vizhidesktop.voicehelper"),
}

def metadata(product, version):
    name, identifier = PRODUCTS[product]
    return {
        "CFBundleIdentifier": identifier,
        "CFBundleName": name + " Voice",
        "CFBundleDisplayName": name + " Voice",
        "CFBundleVersion": version,
        "CFBundleShortVersionString": version,
        "NSMicrophoneUsageDescription": name + " transcribes your speech locally so you can dictate from the keypad.",
    }

def verify(data, product, version):
    return [key for key, expected in metadata(product, version).items() if data.get(key) != expected]

if __name__ == "__main__":
    path, product, version = sys.argv[1:4]
    data = plistlib.loads(Path(path).read_bytes())
    data.update(metadata(product, version))
    Path(path).write_bytes(plistlib.dumps(data))
