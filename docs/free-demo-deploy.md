# Free public demo link (no credit card)

This gets a shareable live URL for WashTag BD for **$0**, using three free tiers that do not require a card. It is a demo, not a production deployment.

## What you get

| Piece | Free service | Role |
| --- | --- | --- |
| React SPA | Render static site | The public URL you share |
| .NET API | Render Docker web service | Business logic, background worker |
| Postgres + pgvector | Neon Free | All persistent data |
| Redis cache | Upstash Free | Best-effort cache, rotation tickets |

Important limits on free tiers:

- Render web services sleep after 15 idle minutes. The first request after that takes about a minute. Tell whoever you share the link with.
- Neon compute scales to zero after 5 idle minutes and resumes on the next query. It is included in that same first-request delay.
- Neon Free gives 1 GB storage and 100 compute hours per month. Never run a real business on it.
- Upstash Free gives 250 MB and 500,000 commands per month. Plenty for a demo.
- This demo seeds the public demo admin and allows self-settlement. Those are deliberate demo settings and are disabled in the normal production profile.

## Step 1: Neon (Postgres)

1. Sign up at `neon.com` with GitHub or email. No card asked.
2. Create a project. Use a region close to your audience or to Render's region (`singapore` in `render.yaml`).
3. In the project dashboard, open **Connection Details**, pick the **pooled** connection string, and copy it. It looks like:
   `postgresql://user:pass@ep-xxx-pooler.ap-southeast-1.aws.neon.tech/washtagdb?sslmode=require`
4. No need to create the `vector` extension. The app runs `CREATE EXTENSION IF NOT EXISTS vector` at startup, and Neon allows it on every plan.

## Step 2: Upstash (Redis)

1. Sign up at `upstash.com`. No card asked.
2. Create a Redis database on the free plan, in any region.
3. In its details, find the **Redis URL** (or the connection string with host, port and password). It looks like:
   `rediss://default:PASSWORD@apn1-xxxx.upstash.io:6379`
4. StackExchange.Redis needs a different format. Convert it to:
   `apn1-xxxx.upstash.io:6379,password=PASSWORD,ssl=True,abortConnect=False`
   Keep the host, port and password from your URL. Drop the `rediss://default:` prefix and the path.

## Step 3: Render

1. Push this repository to your GitHub account. The root file `render.yaml` must be present.
2. Sign up at `render.com` with GitHub. No card needed for the free instance types.
3. In Render, choose **New > Blueprint**, connect your repository, and Render reads `render.yaml`. It creates two services.
4. Fill the secrets it asks for:
   - `ConnectionStrings__Postgres`: the pooled Neon string from Step 1, with `sslmode=require` kept.
   - `ConnectionStrings__Redis`: the converted StackExchange string from Step 2.
   - `PAYMENT_WEBHOOK_SECRET`: leave empty, or set a random string if you want to test webhook settlement.
   - `OPENAI_API_KEY` and `AI_GATEWAY_TOKEN`: leave empty. The assistant works fully offline with local embeddings.
5. Deploy. The API first applies all EF migrations, seeds the demo shop, and builds the vector index. This takes a few minutes on the first boot.
6. Open the URL of the **static site** service (`washtag-web`), not the API service.

The blueprint wires these automatically, so you do not copy URLs by hand:

- `VITE_API_URL` on the SPA points at the API service.
- `ALLOWED_ORIGINS` on the API allows the SPA origin, including CORS preflight.
- `PUBLIC_BASE_URL` on the API points at the SPA so QR codes open the tracking page on your public domain.

## Step 4: verify

Log in with:

- `demo@washtagbd.com` / `demo1234` (owner and platform admin)
- `staff@washtagbd.com` / `demo1234` (limited staff view)

Check that the dashboard loads, open an order, copy its public tracking link into a new incognito window, and confirm the status page renders. The health endpoint of the API is `https://<api-host>/health`.

## Troubleshooting

- **API starts then fails health checks**: the Postgres connection string is wrong or the Neon compute is suspended. Check the API logs in Render, then check Neon's connection details again. Keep `sslmode=require`.
- **SPA loads but every request fails with CORS**: `ALLOWED_ORIGINS` must be the exact `https://...` origin of the static site, with no trailing slash. The blueprint sets it from the service URL. If you used a custom domain, add that domain too, separated by a comma.
- **QR codes point at localhost**: `PUBLIC_BASE_URL` must be the static site URL. The blueprint sets it. If you changed service names, set it manually.
- **First click takes a minute**: expected. Render and Neon both scale down when idle. You can ping `/health` with an uptime monitor every 10 minutes to keep it warm.
- **Assistant says vector search is unavailable**: check the API logs. Neon allows `CREATE EXTENSION vector`, but if the free compute is suspended at that exact moment the startup retry will handle it.

## Why not the other free hosts

- Oracle Cloud Always Free would run the whole Compose file unchanged and stay on, but it requires a card.
- Railway gives only $1 of monthly credit after a short trial, which cannot run a 512 MB service all month.
- Fly.io has no free tier for new accounts as of 2026, only a seven day trial.
- SnapDeploy is no-card and has four containers, but only 100 container hours a month in total, so it cannot stay up.
- Render's own free Postgres expires after 30 days, so this guide uses Neon instead.
