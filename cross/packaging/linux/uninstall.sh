#!/bin/sh
# NetMonitor – Deinstallation. Messdaten bleiben erhalten, außer mit --purge.
#   ~/.local/lib/netmonitor/uninstall.sh [--purge]
data=${XDG_DATA_HOME:-$HOME/.local/share}
rm -f "$HOME/.local/bin/netmonitor"
rm -f "$data/applications/netmonitor.desktop"
rm -f "$data/icons/hicolor/256x256/apps/netmonitor.png" "$data/icons/hicolor/48x48/apps/netmonitor.png"
rm -rf "$HOME/.local/lib/netmonitor"
command -v update-desktop-database >/dev/null 2>&1 && update-desktop-database "$data/applications" >/dev/null 2>&1 || true
if [ "$1" = "--purge" ]; then
  rm -rf "$data/NetMonitor"
  echo "NetMonitor and all measurement data removed."
else
  echo "NetMonitor removed. Your measurement data is kept in $data/NetMonitor (delete it with --purge)."
fi
