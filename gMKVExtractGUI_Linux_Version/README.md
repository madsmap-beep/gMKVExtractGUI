# gMKVExtractGUI Linux Version

This directory is a standalone Linux-only edition of gMKVExtractGUI. It contains the Avalonia desktop application, the cross-platform `gMKVToolNix` library it uses, and Linux package definitions. It does not contain the Windows Forms application or Windows build scripts.

The application bundles the .NET runtime in its packages. MKVToolNix and the host's Avalonia/X11 system libraries are installed as package dependencies.

## Build packages

Run each package build on its target Linux distribution with the .NET 8 SDK installed:

- Debian or Ubuntu: install `dpkg-deb`, then run `bash packaging/debian/build-deb.sh`.
- Fedora: install `rpm-build`, then run `bash packaging/fedora/build-rpm.sh`.
- Arch Linux: install `base-devel` and `dotnet-sdk`, then run `bash packaging/arch/build-arch.sh`.
- AppImage: install `appimagetool`, then run `bash packaging/appimage/build-appimage.sh`.

The generated packages are written to `artifacts/debian`, `artifacts/fedora`, and `artifacts/arch`; the AppImage is written to `artifacts/appimage`. Set `VERSION` to override the default package version (`2.15.0`) for package builds. All builds target x86_64 Linux.

## Build the application

From this directory, run:

```sh
dotnet publish src/gMKVExtractGUI.Linux/gMKVExtractGUI.Linux.csproj \
  --configuration Release --runtime linux-x64 --self-contained true
```