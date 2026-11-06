#!/usr/bin/env bash
# One-time (and re-runnable) setup of the Farmverse backend box: Ubuntu 26.04 on EC2.
# Runs ON the server as the ubuntu user (deploy.sh copies and runs it). Installs:
#   Docker  → Postgres 17 and Redis 7 containers, bound to 127.0.0.1 only
#   Node 22 → the game server, worker and chain indexer (systemd units farmverse-*)
#   ASP.NET Core runtime → the island engine (farmverse-engine, port 8080, local only)
#   nginx   → the public entry on 80/443 (WebSocket included), proxying to 127.0.0.1:8787
#   certbot → Let's Encrypt certificates for DOMAINS, renewed by certbot.timer
#   ufw     → only 22, 80 and 443 open
set -euo pipefail

DOMAINS=(api.farmverse.world server.farmverse.world)
CERT_EMAIL=devarogundade@gmail.com

say() { printf '\n== %s\n' "$*"; }

say "packages"
sudo apt-get update -qq
sudo DEBIAN_FRONTEND=noninteractive apt-get install -y -qq ca-certificates curl gnupg docker.io nginx certbot python3-certbot-nginx aspnetcore-runtime-10.0 ufw >/dev/null
# Caddy was the proxy before nginx.
if dpkg -s caddy >/dev/null 2>&1; then
  sudo systemctl disable --now caddy >/dev/null 2>&1 || true
  sudo DEBIAN_FRONTEND=noninteractive apt-get purge -y -qq caddy >/dev/null
fi
if ! command -v node >/dev/null || [[ "$(node -p 'process.versions.node.split(".")[0]')" != "22" ]]; then
  curl -fsSL https://deb.nodesource.com/setup_22.x | sudo -E bash - >/dev/null
  sudo DEBIAN_FRONTEND=noninteractive apt-get install -y -qq nodejs >/dev/null
fi
sudo systemctl enable --now docker >/dev/null
sudo usermod -aG docker ubuntu

say "folders"
sudo mkdir -p /opt/farmverse/server /opt/farmverse/engine /etc/farmverse
sudo chown -R ubuntu:ubuntu /opt/farmverse
sudo chmod 750 /etc/farmverse

say "postgres and redis"
# The password lives in /etc/farmverse/db.env (made once, read by server.env's DATABASE_URL).
if [[ ! -f /etc/farmverse/db.env ]]; then
  echo "POSTGRES_PASSWORD=$(openssl rand -hex 24)" | sudo tee /etc/farmverse/db.env >/dev/null
  sudo chmod 600 /etc/farmverse/db.env
fi
if ! sudo docker ps -a --format '{{.Names}}' | grep -qx fv-postgres; then
  sudo docker run -d --name fv-postgres --restart unless-stopped \
    --env-file /etc/farmverse/db.env -e POSTGRES_USER=farmverse -e POSTGRES_DB=farmverse \
    -p 127.0.0.1:5432:5432 -v fv-pgdata:/var/lib/postgresql/data postgres:17-alpine >/dev/null
fi
if ! sudo docker ps -a --format '{{.Names}}' | grep -qx fv-redis; then
  sudo docker run -d --name fv-redis --restart unless-stopped \
    -p 127.0.0.1:6379:6379 -v fv-redisdata:/data redis:7-alpine redis-server --appendonly yes >/dev/null
fi

say "services"
sudo cp /tmp/farmverse-deploy/systemd/*.service /etc/systemd/system/
sudo systemctl daemon-reload
sudo systemctl enable farmverse-engine farmverse-server farmverse-worker farmverse-indexer >/dev/null

say "nginx"
sudo cp /tmp/farmverse-deploy/nginx/farmverse.conf /etc/nginx/sites-available/farmverse.conf
sudo ln -sf /etc/nginx/sites-available/farmverse.conf /etc/nginx/sites-enabled/farmverse.conf
sudo rm -f /etc/nginx/sites-enabled/default
sudo nginx -t -q
sudo systemctl enable nginx >/dev/null
sudo systemctl reload-or-restart nginx

say "certificates"
# Only names whose DNS already points here: Let's Encrypt checks them over port 80. Re-running
# reinstalls the existing certificate into the freshly copied nginx config (and expands it
# when another name has started resolving).
MY_IP="$(curl -fsS -m 5 https://checkip.amazonaws.com | tr -d '[:space:]')"
READY=()
for d in "${DOMAINS[@]}"; do
  if getent ahostsv4 "$d" | awk '{print $1}' | grep -qx "$MY_IP"; then READY+=(-d "$d"); else echo "skip $d: DNS doesn't point at $MY_IP yet"; fi
done
if (( ${#READY[@]} )); then
  sudo certbot --nginx --non-interactive --agree-tos -m "$CERT_EMAIL" --redirect \
    --keep-until-expiring --expand --cert-name farmverse "${READY[@]}"
  sudo systemctl enable --now certbot.timer >/dev/null 2>&1 || true
fi

say "firewall"
sudo ufw allow 22/tcp >/dev/null
sudo ufw allow 80/tcp >/dev/null
sudo ufw allow 443/tcp >/dev/null
sudo ufw --force enable >/dev/null
sudo ufw status | head -8

say "versions"
node --version; dotnet --list-runtimes | grep AspNetCore; nginx -v; certbot --version; sudo docker ps --format '{{.Names}}  {{.Image}}  {{.Status}}'
