# Backend server (EC2)

One Ubuntu 26.04 box runs the whole backend for testnet:

| What | How | Where |
|---|---|---|
| API + WebSocket | `farmverse-server` (systemd) | 127.0.0.1:8787 |
| Jobs | `farmverse-worker` | |
| Chain indexer | `farmverse-indexer` | |
| Island engine (C#) | `farmverse-engine` | 127.0.0.1:8080 |
| Postgres 17, Redis 7 | Docker containers `fv-postgres`, `fv-redis` | 127.0.0.1 only |
| Public entry | nginx + certbot (Let's Encrypt) | :443, :80 redirects |

The firewall (ufw) allows only 22, 80 and 443. Public URLs (same API, WebSocket at `/v1/live`):
`https://api.farmverse.world` and `https://server.farmverse.world` (A records → 34.203.207.91).

```bash
ssh -i deployment/farmverse.pem ubuntu@ec2-34-203-207-91.compute-1.amazonaws.com
```

`farmverse.pem` is git-ignored. Don't commit it.

## Deploy

From Git Bash at the repo root. It builds the server and the engine locally, uploads them, runs
`prisma migrate deploy` and restarts the services. It ships the working tree as it is,
uncommitted changes included.

```bash
bash deployment/deploy.sh
```

`--provision` also (re)installs packages, containers, systemd units, the Caddyfile and the
firewall rules. It's safe to run again.

## Settings

`/etc/farmverse/server.env` (root only) was made on the first deploy from `backend/server/.env`:
the NEAR, contract, indexer and mint values were copied over. These were set fresh:
`FARMVERSE_ENV=staging` (no wallet-less dev sign-in), a new `JWT_SEED`, local Postgres/Redis/engine
URLs. Later deploys never touch it. To change a value:

```bash
sudo nano /etc/farmverse/server.env && sudo systemctl restart farmverse-server farmverse-worker farmverse-indexer
```

The Postgres password is in `/etc/farmverse/db.env`.

## Day to day

```bash
systemctl status farmverse-server                      # or -worker, -indexer, -engine
sudo journalctl -u farmverse-indexer -f                 # follow logs
sudo docker exec -it fv-postgres psql -U farmverse      # database shell
```

## HTTPS

`provision.sh` lists the names in `DOMAINS` and gets one certificate for those whose DNS
already points at the box (`certbot --nginx`, account email devarogundade@gmail.com).
`certbot.timer` renews it. `deployment/nginx/farmverse.conf` is the plain HTTP config: certbot adds
the 443 listener and the redirect to the copy on the server, and every `--provision` run puts
them back after copying the file. To add a name, create its A record, add it to `DOMAINS` and to
`server_name` in the nginx config, then run `deploy.sh --provision`.
