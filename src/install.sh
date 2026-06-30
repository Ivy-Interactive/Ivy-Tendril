#!/bin/sh

# Ivy-Tendril macOS & Linux Standalone Installer
# This script downloads and installs the standalone version of Ivy-Tendril.

set -e

RED='\033[0;31m'
GREEN='\033[0;32m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

printf "%b\\n" "${BLUE}=== Ivy-Tendril Installer (macOS/Linux) ===${NC}"

OS_TYPE="unknown"
UNAME_S=$(uname -s)
if [ "$UNAME_S" = "Darwin" ]; then
    OS_TYPE="macos"
elif [ "$UNAME_S" = "Linux" ]; then
    OS_TYPE="linux"
else
    printf "%b\\n" "${RED}Error: Unsupported operating system: $UNAME_S${NC}"
    exit 1
fi

ARCH=$(uname -m)
if [ "$ARCH" = "arm64" ] || [ "$ARCH" = "aarch64" ]; then
    printf "%b\\n" "Detected Architecture: ARM64"
    ARCH_TYPE="arm64"
elif [ "$ARCH" = "x86_64" ] || [ "$ARCH" = "amd64" ]; then
    printf "%b\\n" "Detected Architecture: x64"
    ARCH_TYPE="x64"
else
    printf "%b\\n" "${RED}Error: Unsupported architecture: $ARCH${NC}"
    exit 1
fi

printf "%b\\n" "\n${BLUE}Checking GitHub CLI (gh)...${NC}"
if ! command -v gh >/dev/null 2>&1; then
    printf "%b\\n" "${RED}Error: GitHub CLI (gh) is not installed.${NC}"
    printf "%b\\n" "Please install the latest version of gh from https://cli.github.com/ and try again.${NC}"
    exit 1
fi

LATEST_GH_URL=$(curl -sSf -L -o /dev/null -w %{url_effective} https://github.com/cli/cli/releases/latest)
LATEST_GH_TAG=${LATEST_GH_URL##*/}
LATEST_GH_VERSION=${LATEST_GH_TAG#v}

if [ -z "$LATEST_GH_VERSION" ] || [ "$LATEST_GH_VERSION" = "latest" ]; then
    LATEST_GH_VERSION=$(curl -sSf https://api.github.com/repos/cli/cli/releases/latest | grep tag_name | cut -d'"' -f4 | sed 's/^v//')
fi

if [ -z "$LATEST_GH_VERSION" ]; then
    printf "%b\\n" "${RED}Error: Failed to fetch the latest gh CLI version from GitHub.${NC}"
    exit 1
fi

GH_VERSION=$(gh --version | head -n 1 | awk '{print $3}')
printf "%b\\n" "Installed gh version: ${GREEN}${GH_VERSION}${NC}"
printf "%b\\n" "Latest gh version:    ${GREEN}${LATEST_GH_VERSION}${NC}"

if [ "$GH_VERSION" != "$LATEST_GH_VERSION" ]; then
    printf "%b\\n" "${RED}Error: You do not have the latest GitHub CLI (gh) version.${NC}"
    printf "%b\\n" "Please upgrade gh to version ${LATEST_GH_VERSION} and try again.${NC}"
    exit 1
fi
printf "%b\\n" "${GREEN}✓ GitHub CLI (gh) is up to date.${NC}"

printf "%b\\n" "\n${BLUE}Step 1: Fetching latest release info...${NC}"
LATEST_RELEASE_URL=$(curl -sSf -L -o /dev/null -w %{url_effective} https://github.com/Ivy-Interactive/Ivy-Tendril/releases/latest)
LATEST_TAG=${LATEST_RELEASE_URL##*/}
VERSION=${LATEST_TAG#v}

if [ -z "$VERSION" ]; then
    printf "%b\\n" "${RED}Error: Failed to fetch the latest release version.${NC}"
    exit 1
fi

printf "%b\\n" "Latest version found: ${GREEN}${VERSION}${NC}"

TEMP_DIR=$(mktemp -d)
cd "$TEMP_DIR"

if [ "$OS_TYPE" = "macos" ]; then
    FILE_NAME="IvyTendril-${VERSION}-osx-${ARCH_TYPE}.pkg"
    DOWNLOAD_URL="https://github.com/Ivy-Interactive/Ivy-Tendril/releases/download/${LATEST_TAG}/${FILE_NAME}"
    
    printf "%b\\n" "\n${BLUE}Step 2: Downloading macOS Installer Package...${NC}"
    printf "%b\\n" "Downloading from: ${DOWNLOAD_URL}"
    curl -sSL -o "$FILE_NAME" "$DOWNLOAD_URL"
    
    printf "%b\\n" "\n${BLUE}Step 3: Installing Ivy-Tendril (requires sudo)...${NC}"
    sudo installer -pkg "$FILE_NAME" -target /
    
    printf "%b\\n" "\n${GREEN}✓ Ivy-Tendril installed successfully!${NC}"
else
    FILE_NAME="IvyTendril-${VERSION}-linux-${ARCH_TYPE}.AppImage"
    DOWNLOAD_URL="https://github.com/Ivy-Interactive/Ivy-Tendril/releases/download/${LATEST_TAG}/${FILE_NAME}"
    
    printf "%b\\n" "\n${BLUE}Step 2: Downloading Linux AppImage...${NC}"
    printf "%b\\n" "Downloading from: ${DOWNLOAD_URL}"
    curl -sSL -o "$FILE_NAME" "$DOWNLOAD_URL"
    
    printf "%b\\n" "\n${BLUE}Step 3: Installing to /usr/local/bin/tendril (requires sudo)...${NC}"
    sudo mv "$FILE_NAME" /usr/local/bin/tendril
    sudo chmod +x /usr/local/bin/tendril
    
    printf "%b\\n" "\n${GREEN}✓ Ivy-Tendril installed successfully to /usr/local/bin/tendril${NC}"
fi

# Clean up
rm -rf "$TEMP_DIR"

printf "%b\\n" "\n${GREEN}=== Talk to you soon! ===${NC}"
printf "%b\\n" "You can now run Ivy-Tendril by typing: ${BLUE}tendril${NC}"