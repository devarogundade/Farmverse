#!/usr/bin/env bash
# Builds every contract to chain/res/<name>.wasm with cargo-near (ABI embedded).
# Linux/macOS/WSL. For a verifiable mainnet build use `cargo near build reproducible-wasm` (needs Docker).
set -euo pipefail
cd "$(dirname "$0")/.."
mkdir -p res
for c in farm_token items market; do
  (cd "contracts/$c" && cargo near build non-reproducible-wasm --locked --out-dir ../../res)
done
ls -l res/*.wasm
