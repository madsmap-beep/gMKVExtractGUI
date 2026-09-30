#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
APPIMAGETOOL="${APPIMAGETOOL:-appimagetool}"
OUTPUT_DIR="$ROOT_DIR/artifacts/appimage"
OUTPUT_IMAGE="$OUTPUT_DIR/gMKVExtractGUI-x86_64.AppImage"
BUILD_DIR="$(mktemp -d)"
APP_DIR="$BUILD_DIR/gMKVExtractGUI.AppDir"
trap 'rm -rf "$BUILD_DIR"' EXIT

if [[ "$(uname -m)" != "x86_64" ]]; then
    printf 'AppImage build requires an x86_64 Linux host.\n' >&2
    exit 1
fi

if ! command -v dotnet >/dev/null 2>&1; then
    printf '.NET 8 SDK is required to publish the application.\n' >&2
    exit 1
fi

if ! command -v "$APPIMAGETOOL" >/dev/null 2>&1 && [[ ! -x "$APPIMAGETOOL" ]]; then
    printf 'appimagetool was not found. Set APPIMAGETOOL to its executable path.\n' >&2
    exit 1
fi

dotnet publish "$ROOT_DIR/src/gMKVExtractGUI.Linux/gMKVExtractGUI.Linux.csproj" \
    --configuration Release \
    --runtime linux-x64 \
    --self-contained true \
    --output "$APP_DIR/usr/bin"

install -d "$APP_DIR/usr/share/applications" "$APP_DIR/usr/share/icons/hicolor/scalable/apps"
install -m 0644 "$ROOT_DIR/packaging/appimage/gMKVExtractGUI.desktop" "$APP_DIR/gMKVExtractGUI.desktop"
install -m 0644 "$ROOT_DIR/packaging/appimage/gMKVExtractGUI.desktop" \
    "$APP_DIR/usr/share/applications/gMKVExtractGUI.desktop"
install -m 0644 "$ROOT_DIR/packaging/appimage/gMKVExtractGUI.svg" "$APP_DIR/gMKVExtractGUI.svg"
install -m 0644 "$ROOT_DIR/packaging/appimage/gMKVExtractGUI.svg" \
    "$APP_DIR/usr/share/icons/hicolor/scalable/apps/gMKVExtractGUI.svg"

cat > "$APP_DIR/AppRun" <<'EOF'
#!/bin/sh
HERE="$(dirname "$(readlink -f "$0")")"
export APPDIR="$HERE"
export PATH="$APPDIR/usr/bin:$PATH"
exec "$APPDIR/usr/bin/gMKVExtractGUI" "$@"
EOF
chmod 0755 "$APP_DIR/AppRun" "$APP_DIR/usr/bin/gMKVExtractGUI"
ln -sf gMKVExtractGUI.svg "$APP_DIR/.DirIcon"

mkdir -p "$OUTPUT_DIR"
if [[ "$APPIMAGETOOL" == *.AppImage ]]; then
    ARCH=x86_64 APPIMAGE_EXTRACT_AND_RUN=1 "$APPIMAGETOOL" "$APP_DIR" "$OUTPUT_IMAGE"
else
    ARCH=x86_64 "$APPIMAGETOOL" "$APP_DIR" "$OUTPUT_IMAGE"
fi
chmod 0755 "$OUTPUT_IMAGE"
printf 'Created %s\n' "$OUTPUT_IMAGE"