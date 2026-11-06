#!/usr/bin/env bash
# One-time (and re-runnable) setup of the Farmverse backend box: Ubuntu 26.04 on EC2.
# Runs ON the server as the ubuntu user (deploy.sh copies and runs it). Installs:
#   Docker  → Postgres 17 and Redis 7 containers, bound to 127.0.0.1 only
#   Node 22 → the game server, worker and chain indexer (systemd units farmverse-*)
#   ASP.NET Core runtime → the island engine (farmverse-engine, port 8080, local only)
#   Caddy   → the public entry on 80/443, proxying to the server on 127.0.0.1:8787
#   ufw     → only 22, 80 and 443 open
set -euo pipefail

say() { printf '\n== %s\n' "$*"; }

say "packages"
sudo apt-get update -qq
sudo DEBIAN_FRONTEND=noninteractive apt-get install -y -qq ca-certificates curl gnupg docker.io caddy aspnetcore-runtime-10.0 ufw >/dev/null
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

say "caddy"
sudo cp /tmp/farmverse-deploy/Caddyfile /etc/caddy/Caddyfile
sudo systemctl enable caddy >/dev/null
sudo systemctl reload-or-restart caddy

say "firewall"
sudo ufw allow 22/tcp >/dev/null
sudo ufw allow 80/tcp >/dev/null
sudo ufw allow 443/tcp >/dev/null
sudo ufw --force enable >/dev/null
sudo ufw status | head -8

say "versions"
node --version; dotnet --list-runtimes | grep AspNetCore; caddy version; sudo docker ps --format '{{.Names}}  {{.Image}}  {{.Status}}'
