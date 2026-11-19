# Verification beyond unit tests

These are the manual/repeatable experiments used to close the gaps that unit tests cannot cover. None of them require installing tools on the host — everything runs through Docker.

## 1. Realistic load

```bash
node scripts/load-test.mjs                    # 50 parallel dashboard requests
LOAD_CONCURRENCY=200 node scripts/load-test.mjs
```

Measured on a single API container with the demo database:

| Phase | Requests | Statuses | p50 | p95 |
|---|---|---|---|---|
| Dashboard | 50 | all 200 | 331 ms | 344 ms |
| Dashboard | 200 | all 200 | 650 ms | 704 ms |
| Orders list | 20 | all 200 | 35 ms | 37 ms |
| Public tracking | 30 | all 200 | 167 ms | 170 ms |
| Create order | 12 | all 201 | 174 ms | 189 ms |

No errors. The dashboard is the heaviest path because it aggregates orders in SQL and every authenticated request validates the JWT against the database. It stays well under a second at 200 concurrent requests.

## 2. Two API replicas

Two API containers were started against the same Postgres/Redis with one shared `JWT_SECRET`:

- A token signed by replica 1 was accepted by replica 2 (`200`), confirming a shared secret prevents random 401s.
- After six logins exhausted the per-IP limit on replica 1 (`200×5, 429×2`), a seventh login on replica 2 succeeded (`200`). **Rate limits are per-process** — the effective limit doubles with two replicas.
- SignalR groups and the maintenance worker are likewise process-local; a second replica would duplicate maintenance work and miss cross-replica notifications.

Conclusion: the supported deployment remains **one API replica**. Scaling out requires a distributed rate limiter, a SignalR backplane and a distributed maintenance lock.

## 3. Upgrade from an older tag with data

1. Checked out `v1.6.1` in a separate worktree and started it on its own Compose project and port.
2. Created three extra orders (17 total, 11 customers).
3. Dumped the database with `pg_dump --clean --if-exists`.
4. Restored that dump into a fresh database for the current code, then started the current stack.

Result: 8 migrations applied (the 7 from v1.6.1 plus `AddReportingIndexes`), all 17 orders and 11 customers survived, billing and public tracking worked, and a repeated idempotency key returned `201` then `200` with the same order. No manual repair was needed.

Do not delete rows from `__EFMigrationsHistory` when rolling back — a later upgrade would try to recreate indexes that still exist.

## 4. Payment/SMS provider

A provider-agnostic webhook is available. Set `NOTIFICATION_WEBHOOK_URL` (and optionally `NOTIFICATION_WEBHOOK_TOKEN`) to receive `POST {"to": "...", "text": "..."}`:

- new order → shop phone/email
- order ready for pickup → customer phone
- order delivered → customer phone

Delivery is best-effort: a failing webhook is logged and never breaks a shop operation. With no URL configured, nothing is sent.

A real bKash/Stripe gateway is still not integrated. Production disables self-service settlement (`MANUAL_PAYMENT_ENABLED=false`); an admin confirms payments instead.
