#!/usr/bin/env bash
# The web sites on Vercel (static, prebuilt here): the marketplace (with the Privy sign page), the landing site,
# the admin and the WebGL game. The API stays on the EC2 box (api.farmverse.world).
#
#   npx vercel login                          # once
#   bash deployment/vercel.sh                 # all four
#   bash deployment/vercel.sh market admin    # some
#
# Site URLs default to the farmverse.world domains on the Vercel projects (the API allows those origins); set
# PLAY_URL / MARKET_URL / SITE_URL / ADMIN_URL to change them. SOCIAL_LOGIN=off hides "Email or Google" in the game.
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
SCOPE="${VERCEL_SCOPE:-devarogundades-projects}"
API="${API_URL:-https://api.farmverse.world}"
PLAY_URL="${PLAY_URL:-https://play.farmverse.world}"
MARKET_URL="${MARKET_URL:-https://marketplace.farmverse.world}"
SITE_URL="${SITE_URL:-https://farmverse.world}"
ADMIN_URL="${ADMIN_URL:-https://admin.farmverse.world}"
SOCIAL_LOGIN="${SOCIAL_LOGIN:-on}"
STAGE="$(mktemp -d)"
trap 'rm -rf "$STAGE"' EXIT

ENVF="$ROOT/chain/deploy/testnet.env"
get() { sed -n "s/^$1=//p" "$ENVF" | tr -d '\r' | head -n1; }
# Everything set here, so a developer's .env.local (localhost) never leaks into a build.
export VITE_API_URL="$API/v1/market" VITE_GAME_API_URL="$API" VITE_WS_URL="${API/https:/wss:}/v1/market/live" \
  VITE_NEAR_NETWORK="$(get NEAR_NETWORK)" VITE_NEAR_RPC="${NEAR_RPC:-https://test.rpc.fastnear.com}" \
  VITE_ITEMS_CONTRACT="$(get NEAR_ITEMS_CONTRACT)" VITE_MARKET_CONTRACT="$(get NEAR_MARKET_CONTRACT)" \
  VITE_FARM_CONTRACT="$(get NEAR_FARM_CONTRACT)" VITE_PLAY_URL="$PLAY_URL" VITE_MARKET_URL="$MARKET_URL" \
  VITE_PRIVY_APP_ID="${VITE_PRIVY_APP_ID:-cmw52ee0601da0hjsddtcoykq}" \
  VITE_PRIVY_CLIENT_ID="${VITE_PRIVY_CLIENT_ID:-client-WY6fZRp6drWr76ZsvzH3nG3iP9jymvaZtw2FvkMf3sCCs}"

say() { printf '\n== %s\n' "$*"; }

# deploy <dir> <project>: production deploy of a prebuilt folder to its own project
deploy() {
  (cd "$1" && npx --yes vercel deploy --prod --yes --scope "$SCOPE" --name "$2" 2>&1 | tail -n 4)
}

vite_site() { # vite_site <web dir> <project> [api url]
  say "build $1"
  local out="$STAGE/$2"
  (cd "$ROOT/web/$1" && VITE_API_URL="${3:-$VITE_API_URL}" npx vite build --emptyOutDir --outDir "$out" --logLevel warn)
  cat >"$out/vercel.json" <<'JSON'
{
  "cleanUrls": false,
  "rewrites": [{ "source": "/(.*)", "destination": "/index.html" }],
  "headers": [
    { "source": "/assets/(.*)", "headers": [{ "key": "Cache-Control", "value": "public, max-age=31536000, immutable" }] }
  ]
}
JSON
  deploy "$out" "$2"
}

game() {
  say "game"
  local src="$ROOT/Builds/WebGL-testnet" out="$STAGE/farmverse-play"
  [[ -d "$src" ]] || { echo "no Builds/WebGL-testnet (Farmverse ▸ Web ▸ Build (testnet on farmverse.world))"; return 1; }
  cp -r "$src" "$out"
  # the page's own script and styles from the template, so page fixes ship without a Unity rebuild
  cp "$ROOT/Assets/WebGLTemplates/Farmverse/farmverse-page.js" "$ROOT/Assets/WebGLTemplates/Farmverse/farmverse-page.css" "$out/"
  # Vercel takes files up to 100 MB: a bigger data file goes up in parts the page joins (a smaller one as it is)
  node "$ROOT/deployment/webgl-split.mjs" "$out"
  # the install screen shows MB downloaded of the whole download
  sed -i "s/data-bytes=\"0\"/data-bytes=\"$(du -cb "$out"/Build/* | tail -n1 | cut -f1)\"/" "$out/index.html"
  # the page settings the game reads (no Unity rebuild needed)
  sed -i -e "s#apiBaseUrl: \"[^\"]*\"#apiBaseUrl: \"$API\"#" \
    -e "s#marketplaceUrl: \"[^\"]*\"#marketplaceUrl: \"$MARKET_URL\"#" \
    -e "s#siteUrl: \"[^\"]*\"#siteUrl: \"$SITE_URL\"#" \
    -e "s#socialLogin: \"[^\"]*\"#socialLogin: \"$SOCIAL_LOGIN\"#" "$out/index.html"
  # Unity's Brotli files need their encoding header (the loader can't decompress them itself); their names are
  # content hashes (FarmverseWebBuild), so they can be cached for good
  cat >"$out/vercel.json" <<'JSON'
{
  "headers": [
    { "source": "/Build/(.*)\\.br", "headers": [{ "key": "Content-Encoding", "value": "br" }, { "key": "Cache-Control", "value": "public, max-age=31536000, immutable" }] },
    { "source": "/Build/(.*)\\.js\\.br", "headers": [{ "key": "Content-Type", "value": "application/javascript" }] },
    { "source": "/Build/(.*)\\.wasm\\.br", "headers": [{ "key": "Content-Type", "value": "application/wasm" }] },
    { "source": "/Build/(.*)\\.data\\.br", "headers": [{ "key": "Content-Type", "value": "application/octet-stream" }] }
  ]
}
JSON
  deploy "$out" farmverse-play
}

sites=("$@")
[[ ${#sites[@]} -gt 0 ]] || sites=(market site admin play)
for s in "${sites[@]}"; do
  case "$s" in
    market) vite_site marketplace farmverse-market ;;
    site) vite_site landing farmverse-site ;;
    admin) vite_site admin farmverse-admin "$API" ;;
    play) game ;;
    *) echo "unknown site $s (market | site | admin | play)"; exit 1 ;;
  esac
done
say "done: $MARKET_URL $SITE_URL $ADMIN_URL $PLAY_URL"
