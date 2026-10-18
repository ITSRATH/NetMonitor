#!/bin/sh
# NetMonitor – Installation für den aktuellen Benutzer (keine Root-Rechte nötig).
#   ./install.sh
# Programm:      ~/.local/lib/netmonitor/NetMonitor
# Startbefehl:   ~/.local/bin/netmonitor
# Menüeintrag:   ~/.local/share/applications/netmonitor.desktop
# Messdaten liegen getrennt davon in ~/.local/share/NetMonitor und bleiben bei Updates erhalten.
set -e
here=$(cd "$(dirname "$0")" && pwd)
data=${XDG_DATA_HOME:-$HOME/.local/share}
lib=$HOME/.local/lib/netmonitor
bin=$HOME/.local/bin

if [ ! -f "$here/NetMonitor" ]; then
  echo "NetMonitor binary not found next to install.sh" >&2
  exit 1
fi

mkdir -p "$lib" "$bin" "$data/applications" "$data/icons/hicolor/256x256/apps" "$data/icons/hicolor/48x48/apps"
install -m 755 "$here/NetMonitor" "$lib/NetMonitor"
install -m 755 "$here/uninstall.sh" "$lib/uninstall.sh"
install -m 644 "$here/netmonitor-256.png" "$data/icons/hicolor/256x256/apps/netmonitor.png"
install -m 644 "$here/netmonitor-48.png" "$data/icons/hicolor/48x48/apps/netmonitor.png"
ln -sf "$lib/NetMonitor" "$bin/netmonitor"

cat > "$data/applications/netmonitor.desktop" <<EOF
[Desktop Entry]
Type=Application
Name=NetMonitor
GenericName=Ping & Packet Loss Monitor
GenericName[de]=Ping- und Paketverlust-Monitor
Comment=Continuous ping, packet loss analysis and speed tests
Comment[de]=Dauer-Ping, Paketverlust-Analyse und Speedtests
Exec=$lib/NetMonitor
Icon=netmonitor
Terminal=false
Categories=Network;Monitor;Utility;
Keywords=ping;packet loss;latency;speedtest;network;
StartupWMClass=NetMonitor
EOF

command -v update-desktop-database >/dev/null 2>&1 && update-desktop-database "$data/applications" >/dev/null 2>&1 || true
command -v gtk-update-icon-cache >/dev/null 2>&1 && gtk-update-icon-cache -q "$data/icons/hicolor" >/dev/null 2>&1 || true

# Grafik-Bibliotheken prüfen (auf Desktop-Systemen normalerweise vorhanden)
missing=""
for l in libX11.so.6 libICE.so.6 libSM.so.6 libfontconfig.so.1; do
  ldconfig -p 2>/dev/null | grep -q "$l" || missing="$missing $l"
done

echo "NetMonitor installed."
echo "  Start: application menu → NetMonitor, or run: netmonitor"
case ":$PATH:" in *":$bin:"*) ;; *) echo "  Note: $bin is not in your PATH – use the menu entry or $lib/NetMonitor" ;; esac
if [ -n "$missing" ]; then
  echo ""
  echo "  Missing system libraries:$missing"
  echo "  Debian/Ubuntu: sudo apt install libx11-6 libice6 libsm6 libfontconfig1"
  echo "  Fedora:        sudo dnf install libX11 libICE libSM fontconfig"
  echo "  Arch:          sudo pacman -S libx11 libice libsm fontconfig"
fi
echo "  Optional for Wi-Fi details: nmcli (NetworkManager) or iw."
