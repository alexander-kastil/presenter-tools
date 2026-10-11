#!/usr/bin/env bash
# Builds UxPlay (third_party/UxPlay) and stages uxplay.exe, the GStreamer plugins it needs and every
# MSYS2 UCRT64 DLL they load into <out>. Run inside an MSYS2 UCRT64 shell.
# Usage: stage-engine.sh <out-dir>
set -euo pipefail

OUT="$1"
HERE="$(cd "$(dirname "$0")" && pwd)"
UXPLAY="$(cd "$HERE/../UxPlay" && pwd)"
PREFIX=/ucrt64

PLUGINS="coreelements app playback typefindfunctions autodetect videoconvertscale videoparsersbad libav
d3d11 d3d12 audioconvert audioresample audioparsers volume wasapi wasapi2 directsound jpeg imagefreeze
debugutilsbad hls adaptivedemux2 soup isomp4 mpegtsdemux id3demux videorate level rtp pango videofilter"

if [ ! -f "$UXPLAY/build/uxplay.exe" ]; then
  cmake -S "$UXPLAY" -B "$UXPLAY/build" -G Ninja
  ninja -C "$UXPLAY/build"
fi

rm -rf "$OUT"
mkdir -p "$OUT/lib/gstreamer-1.0" "$OUT/libexec/gstreamer-1.0"
cp "$UXPLAY/build/uxplay.exe" "$OUT/"
cp "$PREFIX/libexec/gstreamer-1.0/gst-plugin-scanner.exe" "$OUT/libexec/gstreamer-1.0/"
for p in $PLUGINS; do
  cp "$PREFIX/lib/gstreamer-1.0/libgst$p.dll" "$OUT/lib/gstreamer-1.0/"
done

# DLL closure: every DLL from $PREFIX/bin that any staged binary loads, directly or indirectly.
find "$OUT" -name '*.exe' -o -name '*.dll' | while read -r f; do ldd "$f"; done \
  | awk '{print $3}' | { grep -i "^$PREFIX/bin/" || true; } | sort -u | while read -r dll; do
  cp -n "$dll" "$OUT/"
done
# Repeat until no new DLLs appear (ldd already resolves transitively; this guards against missed entries).
while :; do
  before=$(ls "$OUT"/*.dll | wc -l)
  for f in "$OUT"/*.dll; do ldd "$f"; done | awk '{print $3}' | { grep -i "^$PREFIX/bin/" || true; } | sort -u \
    | while read -r dll; do cp -n "$dll" "$OUT/"; done
  [ "$(ls "$OUT"/*.dll | wc -l)" -eq "$before" ] && break
done

cp "$UXPLAY/LICENSE" "$OUT/UxPlay-LICENSE.txt"
{
  echo "uxplay.exe is UxPlay, licensed under the GNU GPL v3 (UxPlay-LICENSE.txt)."
  echo "Source: https://github.com/FDH2/UxPlay commit $(git -C "$UXPLAY" rev-parse HEAD)"
  echo "GStreamer and the other DLLs in this folder come from MSYS2 UCRT64 packages (https://packages.msys2.org)."
} > "$OUT/THIRD-PARTY.txt"

echo "staged: $(find "$OUT" -type f | wc -l) files, $(du -sh "$OUT" | cut -f1)"
