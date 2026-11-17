#!/bin/bash
# Usage: ./publish.sh [version]
set -euo pipefail

VERSION="${1:-0.0.0-dev}"

for rid in linux-x64 win-x64; do
    rm -rf "publish/$rid"
    dotnet publish DevTools/DevTools.csproj -c Release -r "$rid" --self-contained false \
        -p:PublishSingleFile=true -p:DebugType=none -p:Version="$VERSION" -o "publish/$rid"
done
