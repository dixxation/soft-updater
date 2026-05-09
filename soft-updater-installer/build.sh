#!/usr/bin/env bash
# Билдит бинарники для Windows и Linux, кладёт в ../SoftUpdater.Lib/Resources/
set -e

OUT="../../SoftUpdaterLib/Resources"
mkdir -p "$OUT"

echo "==> Building Windows x64..."
cargo build --release --target x86_64-pc-windows-gnu
cp target/x86_64-pc-windows-gnu/release/soft-updater-installer.exe "$OUT/soft-updater-installer-win-x64.exe"

echo "==> Building Linux x64..."
cargo build --release --target x86_64-unknown-linux-gnu
cp target/x86_64-unknown-linux-gnu/release/soft-updater-installer "$OUT/soft-updater-installer-linux-x64"

echo "==> Done. Binaries in $OUT"
ls -lh "$OUT"