# Windows code signing

The Windows helpers, the plugin DLL and the whisper bundle are Authenticode-signed at pack time
by `tools/windows/sign-windows-payload.sh`, and `tools/verify-package.sh` refuses a package that
carries an unsigned or untimestamped Windows file. The reasoning is on #110: Logitech QA's
CrowdStrike quarantined the unsigned hook exe, and a signed file lets their IT allow-list the
publisher once instead of a hash per release.

## What is in this folder

`certum-code-signing-2021-ca.pem` — the issuing CA, *Certum Code Signing 2021 CA* (issued by
*Certum Trusted Network CA 2*, a root in the Mozilla and Microsoft programs). Public; it is
embedded in every signature so that a machine that cannot reach Certum's repository can still
build the chain. Fetched from the leaf certificate's own "CA Issuers" URL,
http://repository.certum.pl/ccsca2021.cer, SHA-256 fingerprint
`5A:C8:2C:BE:9F:28:35:1E:85:D5:26:22:93:BC:FC:8B:AC:AB:EA:D1:29:4F:24:8C:1D:F1:7F:81:CE:5A:C3:CE`,
valid to 2036-05-18. If Certum ever issues our renewal from a different CA, the sign script
says so (issuer ≠ this file) and this file gets replaced.

`expected-publisher.txt` — the Subject CN every Windows file must be signed by (`CN=Ravi Shankar S`).
The sign script refuses to sign with any other certificate on the token, and the package gate
refuses a file signed by anyone else — however trusted — or by a certificate from a different CA,
or by two different certificates across one package. Update this line only at a renewal that
changes the name (it should not: Logitech's allow-list keys on it).

Nothing secret lives here. The private key is in Certum's SimplySign cloud and never leaves it.
`.gitignore` ignores `*.pem` and carves out exactly the CA file above; a key file could not be
committed here by accident.

## Where the certificate lives

Certum *Standard Code Signing in the cloud*, an OV certificate issued to the owner as an
individual (subject `CN=Ravi Shankar S`). SimplySign Desktop on the Mac presents it as a PKCS#11
token (`/usr/local/lib/libSimplySignPKCS.dylib`, slot 1, token "Code Signing", no PIN); jsign
signs through that, osslsigncode verifies against a public root bundle.

## Per release

1. Log in to SimplySign Desktop (menu bar) **right before packing**: your e-mail plus the rotating
   code from the SimplySign phone app. The cloud session expires after an hour or two and the
   token stays listed after it does, so the sign script proves the key answers with a probe
   signature first; if that fails it says "log out, then log in again" and stops before touching
   any file.
2. `bash tools/voice/pack-release.sh <ver> <Product>` signs every `.exe` and `.dll` in the staged
   tree, verifies each one, packs, and verifies the packed files again.

`WINDOWS_SIGNING=skip` leaves the files unsigned for a local dev pack; the package is then named
`*-unsigned.lplug4` and must not be uploaded.

## Renewal

The certificate is valid for one year from issue (2026-09-27). Renew a month early at
shop.certum.eu and reissue into the same SimplySign account. Keep the subject name identical:
Logitech's allow-list keys on it. The token label changes with the new serial; the sign script
discovers it, so nothing in the repo needs editing unless the issuing CA changes.
