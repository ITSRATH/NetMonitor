#!/usr/bin/env bash
# Builds dist/NetMonitor-<version>-macos-<arch>.dmg (must run on macOS: iconutil, codesign, hdiutil)
#   cross/build/build-macos.sh [osx-arm64|osx-x64]
set -euo pipefail
rid=${1:-osx-arm64}
cross=$(cd "$(dirname "$0")/.." && pwd)
root=$(cd "$cross/.." && pwd)
version=$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$cross/NetMonitor.Cross.csproj")
case "$rid" in osx-arm64) arch=apple-silicon ;; *) arch=intel ;; esac
name="NetMonitor-$version-macos-$arch"
work="$root/dist/$name"
app="$work/NetMonitor.app"

rm -rf "$work" && mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources"
dotnet publish "$cross/NetMonitor.Cross.csproj" -c Release -r "$rid" -o "$work/publish" -nologo
cp "$work/publish/NetMonitor" "$app/Contents/MacOS/NetMonitor"
chmod +x "$app/Contents/MacOS/NetMonitor"
sed "s/__VERSION__/$version/g" "$cross/packaging/macos/Info.plist" > "$app/Contents/Info.plist"

# App-Symbol aus den PNGs im Repository
iconset="$work/NetMonitor.iconset"
mkdir -p "$iconset"
icons="$cross/packaging/icons"
for s in 16 32 128 256 512; do
  cp "$icons/netmonitor-$s.png" "$iconset/icon_${s}x${s}.png"
  cp "$icons/netmonitor-$((s * 2)).png" "$iconset/icon_${s}x${s}@2x.png"
done
iconutil -c icns "$iconset" -o "$app/Contents/Resources/NetMonitor.icns"

# Ad-hoc-Signatur (Pflicht auf Apple Silicon); keine Apple-Notarisierung
codesign --force --deep --sign - "$app"

# DMG mit Verknüpfung zum Programme-Ordner
dmgroot="$work/dmg"
mkdir -p "$dmgroot"
cp -R "$app" "$dmgroot/"
ln -s /Applications "$dmgroot/Applications"
rm -f "$root/dist/$name.dmg"
hdiutil create -volname "NetMonitor $version" -srcfolder "$dmgroot" -ov -format UDZO "$root/dist/$name.dmg"
rm -rf "$work"
echo "Release package: dist/$name.dmg"
