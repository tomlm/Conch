# Conch

A TUI application shell: a windowing environment for terminal apps.

Conch hosts multiple XTerm terminal windows inside a single console session.
Applications are described by `.yml` registration files (see `src/Conch/Tools`)
that declare how to install, launch, and describe a terminal application.

Conch is the windowing shell for **Cellix**, a minimal Debian distribution that
boots to kmscon with Conch as the session shell.
