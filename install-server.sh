#!/bin/sh
set -eu

archive_url=${1:?usage: install-server.sh ARCHIVE_URL ARCHIVE_SHA256}
archive_digest=${2:?usage: install-server.sh ARCHIVE_URL ARCHIVE_SHA256}
plugin_dir=${BETTERSHUFFLE_PLUGIN_DIR:-/config/plugins}
version=0.1.2
plugin_digest=744931de00a469ce9574ca0a211bde58a23ef1d46b6684cc8b39b0c2d8248f51
harmony_digest=2b0496067bda368ff35c383d80421401c57a3acc091dcb3e5a8f15636104987f
archive=/tmp/bettershuffle-${version}.$$.zip
stage=$plugin_dir/.bettershuffle-stage.$$
plugin_temp=$plugin_dir/.Emby.Plugins.BetterShuffle.dll.new.$$
harmony_temp=$plugin_dir/.0Harmony.dll.new.$$
marker_temp=$plugin_dir/.bettershuffle-version.new.$$
log_path=/config/logs/bettershuffle-install.txt

exec >> "$log_path" 2>&1
printf '\nBetterShuffle installer started: '
date -u '+%Y-%m-%dT%H:%M:%SZ'

cleanup() {
    rm -f "$archive" "$plugin_temp" "$harmony_temp" "$marker_temp"
    case "$stage" in
        "$plugin_dir"/.bettershuffle-stage.*) rm -rf "$stage" ;;
    esac
}
trap cleanup EXIT HUP INT TERM

fetch() {
    if command -v curl >/dev/null 2>&1; then
        curl -fsSL "$1" -o "$2"
    elif command -v wget >/dev/null 2>&1; then
        wget -qO "$2" "$1"
    else
        echo "BetterShuffle installer: curl or wget is required" >&2
        exit 127
    fi
}

digest() {
    if command -v sha256sum >/dev/null 2>&1; then
        sha256sum "$1" | awk '{print $1}'
    elif [ -x "$plugin_dir/busybox" ]; then
        "$plugin_dir/busybox" sha256sum "$1" | awk '{print $1}'
    else
        echo "BetterShuffle installer: sha256sum is required" >&2
        exit 127
    fi
}

require_digest() {
    actual=$(digest "$2")
    if [ "$actual" != "$1" ]; then
        echo "BetterShuffle installer: SHA-256 mismatch for $2" >&2
        exit 1
    fi
}

mkdir -p "$stage"
fetch "$archive_url" "$archive"
echo "BetterShuffle installer: archive downloaded"
require_digest "$archive_digest" "$archive"
echo "BetterShuffle installer: archive checksum verified"

if command -v unzip >/dev/null 2>&1; then
    echo "BetterShuffle installer: extracting with unzip"
    unzip -t "$archive" >/dev/null
    unzip -q "$archive" -d "$stage"
elif [ -x "$plugin_dir/busybox" ]; then
    echo "BetterShuffle installer: extracting with managed BusyBox"
    "$plugin_dir/busybox" unzip -t "$archive" >/dev/null
    "$plugin_dir/busybox" unzip -q "$archive" -d "$stage"
else
    echo "BetterShuffle installer: unzip or the managed BusyBox binary is required" >&2
    exit 127
fi

echo "BetterShuffle installer: archive extracted"
require_digest "$plugin_digest" "$stage/Emby.Plugins.BetterShuffle.dll"
require_digest "$harmony_digest" "$stage/0Harmony.dll"
echo "BetterShuffle installer: component checksums verified"

if [ -f "$plugin_dir/0Harmony.dll" ]; then
    echo "BetterShuffle installer: validating existing Harmony dependency"
    require_digest "$harmony_digest" "$plugin_dir/0Harmony.dll"
fi

cp "$stage/0Harmony.dll" "$harmony_temp"
cp "$stage/Emby.Plugins.BetterShuffle.dll" "$plugin_temp"
chmod 644 "$harmony_temp" "$plugin_temp"
mv "$harmony_temp" "$plugin_dir/0Harmony.dll"
mv "$plugin_temp" "$plugin_dir/Emby.Plugins.BetterShuffle.dll"
printf '%s\n' "$version" > "$marker_temp"
mv "$marker_temp" "$plugin_dir/.bettershuffle-version"

echo "BetterShuffle installer: installed $version; restart Emby to load it"
