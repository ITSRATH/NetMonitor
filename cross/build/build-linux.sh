#!/usr/bin/env bash
# Builds dist/NetMonitor-<version>-linux-<arch>.tar.gz
#   cross/build/build-linux.sh [linux-x64|linux-arm64]
set -euo pipefail
rid=${1:-linux-x64}
cross=$(cd "$(dirname "$0")/.." && pwd)
root=$(cd "$cross/.." && pwd)
version=$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$cross/NetMonitor.Cross.csproj")
arch=${rid#linux-}
name="NetMonitor-$version-linux-$arch"
stage="$root/dist/$name"

rm -rf "$stage" && mkdir -p "$stage"
dotnet publish "$cross/NetMonitor.Cross.csproj" -c Release -r "$rid" -o "$stage" -nologo
rm -f "$stage"/*.pdb "$stage"/*.dbg
cp "$cross/packaging/linux/install.sh" "$cross/packaging/linux/uninstall.sh" "$stage/"
cp "$cross/packaging/icons/netmonitor-256.png" "$cross/packaging/icons/netmonitor-48.png" "$stage/"
cp "$root/LICENSE" "$stage/"
cat > "$stage/README.txt" <<EOF
NetMonitor $version for Linux ($arch)

Install (current user, no root needed):   ./install.sh
Run without installing:                   ./NetMonitor
Uninstall:                                ~/.local/lib/netmonitor/uninstall.sh
Measurement data:                         ~/.local/share/NetMonitor
EOF
chmod +x "$stage/NetMonitor" "$stage/install.sh" "$stage/uninstall.sh"
tar -C "$root/dist" -czf "$root/dist/$name.tar.gz" "$name"
rm -rf "$stage"
echo "Release package: dist/$name.tar.gz"
