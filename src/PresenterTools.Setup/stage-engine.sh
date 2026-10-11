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

# Every MSYS2 package a staged binary comes from, with its license files and the four GNU license texts.
PACKAGES=$(cd "$OUT" && find . -name '*.dll' -o -name 'gst-plugin-scanner.exe' | sed 's|^\./||' \
  | while read -r f; do case "$f" in */*) echo "$PREFIX/$f" ;; *) echo "$PREFIX/bin/$f" ;; esac; done \
  | xargs pacman -Qqo | sort -u)
mkdir -p "$OUT/licenses"
for p in $PACKAGES; do
  pacman -Qlq "$p" | { grep "^$PREFIX/share/licenses/.*[^/]$" || true; } | while read -r f; do
    rel="${f#$PREFIX/share/licenses/}"
    mkdir -p "$OUT/licenses/$(dirname "$rel")"
    cp "$f" "$OUT/licenses/$rel"
  done
done
cp "$PREFIX/share/licenses/xz/COPYING.GPLv2" "$OUT/licenses/GPL-2.0.txt"
cp "$PREFIX/share/licenses/xz/COPYING.GPLv3" "$OUT/licenses/GPL-3.0.txt"
cp "$PREFIX/share/licenses/xz/COPYING.LGPLv2.1" "$OUT/licenses/LGPL-2.1.txt"
cp "$PREFIX/share/licenses/libunistring/LICENSE.LIB" "$OUT/licenses/LGPL-3.0.txt"

cp "$UXPLAY/LICENSE" "$OUT/UxPlay-LICENSE.txt"
{
  echo "uxplay.exe is UxPlay, licensed under the GNU GPL v3 (UxPlay-LICENSE.txt)."
  echo "Source: https://github.com/FDH2/UxPlay commit $(git -C "$UXPLAY" rev-parse HEAD)"
  echo
  echo "GStreamer and the other DLLs in this folder come from the MSYS2 UCRT64 packages below."
  echo "Their sources, by package name and version: https://repo.msys2.org/mingw/sources/"
  echo "License texts: licenses/GPL-2.0.txt, GPL-3.0.txt, LGPL-2.1.txt, LGPL-3.0.txt and licenses/<package>/."
  echo
  for p in $PACKAGES; do
    echo "$(pacman -Q "$p") ($(pacman -Qi "$p" | sed -n 's/^Licenses *: //p'))"
  done
} > "$OUT/THIRD-PARTY.txt"

echo "staged: $(find "$OUT" -type f | wc -l) files, $(du -sh "$OUT" | cut -f1)"
