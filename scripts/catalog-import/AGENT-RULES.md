# Writing Conch app registrations

Conch (S:\github\Conch) is a TUI shell. Each app is one YAML file in
`S:\github\Conch\src\Conch\Tools\<PascalName>.yml`. Read 3-4 existing ones first
(Btop.yml, Nano.yml, Edit.NET.yml, Asciiquarium.yml, Dotnet10.yml) and match them.
Conchix, the main Linux target, is **Debian 13 (trixie)**. On Windows, an app with only a
`linux:` entry runs inside WSL automatically, so a Windows entry is only for a NATIVE build.

Source data: round 1 came from an `apps.json` scraped from
https://www.linuxlinks.com/100-awesome-must-have-tui-linux-apps/ (name, the article's
description, screenshot URL, website, license, language); later rounds use the `batchNN.json`
files described at the end. Whatever the source says about platforms is NOT reliable; verify
everything yourself.

## Fields (exact key names)

```yaml
id: lazygit            # ^[a-z0-9.]+$ only — no hyphens/underscores; use dots (dua.cli)
name: lazygit          # display name, as the project spells it
version: 1.0.0         # registration version, always 1.0.0
author: Jesse Duffield # project author/org
description: Simple terminal UI for git commands   # ONE plain sentence, see below
license: MIT           # SPDX id (GPL-2.0, GPL-3.0, MIT, Apache-2.0, BSD-3-Clause, MPL-2.0 …)
platforms:
  linux:
    install: ...
    uninstall: ...
    detect: ...        # optional; only when "command on PATH" is not proof of install
  windows:             # ONLY if a native winget package exists (verified)
    install: winget install -e --id Publisher.Package
    uninstall: winget uninstall -e --id Publisher.Package
command: lazygit       # the executable the package actually puts on PATH (verify!)
args: ""               # argument template: "" none, "%1?" optional path/file, "%1" required
source: https://github.com/jesseduffield/lazygit
website: https://github.com/jesseduffield/lazygit
documentation: https://github.com/jesseduffield/lazygit#readme
screenshot: https://…png   # see Screenshots
keywords:
  - git
  - tui
roles: []              # see Roles; omit or [] when none
```

`cols`/`rows` may be added only if the app needs more than 80x24 (never less).

