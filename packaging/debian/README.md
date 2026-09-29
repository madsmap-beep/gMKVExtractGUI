# Debian package

Build the native Linux application package on Ubuntu amd64 with the .NET 8 SDK and `dpkg-deb` installed:

```sh
bash packaging/debian/build-deb.sh
```

The package is written to `artifacts/debian/gmkvextractgui_2.15.0_amd64.deb`. It bundles the .NET runtime, depends on the Ubuntu `mkvtoolnix` package and Avalonia's X11/font libraries, and adds an application-menu entry. Install it with:

```sh
sudo apt install ./artifacts/debian/gmkvextractgui_2.15.0_amd64.deb
```

After installation, open Matroska files from the application menu or choose multiple files in the app. The package also registers the app as a handler for Matroska video and audio files, so compatible file managers can launch it with selected files.

Set `VERSION` to override the package version, for example `VERSION=2.15.1 bash packaging/debian/build-deb.sh`.