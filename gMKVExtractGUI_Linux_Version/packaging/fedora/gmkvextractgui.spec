Name:           gmkvextractgui
Version:        %{?pkgversion}%{!?pkgversion:1.0}
Release:        1%{?dist}
Summary:        Matroska track extraction GUI
License:        Unlicense
URL:            https://github.com/madsmap-beep/gMKVExtractGUI
Source0:        gMKVExtractGUI_Linux_Version-%{version}.tar.gz
BuildRequires:  dotnet-sdk-8.0
Requires:       mkvtoolnix
Requires:       fontconfig
Requires:       libICE
Requires:       libSM
Requires:       libX11
Requires:       libXext
Requires:       libXrender
Requires:       libXrandr
Requires:       libXcursor
Requires:       libXi

%description
Cross-platform desktop frontend for extracting Matroska tracks, chapters,
attachments, cues, timecodes, and tags.

%prep
%setup -q -c

%build
dotnet publish src/gMKVExtractGUI.Linux/gMKVExtractGUI.Linux.csproj \
    --configuration Release --runtime linux-x64 --self-contained true \
    --output publish

%install
install -d %{buildroot}/opt/gmkvextractgui \
    %{buildroot}/usr/bin \
    %{buildroot}/usr/share/applications \
    %{buildroot}/usr/share/icons/hicolor/scalable/apps
cp -a publish/. %{buildroot}/opt/gmkvextractgui/
printf '%s\n' '#!/bin/sh' 'exec /opt/gmkvextractgui/gMKVExtractGUI "$@"' \
    > %{buildroot}/usr/bin/gMKVExtractGUI
chmod 0755 %{buildroot}/usr/bin/gMKVExtractGUI \
    %{buildroot}/opt/gmkvextractgui/gMKVExtractGUI
install -m 0644 packaging/gmkvextractgui.desktop \
    %{buildroot}/usr/share/applications/gmkvextractgui.desktop
install -m 0644 packaging/gMKVExtractGUI.svg \
    %{buildroot}/usr/share/icons/hicolor/scalable/apps/gMKVExtractGUI.svg

%files
%license LICENSE
/opt/gmkvextractgui
/usr/bin/gMKVExtractGUI
/usr/share/applications/gmkvextractgui.desktop
/usr/share/icons/hicolor/scalable/apps/gMKVExtractGUI.svg

%changelog
* Wed Sep 30 2026 gMKVExtractGUI contributors - 1.0-1
- Initial Fedora package