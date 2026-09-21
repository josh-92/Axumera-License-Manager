# Axumera License Manager

Offline license generator for the **Axumera Exam Suite** activation system.
A modern WinForms + WebView2 desktop app that signs RSA licenses for exam
terminals — a drop-in replacement for the legacy `keygen.php` web form, with
byte-exact output compatibility.

> The live signing private key is **never** bundled, copied, embedded, or
> uploaded. The app stores only the *path* to the operator's key and signs
> against that file on disk.

---

## Features

- **Sign licenses offline** — no server, no internet, no PHP runtime.
  Byte-compatible with the original PHP contract (exact payload field order,
  RSA-SHA256 signature, base64 envelope).
- **Verified security model** — signing, verification, key handling, and
  persistence all live in a fail-closed C# `WebBridge`; the SPA never holds a
  credential or touches a key. The shell refuses every action unless an
  authenticated session is in place.
- **Operator account** — first-run setup, PBKDF2-hashed credentials, change
  password, logout. The dashboard is unreachable without a verified session.
- **Key management** — browse to `private_key.pem`, confirm its fingerprint and
  ACL state, and one-click **Restrict Access** to harden the key's file ACL
  (current user / Administrators / SYSTEM only).
- **License records** — every signed license is persisted locally
  (`%LOCALAPPDATA%\Axumera\LicenseManager`), with list / archive / restore /
  delete and a searchable archive view.
- **Verification** — open any `license.lic`, verify its signature with the
  bundled production public key, and optionally check it against an expected
  machine HWID.
- **Contract-accurate rules** — HWID normalized exactly like the PHP generator
  (uppercase, non-alphanumerics stripped); expiry validated as `YYYY-MM-DD`
  against the local date; school name limited to 150 characters.
- **Modern UI** — a responsive WebView2 SPA (`app.axumera`) hosted inside a
  native WinForms shell, with configurable theme and per-page layout.

---

## Requirements

- Windows 10/11 (x64)
- [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0)
- [Microsoft Edge WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/)
  (Evergreen)
- An existing **2048-bit RSA private key** (`private_key.pem`, PKCS#8,
  unlocked) — the same key the Axumera Exam Suite consumer is configured to
  trust. The app does not generate or regenerate a key.

## Getting started

1. Launch `Axumera.LicenseManager.exe`.
2. On first run, create the local operator account.
3. Open **Settings**, choose the path to `private_key.pem`, and (recommended)
   click **Restrict Access** to lock the key down to trusted principals.
4. On the **Generate** page, enter the school name, machine HWID, and expiry
   date; review and **Sign** to save `license.lic`.
5. Deliver `license.lic` to the exam site. Use **Verify** to double-check any
   license file — including one produced by the legacy `keygen.php`.

### License file format

```
{"payload":"<base64>","signature":"<base64>"}
```

The `payload` is the base64 of the exact JSON string
`{"school_name":"…","hwid":"…","expires":"YYYY-MM-DD"}` (keys in that order);
the `signature` is base64 of RSA-SHA256 over that exact string. Consumers must
verify the signature over the decoded payload as-is, then re-parse the fields.

---

## Building and testing from source

The build uses the .NET 10 SDK. If `dotnet` is not on `PATH` (a known Windows
apps-alias trap), the scripts locate the SDK under
`%LOCALAPPDATA%\Microsoft\dotnet\dotnet.exe`.

```powershell
# Build + publish the app
.\scripts\build-and-publish.ps1
# Output: build\Axumera.LicenseManager\Axumera.LicenseManager.exe (+ SHA-256)

# Run the test suite (117 tests)
& "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe" test -c Release
```

### Repository layout

```
Axumera.LicenseManager.slnx
Directory.Build.props
scripts/
  build-and-publish.ps1        # build + publish pipeline
src/
  Axumera.LicenseManager/      # WinForms shell, WebView2 SPA, WebBridge
  Axumera.LicenseManager.Core/ # signing, key handling, rules, persistence
tests/
  Axumera.LicenseManager.Core.Tests/
```

---

## Security notes

- The private key is never copied into build output, installation payload, or
  source control; only its on-disk **path** is remembered in
  `settings.json`.
- Signing is blocked unless the key's ACL restricts write access to trusted
  principals (current user / Administrators / SYSTEM).
- `accounts.json` passwords are PBKDF2-hashed (never plaintext); the account
  system is enforced entirely inside the C# gate — there is no JavaScript-side
  authentication.
- The bundled public key is the production Axumera Exam Suite key and is used
  only for verification; it is public by design.



---

Copyright © 2026 Axumera Technologies. All rights reserved.
