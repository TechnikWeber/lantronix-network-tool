#!/usr/bin/env bash
# Builds dist/lantronix-network-tool-<version>-x86_64.AppImage (+ .zsync).
# Self-contained .NET publish of the window and lantronix-scan; needs the .NET 10 SDK,
# curl and FUSE or the extract-and-run fallback.
set -euo pipefail

here="$(cd "$(dirname "$(readlink -f "${BASH_SOURCE[0]}")")" && pwd)"
root="$(cd "$here/../.." && pwd)"
version="${VERSION:-$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$root/Directory.Build.props")}"
work="$root/build/appimage"
dist="$root/dist"
out="$dist/lantronix-network-tool-$version-x86_64.AppImage"
update_info="gh-releases-zsync|TechnikWeber|lantronix-network-tool|latest|lantronix-network-tool-*-x86_64.AppImage.zsync"

rm -rf "$work"
mkdir -p "$work/AppDir/usr/lib/lantronix-network-tool" "$dist"
app="$work/AppDir/usr/lib/lantronix-network-tool"

# both into one folder: untrimmed, so the shared runtime files are identical
for proj in src/LantronixScan src/LantronixTool; do
    dotnet publish "$root/$proj" -c Release -r linux-x64 --self-contained -p:Version="$version" \
        -p:DebugType=none -p:PublishTrimmed=false -o "$app"
done

cd "$work"
cat > AppDir/AppRun <<'SH'
#!/bin/sh
# "AppImage scan …" runs the command line tool instead of the window
here="${APPDIR:-$(dirname "$(readlink -f "$0")")}"
if [ "${1:-}" = "scan" ] || [ "${1:-}" = "assign" ]; then
    exec "$here/usr/lib/lantronix-network-tool/lantronix-scan" "$@"
fi
exec "$here/usr/lib/lantronix-network-tool/lantronix-network-tool" "$@"
SH
chmod +x AppDir/AppRun
install -Dm644 "$root/share/lantronix-network-tool.desktop" AppDir/lantronix-network-tool.desktop
install -Dm644 "$root/share/lantronix-network-tool.desktop" AppDir/usr/share/applications/lantronix-network-tool.desktop
install -Dm644 "$root/share/lantronix-network-tool.png" AppDir/lantronix-network-tool.png
install -Dm644 "$root/share/lantronix-network-tool.png" AppDir/usr/share/icons/hicolor/256x256/apps/lantronix-network-tool.png
install -Dm644 "$root/share/lantronix-network-tool.svg" AppDir/usr/share/icons/hicolor/scalable/apps/lantronix-network-tool.svg
ln -sf lantronix-network-tool.png AppDir/.DirIcon
install -Dm644 "$root/share/io.github.technikweber.lantronix-network-tool.metainfo.xml" \
    AppDir/usr/share/metainfo/io.github.technikweber.lantronix-network-tool.appdata.xml
chmod -R a+rX AppDir

export APPIMAGE_EXTRACT_AND_RUN=1   # no FUSE needed on CI runners
curl -fsSL -o appimagetool \
    https://github.com/AppImage/appimagetool/releases/download/continuous/appimagetool-x86_64.AppImage
chmod +x appimagetool
rm -f "$out" "$out.zsync"
ARCH=x86_64 ./appimagetool --no-appstream -u "$update_info" AppDir "$out"
mv -f "$(basename "$out").zsync" "$dist/" 2>/dev/null || true
ls -lh "$dist"
