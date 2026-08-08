#!/usr/bin/env bash
set -e

REPO_URL="https://github.com/Breakerchap/MonoGame-MedivalChess.git"
BRANCH="master"
REPO_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/MonoGame-MedivalChess"
SOLUTION="CrownAndSiege.sln"
PROJECT="MedivalChess.csproj"

echo "========================================"
echo "      Crown and Siege Launcher"
echo "========================================"
echo

if ! command -v git >/dev/null 2>&1; then
  echo "ERROR: Git is not installed or is not in PATH."
  exit 1
fi

if ! command -v dotnet >/dev/null 2>&1; then
  echo "ERROR: .NET SDK is not installed or is not in PATH."
  exit 1
fi

ARCH="$(uname -m)"

if [[ "$ARCH" != "x86_64" && "$ARCH" != "amd64" ]]; then
  echo "WARNING: This script is intended for Linux x86_64."
  echo "Detected architecture: $ARCH"
  echo
fi

if [[ ! -d "$REPO_DIR/.git" ]]; then
  echo "Cloning repository..."
  git clone --branch "$BRANCH" "$REPO_URL" "$REPO_DIR"
fi

cd "$REPO_DIR"

echo
echo "Fetching latest changes..."
git fetch origin

echo
echo "Switching to $BRANCH..."
git switch "$BRANCH"

echo
echo "Pulling latest changes..."
git pull --ff-only origin "$BRANCH"

echo
echo "Restoring dependencies..."
dotnet restore "$SOLUTION"

echo
echo "Building..."
dotnet build "$SOLUTION" \
  --configuration Debug \
  --no-restore

echo
echo "========================================"
echo "           Starting game..."
echo "========================================"
echo

dotnet run \
  --project "$PROJECT" \
  --configuration Debug \
  --no-build