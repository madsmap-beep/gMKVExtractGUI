#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
VERSION="${VERSION:-1.0}"
ARCH_DIR="$ROOT_DIR/packaging/arch"
OUTPUT_DIR="$ROOT_DIR/artifacts/arch"
SOURCE_ARCHIVE="$ARCH_DIR/gMKVExtractGUI_Linux_Version-${VERSION}.tar.gz"

if [[ "$(uname -m)" != "x86_64" ]]; then
    printf 'Arch package build requires an x86_64 Linux host.\n' >&2
    exit 1
fi

if ! command -v makepkg >/dev/null 2>&1; then
    printf 'makepkg is required; run this script on Arch Linux with base-devel installed.\n' >&2
    exit 1
fi

if [[ ! "$VERSION" =~ ^[0-9][A-Za-z0-9._-]*$ ]]; then
    printf 'Invalid Arch package version: %s\n' "$VERSION" >&2
    exit 1
fi

mkdir -p "$OUTPUT_DIR"
TEMP_ARCHIVE="$(mktemp)"
trap 'rm -f "$SOURCE_ARCHIVE" "$TEMP_ARCHIVE"' EXIT
tar -czf "$TEMP_ARCHIVE" \
    --exclude='*/bin' --exclude='*/obj' --exclude='*/artifacts' \
    -C "$ROOT_DIR/.." "$(basename "$ROOT_DIR")"
cp "$TEMP_ARCHIVE" "$SOURCE_ARCHIVE"
cd "$ARCH_DIR"
VERSION="$VERSION" makepkg --cleanbuild --force --pkgdest "$OUTPUT_DIR"
printf 'Created package(s) in %s\n' "$OUTPUT_DIR"