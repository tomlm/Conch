# Conch

A TUI application shell: a windowing environment for terminal apps.

Conch hosts multiple XTerm terminal windows inside a single console session, so a
machine with no graphical stack still gets overlapping, movable, resizable windows.
Applications are described by `.yml` registration files that declare how to install,
detect, and launch a terminal application.

## Install

```sh
dotnet tool install --global Conch
conch
```

Runs on Windows, Linux and macOS. On Windows, a registration that only describes a
Linux build is launched and installed through WSL.

## App registrations

An app is a YAML file. The seed catalog ships with the tool (see
[`src/Conch/Tools`](src/Conch/Tools)) and is refreshed from this repo at startup, so a
machine with no network still has a usable app list.

```yaml
id: btop
name: btop
description: Resource monitor in the terminal
command: btop
args: ""
platforms:
  linux:
    install: sudo apt-get update && sudo apt-get install -y btop
    uninstall: sudo apt-get remove -y btop
```

`platforms` carries a per-OS `install`, `uninstall`, and optional `detect` command.
Without `detect`, Conch decides whether an app is present by looking for `command` on
`PATH`.

`args` is a template: `%1` is a required value supplied at launch, `%1?` an optional
one. An app declaring no placeholders launches immediately; one that declares them is
prompted for. Values beyond the declared placeholders are appended, so a registration
declaring `%1?` still opens several files.

## Roles

A role is a job the shell asks something else to do — open a folder, edit a file,
configure the network — rather than an app you pick by name. A registration offers
itself for one by declaring it:

```yaml
roles:
  - text-editor
```

`Tools → Settings` lists every role and what serves it. Some have an implementation
built into Conch and that is the default; any of them can be pointed at an installed
app instead. The roles are `app-launcher`, `file-explorer`, `app-manager`,
`text-editor`, `system-monitor`, `network-config` and `audio-config`.

Settings stores the choice, not the implementation, so uninstalling the app you chose
falls back to the built-in rather than leaving the role broken.

## Session

`conch --session` tells Conch it *is* the session rather than one program among many,
which adds Restart and Shut Down to the System menu. Logging out is always available.
Conchix sets the flag through `CONCH_ARGS` in `/etc/default/conch`.

## Building

```sh
dotnet build src/Conch.slnx
dotnet test src/Conch.slnx
dotnet pack src/Conch/Conch.csproj -c Release -o dist
```

Releases go to NuGet through the `Publish Nuget` workflow.

## Conchix

[Conchix](https://github.com/tomlm/Conchix) (*conk-ix*) is a minimal Debian
distribution that boots to kmscon with Conch as the session shell. Everything
distro-shaped — image recipe, Debian packaging, systemd units, kmscon and console
configuration — lives there. This repo is just the shell, and is useful on its own.

## License

MIT. See [LICENSE](LICENSE).
