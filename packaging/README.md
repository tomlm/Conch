# Packaging

Produces the `conch` Debian package that [Conchix](https://github.com/tomlm/Conchix)
installs. The boundary between the two repos is this package: Conch knows how to
package and run itself, Conchix decides what the machine around it looks like.

## Building

```sh
./packaging/build-deb.sh --version 0.1.0 --arch amd64
```

Needs `dotnet`, `dpkg-deb`, and ideally `fakeroot`, so it runs on Linux (or WSL).
To package a tree published elsewhere — cross-publishing from Windows works fine:

```sh
dotnet publish src/Conch/Conch.csproj -c Release -r linux-x64 --self-contained -o /tmp/pub
./packaging/build-deb.sh --version 0.1.0 --arch amd64 --publish-dir /tmp/pub
```

CI does both architectures in `.github/workflows/PublishLinux.yml`, on a `v*` tag
or manual dispatch.

## What lands where

| Path | |
|---|---|
| `/usr/lib/conch/` | self-contained publish, including the seed `Tools/` catalog |
| `/usr/bin/conch` | wrapper that execs the payload |
| `/lib/systemd/system/conch@.service` | tty template unit, **not** enabled |
| `/usr/share/doc/conch/` | copyright, changelog |

`/usr/bin/conch` is a wrapper rather than a symlink on purpose:
`AppContext.BaseDirectory` has to resolve to `/usr/lib/conch` or the seeded catalog
is not found.

## Why self-contained

Debian does not carry .NET 10, and an appliance image should not have to add
Microsoft's apt repository just to boot its shell. The cost is size — roughly 111 MB
installed, 36 MB compressed.

Skia and HarfBuzz account for ~13 MB and the console renderer never draws with them,
but excluding native assets needs testing on a real console before it ships.

## Running it

The unit is a `getty`-style template, installed disabled. Enabling an instance takes
over that tty:

```sh
systemctl enable --now conch@tty1
```

It sets `Restart=always` with no start limit: on an appliance a shell that gives up
leaves a dead console, which is worse than a restart loop visible in the journal.
Override anything via a drop-in or `/etc/default/conch`.

Under Conchix the unit is not used directly — kmscon owns the tty and launches Conch
as the session. The unit is what makes Conch usable on a plain Linux console without
kmscon at all.

## Lintian

Two classes of finding are expected and not defects:

- `embedded-library` and `unstripped-binary-or-object` on `libSkiaSharp.so`,
  `libHarfBuzzSharp.so` and the .NET native shims. These are prebuilt binaries from
  NuGet; unbundling them would mean giving up the self-contained publish.
- `systemd-service-file-refers-to-unusual-wantedby-target getty.target`. That is the
  point of the unit.

## Known gaps

- **Runs as root.** There is no dedicated `conch` user yet, and no decision on the
  privilege model for the `sudo apt-get` install commands in the registrations.
- **Startup.** Measured ~12 s from process start to catalog load on Linux under a
  synthetic pty, against ~1–2 s on Windows. Reproducible when warm, so it is not JIT.
  Needs confirming on a real console before chasing it — the pty harness may be
  provoking terminal capability queries that time out.
- **No arm64 validation.** The workflow builds it; nothing has run it.
