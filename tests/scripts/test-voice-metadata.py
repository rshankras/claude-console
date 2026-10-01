#!/usr/bin/env python3
"""Exercise the same metadata gate used on the final release archive."""
from pathlib import Path
import plistlib
import runpy
import subprocess
import tempfile

root = Path(__file__).resolve().parents[2]
module = runpy.run_path(str(root / "tools/voice/helper-metadata.py"))
verify = module["verify"]
with tempfile.TemporaryDirectory() as temporary:
    for product in module["PRODUCTS"]:
        path = Path(temporary) / (product + ".plist")
        path.write_bytes((root / "tools/voice/Info.plist").read_bytes())
        assert verify(plistlib.loads(path.read_bytes()), product, "3.2.1")
        subprocess.run(["python3", str(root / "tools/voice/helper-metadata.py"), str(path), product, "3.2.1"], check=True)
        actual = plistlib.loads(path.read_bytes())
        assert verify(actual, product, "3.2.1") == []
        for key in ["CFBundleIdentifier", "NSMicrophoneUsageDescription", "CFBundleVersion"]:
            broken = dict(actual, **{key: "stale"})
            assert key in verify(broken, product, "3.2.1")
print("Voice metadata: all products match; stale identity, prompt and version are rejected")
