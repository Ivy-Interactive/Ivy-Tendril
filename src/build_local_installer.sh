#!/bin/bash
set -e

# Directories
REPO_DIR="/Users/rorychatt/git/ivy/Ivy-Tendril"
PUBLISH_DIR="$REPO_DIR/src/publish/desktop/osx-arm64"
APP_DIR="$REPO_DIR/src/publish/desktop/Ivy Tendril.app"
RELEASES_DIR="$REPO_DIR/src/releases"

echo "=== 1. Publishing Ivy.Tendril for osx-arm64 ==="
cd "$REPO_DIR"
dotnet publish src/Ivy.Tendril/Ivy.Tendril.csproj \
  -c Release \
  -r osx-arm64 \
  -o "$PUBLISH_DIR" \
  -p:PublishSingleFile=true \
  -p:ReadyToRun=false \
  -p:Version=1.0.99 \
  --self-contained true

echo "=== 2. Generating Certificates ==="
mkdir -p "$PUBLISH_DIR/certs"
chmod +x "$PUBLISH_DIR/Ivy.Tendril"
"$PUBLISH_DIR/Ivy.Tendril" generate-certs "$PUBLISH_DIR/certs"

echo "=== 3. Creating .app Bundle Structure ==="
rm -rf "$APP_DIR"
mkdir -p "$APP_DIR/Contents/MacOS"
mkdir -p "$APP_DIR/Contents/Resources"

cp -R "$PUBLISH_DIR"/* "$APP_DIR/Contents/MacOS/"

if [ -d "$APP_DIR/Contents/MacOS/certs" ]; then
  mv "$APP_DIR/Contents/MacOS/certs" "$APP_DIR/Contents/Resources/"
fi
if [ -f "$APP_DIR/Contents/MacOS/example.config.yaml" ]; then
  mv "$APP_DIR/Contents/MacOS/example.config.yaml" "$APP_DIR/Contents/Resources/"
fi

# Copy icon
cp src/Ivy.Tendril/Assets/icon.icns "$APP_DIR/Contents/Resources/icon.icns"

# Info.plist
cat << 'EOF' > "$APP_DIR/Contents/Info.plist"
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleDevelopmentRegion</key>
    <string>en</string>
    <key>CFBundleDisplayName</key>
    <string>Ivy Tendril</string>
    <key>CFBundleExecutable</key>
    <string>Ivy.Tendril</string>
    <key>CFBundleIdentifier</key>
    <string>com.ivy.tendril</string>
    <key>CFBundleInfoDictionaryVersion</key>
    <string>6.0</string>
    <key>CFBundleName</key>
    <string>Ivy Tendril</string>
    <key>CFBundlePackageType</key>
    <string>APPL</string>
    <key>CFBundleShortVersionString</key>
    <string>1.0.99</string>
    <key>CFBundleVersion</key>
    <string>1.0.99</string>
    <key>CFBundleIconFile</key>
    <string>icon.icns</string>
    <key>LSMinimumSystemVersion</key>
    <string>10.15</string>
    <key>NSHighResolutionCapable</key>
    <true/>
</dict>
</plist>
EOF

# Clean debugging files
find "$APP_DIR" -name "*.pdb" -delete
find "$APP_DIR/Contents/MacOS" -name "*.xml" -delete

echo "=== 4. Packing Installer with Velopack ==="
rm -rf "$RELEASES_DIR"
mkdir -p "$RELEASES_DIR"

vpk pack \
  --packId IvyTendril \
  --packTitle "Ivy Tendril" \
  --packVersion 1.0.99 \
  --packDir "$APP_DIR" \
  --mainExe Ivy.Tendril \
  --outputDir "$RELEASES_DIR" \
  --icon src/Ivy.Tendril/Assets/icon.icns \
  --noPortable \
  --bundleId com.ivy.tendril \
  --channel osx-arm64

echo "=== 5. Injecting Postinstall Script into macOS PKG ==="
# Find the generated pkg
PKG_PATH=$(find "$RELEASES_DIR" -name "*.pkg")
if [ -z "$PKG_PATH" ]; then
  echo "Error: No .pkg package found in releases!"
  exit 1
fi

pkgutil --expand "$PKG_PATH" expanded-pkg
mkdir -p expanded-pkg/1.pkg/Scripts

cat << 'EOF' > expanded-pkg/1.pkg/Scripts/postinstall
#!/bin/sh
rm -rf /tmp/velopack/IvyTendril
sudo -u "$USER" rm -rf ~/Library/Caches/velopack/IvyTendril
sudo -u "$USER" env VELOPACK_FIRSTRUN=1 open "$2/Ivy Tendril.app/"

# Path to the installed app certificate
CERT_PATH="$3/Applications/Ivy Tendril.app/Contents/Resources/certs/localhost.crt"
if [ -f "$CERT_PATH" ]; then
  echo "Trusting Ivy Tendril localhost certificate system-wide..."
  security add-trusted-cert -d -r trustRoot -k /Library/Keychains/System.keychain "$CERT_PATH"
fi
exit 0
EOF
chmod +x expanded-pkg/1.pkg/Scripts/postinstall

pkgutil --flatten expanded-pkg "$PKG_PATH"
rm -rf expanded-pkg

echo "=== Build Complete! ==="
echo "Local installer package created at: $PKG_PATH"
