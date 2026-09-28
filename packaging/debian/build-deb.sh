#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
VERSION="${VERSION:-2.15.0}"
ARCHITECTURE="$(dpkg --print-architecture)"

if [[ "$ARCHITECTURE" != "amd64" ]]; then
    printf 'Unsupported architecture: %s (expected amd64)\n' "$ARCHITECTURE" >&2
    exit 1
fi

if [[ ! "$VERSION" =~ ^[0-9][A-Za-z0-9.+~:-]*$ ]]; then
    printf 'Invalid Debian package version: %s\n' "$VERSION" >&2
    exit 1
fi

BUILD_DIR="$(mktemp -d)"
trap 'rm -rf "$BUILD_DIR"' EXIT
PUBLISH_DIR="$BUILD_DIR/publish"
PACKAGE_ROOT="$BUILD_DIR/package"
OUTPUT_DIR="$ROOT_DIR/artifacts/debian"
OUTPUT_PACKAGE="$OUTPUT_DIR/gmkvextractgui_${VERSION}_${ARCHITECTURE}.deb"

dotnet publish "$ROOT_DIR/src/gMKVExtractGUI.Linux/gMKVExtractGUI.Linux.csproj" \
    --configuration Release \
    --runtime linux-x64 \
    --self-contained true \
    --output "$PUBLISH_DIR"

install -d "$PACKAGE_ROOT/DEBIAN" \
    "$PACKAGE_ROOT/usr/lib/gmkvextractgui" \
    "$PACKAGE_ROOT/usr/bin" \
    "$PACKAGE_ROOT/usr/share/applications" \
    "$PACKAGE_ROOT/usr/share/doc/gmkvextractgui"
cp -a "$PUBLISH_DIR/." "$PACKAGE_ROOT/usr/lib/gmkvextractgui/"
install -m 0644 "$ROOT_DIR/LICENSE" "$PACKAGE_ROOT/usr/share/doc/gmkvextractgui/copyright"
install -m 0644 "$ROOT_DIR/packaging/debian/gmkvextractgui.desktop" \
    "$PACKAGE_ROOT/usr/share/applications/gmkvextractgui.desktop"

printf '%s\n' '#!/bin/sh' 'exec /usr/lib/gmkvextractgui/gMKVExtractGUI "$@"' \
    > "$PACKAGE_ROOT/usr/bin/gMKVExtractGUI"
chmod 0755 "$PACKAGE_ROOT/usr/bin/gMKVExtractGUI" \
    "$PACKAGE_ROOT/usr/lib/gmkvextractgui/gMKVExtractGUI"

cat > "$PACKAGE_ROOT/DEBIAN/control" <<EOF
Package: gmkvextractgui
Version: $VERSION
Section: video
Priority: optional
Architecture: $ARCHITECTURE
Maintainer: gMKVExtractGUI contributors
Depends: mkvtoolnix, libfontconfig1, libice6, libsm6, libx11-6, libxext6, libxrender1, libxrandr2, libxcursor1, libxi6
Description: Matroska track extraction GUI
 Cross-platform desktop frontend for extracting Matroska tracks,
 chapters, attachments, cues, timecodes, and tags.
EOF

mkdir -p "$OUTPUT_DIR"
dpkg-deb --build --root-owner-group "$PACKAGE_ROOT" "$OUTPUT_PACKAGE"
printf 'Created %s\n' "$OUTPUT_PACKAGE"