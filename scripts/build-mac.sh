#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "$0")/.." && pwd)"
PROJECT_DIR="$ROOT_DIR/src/willGoMac"
BUILD_DIR="$ROOT_DIR/dist/mac"
APP_DIR="$BUILD_DIR/willGo.app"
CONTENTS_DIR="$APP_DIR/Contents"
MACOS_DIR="$CONTENTS_DIR/MacOS"
RESOURCES_DIR="$CONTENTS_DIR/Resources"

mkdir -p "$BUILD_DIR"
rm -rf "$APP_DIR"

cd "$PROJECT_DIR"
swift build -c release

EXECUTABLE_PATH="$(swift build -c release --show-bin-path)/willGo"

mkdir -p "$MACOS_DIR" "$RESOURCES_DIR"
cp "$EXECUTABLE_PATH" "$MACOS_DIR/willGo"
cp "$PROJECT_DIR/Resources/Info.plist" "$CONTENTS_DIR/Info.plist"

cat > "$RESOURCES_DIR/README.txt" <<'EOF'
willGo macOS local app bundle.

This bundle is unsigned. If Gatekeeper blocks it, sign/notarize it for distribution
or remove quarantine only for your own local test copy.
EOF

echo "Built: $APP_DIR"
