#!/usr/bin/env bash
# Build the Farmverse backend here and ship it to the EC2 box. Run from Git Bash at the repo root:
#
#   bash deployment/deploy.sh              # build, upload, migrate, restart
#   bash deployment/deploy.sh --provision  # first run: also install packages, containers, units, nginx, certificates
#   bash deployment/deploy.sh --web        # only the static sites: market, landing, admin, and Builds/WebGL-testnet
#
# What runs on the box (see provision.sh): farmverse-server (API :8787), farmverse-worker,
# farmverse-indexer, farmverse-engine (:8080), Postgres and Redis in Docker, nginx + certbot on :80/:443.
# It ships the working tree as it is, uncommitted changes included.
set -euo pipefail

HOST="${FARMVERSE_HOST:-ubuntu@ec2-34-203-207-91.compute-1.amazonaws.com}"
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
KEY="$ROOT/deployment/farmverse.pem"
SSH=(ssh -i "$KEY" -o BatchMode=yes -o StrictHostKeyChecking=accept-new "$HOST")
STAGE="$(mktemp -d)"
trap 'rm -rf "$STAGE"' EXIT

say() { printf '\n== %s\n' "$*"; }

if [[ "${1:-}" == "--web" ]]; then
  # The sites talk to api.farmverse.world on the network and contracts in chain/deploy/testnet.env.
  ENVF="$ROOT/chain/deploy/testnet.env"
  get() { sed -n "s/^$1=//p" "$ENVF" | tr -d '
' | head -n1; }
  export VITE_API_URL=https://api.farmverse.world/v1/market VITE_GAME_API_URL=https://api.farmverse.world     VITE_NEAR_NETWORK="$(get NEAR_NETWORK)" VITE_ITEMS_CONTRACT="$(get NEAR_ITEMS_CONTRACT)" VITE_MARKET_CONTRACT="$(get NEAR_MARKET_CONTRACT)"     VITE_FARM_CONTRACT="$(get NEAR_FARM_CONTRACT)" VITE_PLAY_URL=https://play.farmverse.world VITE_MARKET_URL=https://marketplace.farmverse.world
  for site in marketplace:market landing:landing admin:admin; do
    say "build ${site%%:*}"
    api=$VITE_API_URL
    [[ ${site%%:*} == admin ]] && api=https://api.farmverse.world   # the admin web calls the API's own routes
    (cd "$ROOT/web/${site%%:*}" && VITE_API_URL=$api npx vite build --emptyOutDir --outDir "$STAGE/web/${site##*:}" --logLevel warn)
  done
  if [[ -d "$ROOT/Builds/WebGL-testnet" ]]; then
    cp -r "$ROOT/Builds/WebGL-testnet" "$STAGE/web/play"
  else
    echo "no Builds/WebGL-testnet (Farmverse ▸ Web ▸ Build (testnet on farmverse.world)): the game is not uploaded"
  fi
  say "upload sites"
  tar -C "$STAGE/web" -czf "$STAGE-web.tgz" .
  scp -q -i "$KEY" -o BatchMode=yes "$STAGE-web.tgz" "$HOST:/tmp/farmverse-web.tgz"
  rm -f "$STAGE-web.tgz"
  "${SSH[@]}" 'set -e; rm -rf /tmp/farmverse-web && mkdir -p /tmp/farmverse-web && tar -C /tmp/farmverse-web -xzf /tmp/farmverse-web.tgz
    for d in /tmp/farmverse-web/*; do n=$(basename "$d"); mkdir -p /var/www/farmverse/$n && rsync -a --delete "$d"/ /var/www/farmverse/$n/; done
    rm -rf /tmp/farmverse-web /tmp/farmverse-web.tgz'
  say "done: https://play.farmverse.world https://marketplace.farmverse.world https://farmverse.world https://admin.farmverse.world"
  exit 0
fi

say "build server"
(cd "$ROOT" && npm run build --workspace backend/server --silent)
mkdir -p "$STAGE/server" "$STAGE/engine"
cp -r "$ROOT/backend/server/dist" "$ROOT/backend/server/prisma" "$ROOT/backend/server/prisma.config.ts" "$STAGE/server/"
# The bundle inlines our own code and zod; the server installs only what esbuild left external,
# plus the Prisma CLI for `migrate deploy`. Versions are the ones pinned in backend/server.
node -e '
  const p = require(process.argv[1]);
  const deps = { ...p.dependencies, prisma: p.devDependencies.prisma };
  delete deps["@farmverse/contracts"]; delete deps.zod;
  console.log(JSON.stringify({ name: "farmverse-server-runtime", private: true, type: "module", dependencies: deps }, null, 2));
' "$ROOT/backend/server/package.json" > "$STAGE/server/package.json"

say "build island engine"
# Its own build folder, so an engine already running locally (which locks bin/) doesn't block it.
dotnet publish "$ROOT/tools/dotnet/Farmverse.IslandEngine" -c Release -o "$STAGE/engine" \
  --artifacts-path "$STAGE/.engine-build" --nologo -v quiet
rm -rf "$STAGE/.engine-build"

cp -r "$ROOT/deployment/systemd" "$ROOT/deployment/nginx" "$ROOT/deployment/provision.sh" "$STAGE/"

say "upload"
tar -C "$STAGE" -czf "$STAGE.tgz" .
scp -q -i "$KEY" -o BatchMode=yes "$STAGE.tgz" "$HOST:/tmp/farmverse-deploy.tgz"
rm -f "$STAGE.tgz"
"${SSH[@]}" 'rm -rf /tmp/farmverse-deploy && mkdir -p /tmp/farmverse-deploy && tar -C /tmp/farmverse-deploy -xzf /tmp/farmverse-deploy.tgz'

if [[ "${1:-}" == "--provision" ]]; then
  say "provision"
  "${SSH[@]}" 'bash /tmp/farmverse-deploy/provision.sh'
fi

# The server's settings: made once from backend/server/.env (NEAR, contracts, indexer, mint
# settings and keys) with the server-specific values replaced. Never written to disk here.
if ! "${SSH[@]}" 'sudo test -f /etc/farmverse/server.env'; then
  say "server settings (first time)"
  node -e '
    const fs = require("fs"), crypto = require("crypto");
    const local = Object.fromEntries(fs.readFileSync(process.argv[1], "utf8").split(/\r?\n/)
      .filter((l) => /^[A-Z_]+=/.test(l)).map((l) => [l.slice(0, l.indexOf("=")), l.slice(l.indexOf("=") + 1)]));
    const keep = ["MIN_CLIENT_VERSION","NEAR_NETWORK","NEAR_RPC_URL","AUTH_RECIPIENT","ADMIN_ACCOUNTS",
      "NEAR_ITEMS_CONTRACT","NEAR_MARKET_CONTRACT","NEARDATA_URL","NEARDATA_API_KEY","NEARDATA_RPM",
      "INDEXER_START_BLOCK","MINT_SIGNER_SEED","MINT_DAILY_VALUE_CAP","MINT_MIN_ACCOUNT_HOURS"];
    const out = {
      FARMVERSE_ENV: "staging",
      PORT: "8787",
      REDIS_URL: "redis://127.0.0.1:6379",
      ISLAND_ENGINE_URL: "http://127.0.0.1:8080",
      JWT_SEED: crypto.randomBytes(32).toString("base64"),
      ALLOWED_ORIGINS: [local.ALLOWED_ORIGINS, "https://play.farmverse.world,https://marketplace.farmverse.world,https://admin.farmverse.world,https://farmverse.world"].filter(Boolean).join(","),
    };
    for (const k of keep) if (local[k] !== undefined) out[k] = local[k];
    process.stdout.write(Object.entries(out).map(([k, v]) => k + "=" + v).join("\n") + "\n");
  ' "$ROOT/backend/server/.env" | "${SSH[@]}" 'sudo bash -c "
    umask 077
    . /etc/farmverse/db.env
    { cat; echo DATABASE_URL=postgresql://farmverse:\$POSTGRES_PASSWORD@127.0.0.1:5432/farmverse; } > /etc/farmverse/server.env
  "'
fi

# Settings added since the first deploy (plan 15): set from backend/server/.env and chain/deploy/testnet.env on every
# deploy, so the box follows the testnet contracts and keys here. Everything else in server.env is left as it is.
# ALLOWED_ORIGINS gains the web sites' origins (the farmverse.world ones and the Vercel ones, plus EXTRA_ORIGINS).
say "server settings"
node -e '
  const fs = require("fs");
  const read = (f) => fs.existsSync(f) ? Object.fromEntries(fs.readFileSync(f, "utf8").split(/\r?\n/)
    .filter((l) => /^[A-Z_]+=/.test(l)).map((l) => [l.slice(0, l.indexOf("=")), l.slice(l.indexOf("=") + 1)])) : {};
  const local = read(process.argv[1]), chain = read(process.argv[2]);
  const out = {};
  for (const k of ["NEAR_ITEMS_CONTRACT", "NEAR_MARKET_CONTRACT", "NEAR_FARM_CONTRACT", "NEAR_TREASURY"]) if (chain[k]) out[k] = chain[k];
  // (no PRIVY_VERIFICATION_KEY: a multi-line PEM; unset, the server checks tokens with the app published keys)
  for (const k of ["PRIVY_APP_ID", "PRIVY_APP_SECRET", "FAUCET_ACCOUNT", "FAUCET_PRIVATE_KEY",
    "FAUCET_FARM", "FAUCET_NEAR", "FAUCET_DAILY_CAP", "RELAYER_ACCOUNT", "RELAYER_PRIVATE_KEY", "WELCOME_NEAR",
    "WELCOME_BELOW_NEAR", "MERCHANTS", "FARM_PER_NEAR", "INTENTS_LIVE", "ONECLICK_API_KEY"]) if (local[k]) out[k] = local[k];
  if (!out.FAUCET_NEAR) out.FAUCET_NEAR = "0.2";
  out.ADD_ORIGINS = ["https://play.farmverse.world", "https://marketplace.farmverse.world", "https://admin.farmverse.world",
    "https://farmverse.world", "https://farmverse-play.vercel.app", "https://farmverse-market.vercel.app",
    "https://farmverse-admin.vercel.app", "https://farmverse-site.vercel.app", ...(process.env.EXTRA_ORIGINS || "").split(",")]
    .filter(Boolean).join(",");
  process.stdout.write(Object.entries(out).map(([k, v]) => k + "=" + v).join("\n") + "\n");
' "$ROOT/backend/server/.env" "$ROOT/chain/deploy/testnet.env" | "${SSH[@]}" 'sudo python3 -c "
import sys, os
f = \"/etc/farmverse/server.env\"
new = dict(l.split(\"=\", 1) for l in sys.stdin.read().splitlines() if \"=\" in l)
add = [o for o in new.pop(\"ADD_ORIGINS\", \"\").split(\",\") if o]
lines = open(f).read().splitlines()
cur = dict(l.split(\"=\", 1) for l in lines if \"=\" in l and not l.startswith(\"#\"))
origins = [o for o in cur.get(\"ALLOWED_ORIGINS\", \"\").split(\",\") if o]
new[\"ALLOWED_ORIGINS\"] = \",\".join(origins + [o for o in add if o not in origins])
out, seen = [], set()
for l in lines:
    k = l.split(\"=\", 1)[0]
    if \"=\" in l and k in new: out.append(k + \"=\" + new[k]); seen.add(k)
    else: out.append(l)
out += [k + \"=\" + v for k, v in new.items() if k not in seen]
os.umask(0o077)
open(f + \".new\", \"w\").write(\"\\n\".join(out) + \"\\n\")
os.replace(f + \".new\", f)
print(\"server.env:\", \", \".join(sorted(new)))
"'

say "install, migrate, restart"
"${SSH[@]}" 'set -euo pipefail
  rm -rf /opt/farmverse/server/dist /opt/farmverse/server/prisma /opt/farmverse/engine/*
  cp -r /tmp/farmverse-deploy/server/. /opt/farmverse/server/
  cp -r /tmp/farmverse-deploy/engine/. /opt/farmverse/engine/
  cd /opt/farmverse/server
  npm install --omit=dev --no-audit --no-fund --loglevel=error
  sudo bash -c "set -a; . /etc/farmverse/server.env; set +a; cd /opt/farmverse/server && npx --no-install prisma migrate deploy"
  sudo systemctl restart farmverse-engine
  sleep 2
  sudo systemctl restart farmverse-server farmverse-worker farmverse-indexer
  sleep 4
  systemctl is-active farmverse-engine farmverse-server farmverse-worker farmverse-indexer nginx
  curl -fsS http://127.0.0.1:8787/healthz && echo'
