# Catalog import

How the app catalog in `src/Conch/Tools` is grown from public lists of TUI apps, without
Conch depending on any of them at runtime.

The catalog stays the only thing Conch installs from. Every registration in it has been
checked by hand (or by an agent following `AGENT-RULES.md`): the package exists on Debian 13,
the binary name is right, a release download is pinned and hashed, a winget id resolves. The
lists below are where candidates come from, not a source of truth.

## Sources

- [Terminal Trove](https://terminaltrove.com/), TUI category: tagline, screenshot, and its
  per-package-manager install commands. No API; pages are fetched once per import, slowly.
  If Terminal Trove ever offers a JSON export, use that instead of `fetch-sources.sh`.
- [awesome-tuis](https://github.com/rothgar/awesome-tuis): a curated Markdown list, by section.
- Debian 13's source index, to match apps to Debian packages by their upstream repository.
- GitHub's API for stars, archive state, last push, licence and release assets.

## Running an import

Needs bash with curl and xz (WSL works), PowerShell 7 and an authenticated `gh`.

```sh
./fetch-sources.sh work                                   # ~15 min, resumable
pwsh ./parse-sources.ps1 -Work work
pwsh ./ghmeta.ps1 -ReposFile work/repos.txt -OutFile work/meta.json
pwsh ./triage.ps1 -Work work -MinStars 1000 -BatchSize 20
```

`triage.ps1` writes `candidates.json` (everything, with its classification), one
`batchNN.json` per batch of this round, and `deferred.json`. It drops:

- apps already in the catalog (matched by repository URL in `source`/`website`/`documentation`),
- libraries and frameworks (awesome-tuis' Libraries section, Terminal Trove's tui-frameworks),
- archived repositories, and ones with no push in three years.

Each batch then goes to an agent with `AGENT-RULES.md`, which writes and verifies the
registrations. Run `dotnet test src/Conch.slnx` afterwards: the catalog tests check every file.

## Rounds so far

| Round | Source | Scope | Result |
|---|---|---|---|
| 1 | LinuxLinks' 100 must-have TUI apps | all | 76 added, 4 updated, 20 excluded |
| 2 | Terminal Trove + awesome-tuis | 1000+ stars, no cargo/go | 178 added of 227, 49 excluded |

## Deferred: apps that only build from source

About 150 maintained apps (29 of them with 1000+ stars) have no Debian package and no release
binary; their only route is `cargo install` or `go install`. They need two prerequisite
registrations first, tested on Debian 13 before anything depends on them:

- `rust.toolchain`: Debian's `rustup` with a system-wide toolchain under `/opt/rust`, since
  Debian's own cargo (1.85) is older than many crates now require.
- `go.toolchain`: Debian's `golang-go`, with installs run under `GOTOOLCHAIN=auto` (Debian
  builds Go with `local`) so a module can fetch the newer toolchain it asks for.