### Description
One sentence, plain, no marketing, no "we think". Start from the article's first sentence
in apps.json, rewritten as a noun phrase like the existing files ("Resource monitor that
shows usage and stats for processor, memory, disks, network and processes"). Fix typos.

### Roles
Only these exist: `file-explorer`, `text-editor`, `system-monitor`, `network-config`,
`audio-config` (others are built-in only). Add one only when the app genuinely does that job
AND accepts what the shell passes: text-editor and file-explorer get a path argument
(args must be "%1?"); system-monitor/network-config/audio-config get none. E.g. Helix →
text-editor; pulsemixer → audio-config. Most apps: no role.

## Choosing the Linux install — in this order, first that works

1. **Debian 13 package.** Verify it exists: `curl -s https://packages.debian.org/trixie/PKG`
   must show `<h1>Package: PKG`. Verify the binary name from the file list:
   `curl -s https://packages.debian.org/trixie/amd64/PKG/filelist` (look under /usr/bin).
   install: `sudo apt-get update && sudo apt-get install -y PKG`
   uninstall: `sudo apt-get remove -y PKG`
2. **Python app on PyPI** (not in Debian): use pipx global install (pipx 1.7 in trixie):
   install: `sudo apt-get update && sudo apt-get install -y pipx && sudo pipx install --global PYPIPKG`
   uninstall: `sudo pipx uninstall --global PYPIPKG`
   Verify the PyPI name and console-script name: `curl -s https://pypi.org/pypi/PYPIPKG/json`
   (info.name; entry points are in the project's pyproject/setup — check the repo).
3. **Prebuilt static binary from the project's GitHub releases** (Go/Rust apps not in Debian).
   Pin the latest release version; get the asset names and compute SHA-256 of BOTH the
   linux x86_64 and aarch64 assets (download them with curl, `sha256sum`). Use the musl/static
   asset when offered. Pattern (YAML folded scalar, each line joined by a space):
   ```yaml
   install: >-
     t=$(mktemp -d) && cd "$t" &&
     case "$(uname -m)" in
     x86_64) a=https://github.com/O/R/releases/download/vX/app-x86_64-unknown-linux-musl.tar.gz; s=SHA256X ;;
     aarch64) a=https://github.com/O/R/releases/download/vX/app-aarch64-unknown-linux-musl.tar.gz; s=SHA256A ;;
     *) echo "unsupported architecture $(uname -m)"; exit 1 ;;
     esac &&
     wget -q -O pkg "$a" && echo "$s  pkg" | sha256sum -c --quiet &&
     tar -xzf pkg && sudo install -m 755 PATH/IN/ARCHIVE/app /usr/local/bin/app &&
     cd / && rm -rf "$t"
   uninstall: sudo rm -f /usr/local/bin/app
   ```
   Check the archive layout (where the binary sits inside it) by actually listing it.
   If an arch has no asset, keep only the arches that exist. Zip assets: use `unzip -q`
   and add `unzip` to an apt-get line first. Plain-binary assets: skip tar.
   Prefix with `sudo apt-get update && sudo apt-get install -y wget &&` (minimal images may lack it).
4. **Node app**: `sudo apt-get update && sudo apt-get install -y npm && sudo npm install -g PKG`,
   uninstall `sudo npm uninstall -g PKG` — only if the npm package is current and works.

Never: snap, flatpak, brew, `curl … | sh`, cargo/go install (needs toolchains), unpinned
"latest" downloads, or anything needing a desktop.

## Windows entry (optional)
Only when the app runs natively on Windows AND winget has it. Verify on this machine:
`winget show -e --id ID --disable-interactivity --accept-source-agreements` must say
`Found …`. `-e` is CASE-SENSITIVE: copy the id exactly as `winget search NAME` prints it.
Check the winget package actually provides `command` (portable packages add an alias; MSI
installers may not touch PATH — say so in your report if unsure). Run winget calls one at a
time; other agents use winget concurrently, retry once if it errors.

## Screenshots
Prefer the project's own screenshot (README image on GitHub — use the raw URL, e.g.
https://raw.githubusercontent.com/O/R/<commit-or-tag>/docs/screenshot.png, or the
user-attachments/project-site URL the README uses). PNG/JPEG/GIF only (no SVG/WebP), and
prefer under ~3 MB. Verify: `curl -s -o /dev/null -w "%{http_code} %{content_type} %{size_download}" URL`
→ 200 and image/png|jpeg|gif. If the project has none, use the article's URL from apps.json
(also verify). Leave the key out if neither works.

## Exclude an app (don't write a file) when
it has no executable to launch (library/framework), needs X11/Wayland, is a bootable image,
is archived/abandoned AND its service API no longer works, or no install route above works
on Debian 13. Report each exclusion with a one-line reason.

## Existing files
Btop.yml, Nnn.yml, Ncdu.yml, Weechat.yml already exist: EDIT them in place (keep their id,
keep their working install lines), only improving description/screenshot/missing fields.

## Don'ts
- Do NOT run `dotnet build`/`dotnet test` (other agents share the project; the lead runs them).
- Do NOT git add/commit/push. Do NOT touch files outside your assigned apps.
- Do not invent facts; if you cannot verify something, leave it out and say so.

## Report back
A table: app | file | linux route (apt/pipx/release/npm) | windows (id or —) | screenshot
(upstream/article/none) | notes. Then exclusions with reasons, then anything unverified.
# Imports from Terminal Trove + awesome-tuis (round 2 onwards)

Follow ALL of the rules above (fields, install-route order, verification commands, Windows
rules, never-list, don'ts, report format). This file only lists what is different.

## Source data
Your batch is `batchNN.json` in this folder. Per app:
- `name`, `repo` (GitHub/GitLab/Codeberg URL), `description` (Terminal Trove's tagline, or
  awesome-tuis' line), `image` (Terminal Trove screenshot, may be empty), `section` /
  `categories` (where the sources filed it), `stars`, `language`, `license` (GitHub SPDX),
  `release` (latest tag), `linuxAsset` (true if that release has a linux x86_64 asset),
  `troveInstall` (Terminal Trove's install commands per manager — a HINT for package names;
  never copy brew/cargo/go commands as the install line),
  `debian` (source package matched by upstream repo URL — strong hint) and `debianByName`
  (a Debian package with the repo's name — WEAK hint, names collide, verify it is the same
  program), `route` (the triage's guess; verify, don't trust).

## Only real TUIs
Exclude, with a reason, anything that is not an interactive full-screen terminal program:
- pure CLI tools that print and exit (tldr, formatters, one-shot reporters);
- web services or web UIs (wttr.in, ntop's web interface), GUI apps, operating systems or
  distros (Talos Linux), libraries/frameworks/SDKs;
- apps whose only install route is `cargo install` or `go install` (no Debian package, no
  prebuilt release binary): these are DEFERRED to a later round with a Rust/Go toolchain —
  list them separately as "deferred: cargo" / "deferred: go", don't write a file.
Devzat was already excluded last round (its user side is plain ssh) — skip it.

An app with BOTH a CLI and a TUI mode is fine; set `args` so launching it opens the TUI.

## Screenshots
Same preference as SPEC.md (the project's own, pinned), then the batch's `image` URL
(cdn.terminaltrove.com — verified to serve images directly), then omit.

## Collisions with the existing catalog
93 registrations already exist in S:\github\Conch\src\Conch\Tools. Before writing, check that
your `id` and file name are not already taken (grep `^id:` across the folder) and that the
app is not already there under another name (grep its repo URL). If it is, skip it and say so.
Other agents are writing at the same time; if a file name you want appears while you work,
pick another and say so.

## AI coding agents (claude-code, codex, gemini-cli, crush, …)
They are TUIs — include them normally. Prefer the vendor's own install route: Debian package,
release binary, or npm (`sudo npm install -g PKG`). No `curl | sh` installers.

## Report
Same table as SPEC.md, then exclusions (reason), deferred (cargo/go), unverified.
