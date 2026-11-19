# Deploying WashTag BD publicly

This guide assumes one VPS, one domain, Docker + Compose, and a TLS reverse proxy in front of the app. **Run through it all before exposing port 443.**

## 1. Prepare the host

- Point your domain's A record at the VPS.
- Open only ports **80 and 443**. Do **not** expose `8081` — the web container is bound to `127.0.0.1`.
- Install Docker Engine + Compose v2 and `git`.
- Clone the repo: `git clone <repo-url> washtag-bd && cd washtag-bd`.

## 2. Create a production `.env`

```bash
cp .env.example .env
```

Then edit it. Generate secrets on the server (never reuse the example values):

```bash
openssl rand -base64 48   # → JWT_SECRET (32+ characters required)
openssl rand -base64 32   # → POSTGRES_PASSWORD
```

Minimum production values:

```env
POSTGRES_USER=washtag
POSTGRES_PASSWORD=<strong generated value>
POSTGRES_DB=washtagdb
JWT_SECRET=<strong generated value>
APP_PORT=8081
SEED_DEMO_DATA=false
ADMIN_EMAILS=
ALLOWED_ORIGINS=https://wash.example.com
PUBLIC_BASE_URL=https://wash.example.com
ENABLE_SWAGGER=false
MAINTENANCE_ENABLED=true
MAINTENANCE_INTERVAL_MINUTES=60
MAINTENANCE_RUN_ON_STARTUP=true
```

**Important:** `POSTGRES_PASSWORD`, `POSTGRES_USER` and `POSTGRES_DB` are only read when the `pgdata` volume is first created. Changing them later does not change the database password but *does* change what the API tries to connect with, causing a total outage. If you must change it on an existing volume:

```bash
docker compose exec -T postgres psql -U washtag -d washtagdb -c "ALTER USER washtag WITH PASSWORD 'newpassword';"
# then update .env and recreate the api container
```

`SEED_DEMO_DATA` also only affects a fresh volume. Flipping it to `false` later does **not** delete the demo account — use a fresh `pgdata` volume (`docker compose down -v`) or delete the demo rows manually.

## 3. First start and admin bootstrap

`ADMIN_EMAILS` must be empty on the very first start, because registration deliberately rejects reserved admin addresses:

```bash
docker compose -f docker-compose.yml -f docker-compose.prod.yml up -d --build
```

1. Visit `https://wash.example.com`, register your real owner account.
2. Set `ADMIN_EMAILS=you@example.com` in `.env`.
3. Restart the API so the new setting is picked up (it is read at startup):

```bash
docker compose -f docker-compose.yml -f docker-compose.prod.yml up -d api
```

The API logs a critical warning on every start if demo seeding and the public demo admin address are both active — treat it as a stop-ship condition.

## 4. TLS reverse proxy

The app itself is HTTP-only; TLS terminates at your proxy. Both examples forward the real client IP and the original HTTPS scheme, which auth rate limiting and QR URLs depend on.

**Caddy** (automatic certificates):

```caddyfile
wash.example.com {
	reverse_proxy 127.0.0.1:8081 {
		header_up X-Forwarded-Proto {scheme}
	}
}
```

**nginx**:

```nginx
server {
	listen 443 ssl http2;
	server_name wash.example.com;
	ssl_certificate     /etc/letsencrypt/live/wash.example.com/fullchain.pem;
	ssl_certificate_key /etc/letsencrypt/live/wash.example.com/privkey.pem;

	location / {
		proxy_pass http://127.0.0.1:8081;
		proxy_http_version 1.1;
		proxy_set_header Host $host;
		proxy_set_header X-Real-IP $remote_addr;
		proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
		proxy_set_header X-Forwarded-Proto $scheme;
		proxy_set_header Upgrade $http_upgrade;
		proxy_set_header Connection "upgrade";
	}
}
```
Also redirect port 80 to 443. Do not skip the `X-Forwarded-*` headers: without them every visitor shares one auth rate-limit bucket and printed QR codes become plain HTTP links.

## 5. Verify before going live

- `curl https://wash.example.com/health` → `{"status":"healthy"}`.
- Log in with your real account. **Confirm `demo@washtagbd.com` / `demo1234` does not work.**
- Create an order and open its QR/receipt: the link must start with `https://wash.example.com/track/...`.
- As a non-admin account, `/api/admin/stats` and `/api/admin/maintenance/run` must return `403`.
- In the admin UI, Suspend → Reactivate a test shop, then Cancel → Restore it. Verify the lock and banner behave as expected.
- Check `docker compose logs api` for warnings about `JWT_SECRET`, `SEED_DEMO_DATA` or `PUBLIC_BASE_URL`.

## 6. Backups and upgrades

```bash
# nightly dump, kept for 7 days in ./backups (gitignored)
0 2 * * * cd /srv/washtag-bd && ./scripts/backup.sh
```

Copy the dumps off-server. Restore (destructive, asks for confirmation):

```bash
./scripts/restore.sh backups/washtag_....sql.gz
```

Upgrade:

```bash
git pull origin main
docker compose -f docker-compose.yml -f docker-compose.prod.yml up -d --build
```

EF migrations run automatically at API startup. For rollback, check out the previous release tag and rebuild; there is no image registry yet, so rollbacks are source-based.

## 7. Scaling limits — stay on one API replica

This deployment is designed for a single API container. Running more replicas without extra infrastructure causes:

- each replica mints a different `JWT_SECRET` if none is configured → random `401`s (a strong shared `JWT_SECRET` avoids this);
- the maintenance worker and startup indexing to run once per replica (no distributed lock);
- SignalR groups to be process-local (clients miss updates from other replicas);
- in-memory rate limits to multiply;
- concurrent startup seed/migration races.

If you need multiple replicas: add a shared secret, a Postgres advisory lock around startup + maintenance, a SignalR backplane, and a distributed rate limiter, or enforce sticky sessions at the proxy.

## 8. Known product limitations

- Invoice settlement is **self-declared** (cash/bKash/card reference, no gateway webhook). A shop owner can mark an invoice paid to renew. Add a verified payment provider before charging real money.
- Tokens are stored in `localStorage`; XSS is mitigated by a strict CSP but HttpOnly cookie auth with CSRF protection would be stronger.
- Bengali translations are partial (English fallback per string).
- The assistant uses deterministic local embeddings by default; an LLM is optional.
