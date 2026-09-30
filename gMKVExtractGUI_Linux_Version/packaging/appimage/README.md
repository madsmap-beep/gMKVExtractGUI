# AppImage

Build an x86_64 AppImage on Linux with the .NET 8 SDK and `appimagetool` available:

```sh
bash packaging/appimage/build-appimage.sh
```

The output is `artifacts/appimage/gMKVExtractGUI-x86_64.AppImage`. To use a downloaded AppImage build of `appimagetool`, set its path explicitly:

```sh
APPIMAGETOOL="$HOME/Applications/appimagetool-x86_64.AppImage" bash packaging/appimage/build-appimage.sh
```

The AppImage bundles the .NET runtime and application files. MKVToolNix and the host's X11/font libraries must be available on the system. This package targets x86_64 Linux desktops.