#!/usr/bin/env bash
# Builds dist/lantronix-network-tool-<version>-macos-<arch>.zip (the .app) and
# dist/lantronix-scan-<version>-macos-<arch>.tar.gz. Runs on macOS (sips, iconutil,
# codesign). The app is signed ad hoc only: on first start right-click › Open.
set -euo pipefail

arch="${1:?usage: build.sh arm64|x64}"
root="$(cd "$(dirname "$0")/../.." && pwd)"
version="${VERSION:-$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$root/Directory.Build.props")}"
work="$root/build/macos-$arch"
dist="$root/dist"
app="$work/Lantronix Network Tool.app"
rm -rf "$work"
mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources" "$dist"

dotnet publish "$root/src/LantronixTool" -c Release -r "osx-$arch" --self-contained \
    -p:PublishTrimmed=true -p:DebugType=none -p:Version="$version" -o "$app/Contents/MacOS"
rm -f "$app/Contents/MacOS/"*.xml

# icon
set=$work/icon.iconset
mkdir -p "$set"
for s in 16 32 128 256 512; do
    sips -z $s $s "$root/share/lantronix-network-tool-1024.png" --out "$set/icon_${s}x${s}.png" >/dev/null
    sips -z $((s*2)) $((s*2)) "$root/share/lantronix-network-tool-1024.png" --out "$set/icon_${s}x${s}@2x.png" >/dev/null
done
iconutil -c icns "$set" -o "$app/Contents/Resources/lantronix-network-tool.icns"

cat > "$app/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleName</key><string>Lantronix Network Tool</string>
  <key>CFBundleDisplayName</key><string>Lantronix Network Tool</string>
  <key>CFBundleIdentifier</key><string>io.github.technikweber.lantronix-network-tool</string>
  <key>CFBundleVersion</key><string>$version</string>
  <key>CFBundleShortVersionString</key><string>$version</string>
  <key>CFBundleExecutable</key><string>lantronix-network-tool</string>
  <key>CFBundleIconFile</key><string>lantronix-network-tool</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>LSMinimumSystemVersion</key><string>12.0</string>
  <key>NSHighResolutionCapable</key><true/>
  <key>NSLocalNetworkUsageDescription</key><string>Finds Lantronix devices on the local network.</string>
  <key>LSApplicationCategoryType</key><string>public.app-category.utilities</string>
</dict>
</plist>
PLIST
codesign --force --deep --sign - "$app"
(cd "$work" && ditto -c -k --keepParent "Lantronix Network Tool.app" "$dist/lantronix-network-tool-$version-macos-$arch.zip")

# command line tool
dotnet publish "$root/src/LantronixScan" -c Release -r "osx-$arch" --self-contained \
    -p:PublishSingleFile=true -p:DebugType=none -p:Version="$version" -o "$work/cli"
codesign --force --sign - "$work/cli/lantronix-scan"
tar -czf "$dist/lantronix-scan-$version-macos-$arch.tar.gz" -C "$work/cli" lantronix-scan
ls -lh "$dist"
