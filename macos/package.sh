#!/usr/bin/env bash
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
rid="${1:-osx-arm64}"
case "$rid" in
  osx-arm64|osx-x64) ;;
  *) echo "Choose osx-arm64 or osx-x64." >&2; exit 1 ;;
esac
out="${2:-$here/dist/$rid}"
mkdir -p "$out"
app="$out/Neo Nibbler.app"
pub="$(mktemp -d)"
trap 'rm -rf "$pub"' EXIT
"${NEO_DOTNET:-dotnet}" publish "$here/NeoNibbler.csproj" -c Release -r "$rid" --self-contained true -p:DebugType=none -o "$pub"
mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources"
cp -R "$pub/." "$app/Contents/MacOS/"
cp "$here/Info.plist" "$app/Contents/"
cp "$here/NeoNibbler.icns" "$app/Contents/Resources/"
if [[ "$(uname -s)" == Darwin ]]; then
  codesign --force --deep --sign - "$app"
  ditto -c -k --keepParent "$app" "$out/NeoNibbler-$rid.zip"
else
  (cd "$out" && zip -q -r "NeoNibbler-$rid.zip" "Neo Nibbler.app")
fi
