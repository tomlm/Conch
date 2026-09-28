#!/usr/bin/env bash
#
# Builds the conch .deb from a self-contained publish tree.
#
# The app is shipped as a self-contained .NET publish rather than depending on a
# dotnet runtime package: Debian does not carry .NET 10, and an appliance image
# should not have to add Microsoft's apt repository just to boot its shell.
#
#   ./packaging/build-deb.sh --version 0.1.0 --arch amd64
#
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

VERSION=""
ARCH="amd64"
RID=""
PUBLISH_DIR=""
OUT_DIR="$REPO_ROOT/dist"

usage() {
    cat <<'EOF'
Usage: build-deb.sh --version <x.y.z> [options]

  --version <x.y.z>   Package version (required)
  --arch <amd64|arm64>  Debian architecture (default: amd64)
  --publish-dir <dir> Use an existing publish tree instead of building one
  --out <dir>         Where to write the .deb (default: ./dist)
EOF
}

while [[ $# -gt 0 ]]; do
    case "$1" in
        --version) VERSION="$2"; shift 2 ;;
        --arch) ARCH="$2"; shift 2 ;;
        --publish-dir) PUBLISH_DIR="$2"; shift 2 ;;
        --out) OUT_DIR="$2"; shift 2 ;;
        -h|--help) usage; exit 0 ;;
        *) echo "unknown argument: $1" >&2; usage; exit 1 ;;
    esac
done

if [[ -z "$VERSION" ]]; then
    echo "error: --version is required" >&2
    usage
    exit 1
fi

case "$ARCH" in
    amd64) RID="linux-x64" ;;
    arm64) RID="linux-arm64" ;;
    *) echo "error: unsupported arch '$ARCH' (expected amd64 or arm64)" >&2; exit 1 ;;
esac

STAGE="$(mktemp -d)"
trap 'rm -rf "$STAGE"' EXIT

# ---------------------------------------------------------------- publish ----
if [[ -z "$PUBLISH_DIR" ]]; then
    PUBLISH_DIR="$STAGE/publish"
    echo ":: publishing $RID"
    dotnet publish "$REPO_ROOT/src/Conch/Conch.csproj" \
        -c Release \
        -r "$RID" \
        --self-contained true \
        -p:Version="$VERSION" \
        -o "$PUBLISH_DIR" \
        --nologo
fi

if [[ ! -f "$PUBLISH_DIR/Conch" ]]; then
    echo "error: $PUBLISH_DIR/Conch not found; is this a linux publish tree?" >&2
    exit 1
fi

if [[ ! -d "$PUBLISH_DIR/Tools" ]]; then
    echo "error: $PUBLISH_DIR/Tools missing; the seed catalog must ship with the app" >&2
    exit 1
fi

# ------------------------------------------------------------------ stage ----
ROOT="$STAGE/root"
install -d "$ROOT/DEBIAN"
install -d "$ROOT/usr/lib/conch"
install -d "$ROOT/usr/bin"
install -d "$ROOT/lib/systemd/system"
install -d "$ROOT/usr/share/doc/conch"

cp -r "$PUBLISH_DIR"/. "$ROOT/usr/lib/conch/"

# A publish tree built on Windows arrives with every file executable. Normalise
# to data permissions, then restore the bit on the things that actually run.
find "$ROOT/usr/lib/conch" -type d -exec chmod 0755 {} +
find "$ROOT/usr/lib/conch" -type f -exec chmod 0644 {} +
chmod 0755 "$ROOT/usr/lib/conch/Conch"
[ -f "$ROOT/usr/lib/conch/createdump" ] && chmod 0755 "$ROOT/usr/lib/conch/createdump"

# A wrapper rather than a symlink: AppContext.BaseDirectory must resolve to the
# payload directory so the seeded Tools catalog is found.
cat > "$ROOT/usr/bin/conch" <<'WRAPPER'
#!/bin/sh
exec /usr/lib/conch/Conch "$@"
WRAPPER
chmod 0755 "$ROOT/usr/bin/conch"

install -m 0644 "$SCRIPT_DIR/conch@.service" "$ROOT/lib/systemd/system/conch@.service"
install -m 0644 "$SCRIPT_DIR/debian/copyright" "$ROOT/usr/share/doc/conch/copyright"

printf 'conch (%s) unstable; urgency=medium\n\n  * Release %s.\n\n -- Tom Laird-McConnell <thermous@iciclecreek.com>  %s\n' \
    "$VERSION" "$VERSION" "$(date -R)" \
    | gzip -9n > "$ROOT/usr/share/doc/conch/changelog.gz"
chmod 0644 "$ROOT/usr/share/doc/conch/changelog.gz"

INSTALLED_KB="$(du -sk "$ROOT" | cut -f1)"

sed -e "s/@VERSION@/$VERSION/g" \
    -e "s/@ARCH@/$ARCH/g" \
    -e "s/@INSTALLED_SIZE@/$INSTALLED_KB/g" \
    "$SCRIPT_DIR/debian/control.in" > "$ROOT/DEBIAN/control"

install -m 0755 "$SCRIPT_DIR/debian/postinst" "$ROOT/DEBIAN/postinst"
install -m 0755 "$SCRIPT_DIR/debian/prerm" "$ROOT/DEBIAN/prerm"

# ------------------------------------------------------------------ build ----
mkdir -p "$OUT_DIR"
DEB="$OUT_DIR/conch_${VERSION}_${ARCH}.deb"

if command -v fakeroot >/dev/null 2>&1; then
    fakeroot dpkg-deb --build --root-owner-group "$ROOT" "$DEB"
else
    dpkg-deb --build --root-owner-group "$ROOT" "$DEB"
fi

echo
echo ":: built $DEB ($(du -h "$DEB" | cut -f1))"
