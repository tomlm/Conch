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

## The top bar

```
 🐚  🔍   nano  [btop]                                          ⇅  ♪  ▭  14:32
```

- **🐚** opens the one menu: Software; Network, Display, Audio and
  Preferences; About; Logout, with Restart and Shut Down when Conch is the session.
- **🔍** opens search, where everything starts. Empty, it lists the installed apps;
  typed into, it finds apps, settings and commands, and offers to run anything else as a
  command line in a terminal window that stays open once it finishes.
- **The window list** has a button per open window. Clicking one brings it forward, or
  minimizes it if it is already in front.
- **The status area** has one indicator per thing worth fixing from there, each opening
  its settings. Audio and Display appear only when something on the machine serves them.
  On a console font without the glyphs, *Preferences → Appearance* shows them as words.

### Hotkeys

| Keys | Does |
|---|---|
| Alt+F2, Ctrl+Space | Search apps and settings |
| Alt+F1 | Open the Conch menu |
| Ctrl+Alt+T | New terminal |
| Ctrl+Alt+E | Files |
| Ctrl+F10 | Maximize or restore the window |
| Ctrl+F4 | Close the window |
| Ctrl+F6, Ctrl+Shift+F6 | Next, previous window |

All but the last two can be changed in *Preferences → Keyboard*, and an action can have
more than one binding; the last two belong to the window manager. The defaults avoid the Windows/Super key, which Windows Terminal and
desktop environments keep for themselves, and Alt+F4, which closes the terminal itself
on Windows. Super combinations can still be bound where the terminal passes them through.

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

*Preferences → Default Apps* lists every role and what serves it. Some have an
implementation built into Conch and that is the default; any of them can be pointed at
an installed app instead. The roles are `file-explorer`, `app-manager`, `text-editor`,
`system-monitor`, `network-config`, `audio-config` and `display-config`.

Preferences stores the choice, not the implementation, so uninstalling the app you chose
falls back to the built-in rather than leaving the role broken.

## Session

`conch --session` tells Conch it *is* the session rather than one program among many,
which adds Restart and Shut Down to the Conch menu. Logout is always there; outside a
session it is called Exit.
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
