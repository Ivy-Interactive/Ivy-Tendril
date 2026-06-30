#!/bin/bash
set -e

# Change directory to the repository root directory
cd "$(dirname "$0")/.."

VERSION="1.0.52-local-test"
RID="osx-arm64"
PUBLISH_DIR="./publish/desktop/$RID"

echo "=== Cleaning previous builds ==="
rm -rf ./publish
rm -rf ./releases

echo "=== Publishing Tendril App ==="
dotnet publish src/Ivy.Tendril/Ivy.Tendril.csproj \
  --configuration Release \
  --runtime $RID \
  --output $PUBLISH_DIR \
  -p:PublishSingleFile=true \
  -p:ReadyToRun=false \
  -p:Version=$VERSION \
  -p:Publishing=true \
  --self-contained true

echo "=== Bundling PowerShell ==="
PWSH_VERSION="7.4.2"
TARGET_PWSH_DIR="$PUBLISH_DIR/PowerShell"
mkdir -p "$TARGET_PWSH_DIR"
URL_PWSH="https://github.com/PowerShell/PowerShell/releases/download/v$PWSH_VERSION/powershell-$PWSH_VERSION-osx-arm64.tar.gz"
echo "Downloading PowerShell..."
curl -L -o pwsh.tar.gz "$URL_PWSH"
tar -xzf pwsh.tar.gz -C "$TARGET_PWSH_DIR"
rm pwsh.tar.gz
chmod +x "$TARGET_PWSH_DIR/pwsh"

echo "=== Bundling .NET SDK ==="
DOTNET_VERSION="10.0.100"
TARGET_DOTNET_DIR="$PUBLISH_DIR/dotnet"
mkdir -p "$TARGET_DOTNET_DIR"
URL_DOTNET="https://dotnetcli.azureedge.net/dotnet/Sdk/$DOTNET_VERSION/dotnet-sdk-$DOTNET_VERSION-osx-arm64.tar.gz"
echo "Downloading .NET SDK..."
curl -L -o dotnet.tar.gz "$URL_DOTNET"
tar -xzf dotnet.tar.gz -C "$TARGET_DOTNET_DIR"
rm dotnet.tar.gz
chmod +x "$TARGET_DOTNET_DIR/dotnet"

echo "=== Packing with Velopack ==="
vpk pack \
  --packId IvyTendril \
  --packVersion $VERSION \
  --packDir $PUBLISH_DIR \
  --mainExe Ivy.Tendril \
  --outputDir ./releases \
  --icon src/Ivy.Tendril/Assets/icon.icns \
  --noPortable \
  --delta None \
  --bundleId com.ivy.tendril \
  --channel $RID

echo "=== Renaming Installer Setup Package ==="
mv releases/IvyTendril-${RID}-Setup.pkg releases/IvyTendril-${VERSION}-${RID}.pkg

echo "=== Local Build Complete ==="
echo "Installer is available at: $(pwd)/releases/IvyTendril-${VERSION}-${RID}.pkg"
