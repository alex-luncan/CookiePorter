# 🍪 CookiePorter

**CookiePorter** is a small Windows command-line tool written in **C# (.NET 8)** that can **read and export cookies from Microsoft Edge**.

Right now the project is focused on:

- Safely reading Edge’s cookie database
- Decrypting cookie values using the same mechanisms as the browser
- Exporting cookies to JSON with useful filters
- Providing `import` and `transfer` commands that *preview* what would happen, without touching your browser data yet

More functionality will be added over time, but this README only describes what is implemented **today**.

---

## ✅ Current Status

- **Platform**: Windows 10/11 (x64)
- **Browser support**: Microsoft Edge (Chromium) only
- **Interface**: CLI (command-line) only
- **Writes to browser DB**: ❌ No – all operations are read-only for now

---

## ✨ What it can do

### 1. Detect Edge profiles

The `detect` command scans your local `AppData` for Chromium user-data directories and prints what it finds.  
On a typical system this will show your Edge profile paths.

### 2. Export Edge cookies to JSON

The `export` command:

- Reads `Local State` and the Edge `Cookies` SQLite database for a chosen profile
- Decrypts cookie values using **DPAPI + AES-GCM**
- Copies the DB to a temporary location to avoid file locks
- Lets you filter by:
  - domain patterns (`--domains "*.example.com,foo.com"`)
  - cookie names (`--names "sessionid,csrftoken"`)
  - whether to include session cookies (`--include-session` or `--all`)
- Writes the result to a JSON file

### 3. Preview imports from JSON

The `import` command:

- Loads a JSON export created by `export`
- Applies the same domain/name/session filters
- Reports how many cookies would be imported into the chosen target browser/profile

> ⚠ **Important:** `import` currently **does not modify** any browser database.  
> It is a **preview / planning** command only.

### 4. Preview transfers between profiles

The `transfer` command:

- Reads cookies from a source browser/profile (currently only Edge is implemented)
- Applies filters
- Reports how many cookies would be transferred to the target browser/profile

> ⚠ **Important:** `transfer` also **does not write** to browser databases yet.  
> It is effectively “export + import in memory” with a summary.

---

## 🧱 Project Layout

CookiePorter/
├─ CookiePorter.Core/   # Core logic: Edge cookie reader, crypto, models
└─ CookiePorter.Cli/    # Command-line entry point (dotnet run / .exe)


🚀 Building and Running
Requirements
Windows 10 or 11 (x64)

.NET 8 SDK

Build
bash
Copy code
git clone https://github.com/alex-luncan/CookiePorter.git
cd CookiePorter
dotnet build
Run (CLI)
From the repo root:

bash
Copy code
# Detect profiles (mainly Edge for now)
dotnet run --project CookiePorter.Cli -- detect
Or run the built CookiePorter.Cli.exe directly.

🧾 Command Reference (current)
detect
bash
Copy code
CookiePorter.Cli.exe detect
Detect installed Chromium user-data directories and profiles (primarily Edge).

export
Export cookies from Edge to a JSON file.

bash
Copy code
CookiePorter.Cli.exe export --from edge --profile Default --out cookies.json
Options:

--from
Browser key. Currently only edge is supported.

--profile / --from-profile
Edge profile name (e.g. Default).

--out
Output JSON file path.

--domains
Comma-separated domain filters.
Example: --domains "*.example.com,login.site.com"

--names
Comma-separated cookie name filters.
Example: --names "sessionid,csrftoken"

--include-session
Include session cookies (those without an expiry).

--all
Include all cookies (overrides the default behavior of skipping session cookies).

Example:

bash
Copy code
CookiePorter.Cli.exe export --from edge --profile Default --out edge-cookies.json --domains "*.proton.me" --include-session
import (preview only)
Load cookies from a JSON file and show how many would be imported.

bash
Copy code
CookiePorter.Cli.exe import --to edge --profile Default --in edge-cookies.json
Options:

--to
Target browser key (currently treated as edge).

--profile / --to-profile
Target profile name.

--in
JSON file produced by export.

--domains, --names, --include-session, --all
Same filters as with export.

🔒 No changes are made to your browser.
This command is read-only and meant to verify data before we implement safe writing.

transfer (preview only)
Preview a transfer from one browser/profile to another.

bash
Copy code
CookiePorter.Cli.exe transfer \
  --from edge --from-profile Default \
  --to   edge --to-profile TestProfile \
  --domains "*.example.com"
Options:

--from, --to
Browser keys (currently effectively Edge → Edge).

--from-profile, --to-profile
Source and target profiles.

--domains, --names, --include-session, --all
Same filters as above.

Again, this command does not write to any browser DB yet.

🔒 Decryption Details (Edge)
CookiePorter follows the same approach used by Chromium-based browsers on Windows:

Read the Edge Local State file.

Extract os_crypt.encrypted_key (Base64).

Strip the DPAPI prefix and decrypt with
ProtectedData.Unprotect(..., DataProtectionScope.CurrentUser).

Use the resulting AES key to decrypt cookie values in the Cookies SQLite DB:

New format: AES-GCM (v10 / v11) with nonce + ciphertext + tag.

Fallback to direct DPAPI decryption for legacy cookies.

Only a temporary copy of the DB is opened to avoid file locks.

All operations run under your current Windows user, so the DPAPI decryption matches what the browser itself can do.

🔭 Future Work
This project is actively evolving.
Additional features (such as safer import/transfer to browser profiles and broader browser support) will be added gradually, once they can be implemented in a safe and reliable way.

⚖️ License
CookiePorter Source-Available License
Copyright © 2025 Alex Luncan – All rights reserved.

Permission is hereby granted to any individual to view, study, and use the
source code of CookiePorter for personal, non-commercial purposes only.

You may:

View and study the source code.

Build and run the software for your own personal use.

Share links to the official repository.

You may not, without prior written permission from the author (Alex Luncan):

Modify, fork, or create derivative works of this software or its source code.

Redistribute, publish, or share compiled binaries or source code in any form.

Use this software, or any part of its code, for commercial purposes.

Rebrand, rename, or represent this software as your own.

Remove or alter copyright or author notices.

The software is provided “AS IS”, without warranty of any kind, express or
implied, including but not limited to the warranties of merchantability,
fitness for a particular purpose, and non-infringement. In no event shall the
author be liable for any claim, damages, or other liability, arising from or
in connection with the use or distribution of this software.

All rights not expressly granted to you are reserved by the author.
Jurisdiction: The Netherlands

💬 Credits
Developed by Alex Luncan
Written in C# (.NET 8) using Microsoft.Data.Sqlite and Windows DPAPI.
