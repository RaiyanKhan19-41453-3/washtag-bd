// Simple realistic-load probe against a running stack.
// Usage: BASE_URL=http://localhost:8081 node scripts/load-test.mjs
import { writeFileSync } from 'node:fs';

const base = process.env.BASE_URL || 'http://localhost:8081';
const json = { 'Content-Type': 'application/json' };

async function timed(label, n, fn) {
  const results = [];
  const start = performance.now();
  await Promise.all(Array.from({ length: n }, async () => {
    const t = performance.now();
    try {
      const r = await fn();
      results.push({ status: r.status, ms: performance.now() - t });
      await r.arrayBuffer();
    } catch (e) {
      results.push({ status: 'ERR', ms: performance.now() - t, error: String(e).slice(0, 80) });
    }
  }));
  const ms = results.map(r => r.ms).sort((a, b) => a - b);
  const statuses = {};
  for (const r of results) statuses[r.status] = (statuses[r.status] || 0) + 1;
  const summary = {
    phase: label, requests: n, elapsedMs: Math.round(performance.now() - start),
    statuses, p50Ms: Math.round(ms[Math.floor(ms.length * 0.5)] || 0),
    p95Ms: Math.round(ms[Math.floor(ms.length * 0.95)] || 0),
    maxMs: Math.round(ms[ms.length - 1] || 0)
  };
  console.log(JSON.stringify(summary));
  return summary;
}

const login = await fetch(`${base}/api/auth/login`, {
  method: 'POST', headers: json,
  body: JSON.stringify({ email: 'demo@washtagbd.com', password: 'demo1234' })
}).then(r => r.json());
const auth = { Authorization: `Bearer ${login.token}`, ...json };

const orders = await fetch(`${base}/api/orders`, { headers: auth }).then(r => r.json());
const token = orders.items[0]?.publicToken;

const report = [];
const concurrency = Number(process.env.LOAD_CONCURRENCY || 50);
report.push(await timed(`dashboard ${concurrency} parallel`, concurrency, () => fetch(`${base}/api/dashboard`, { headers: auth })));
report.push(await timed('orders list 20 parallel', 20, () => fetch(`${base}/api/orders`, { headers: auth })));
if (token) report.push(await timed('public track 30 parallel', 30, () => fetch(`${base}/api/public/track/${token}`)));
report.push(await timed('create order 12 parallel', 12, () => fetch(`${base}/api/orders`, {
  method: 'POST', headers: { ...auth, 'Idempotency-Key': `load-${crypto.randomUUID()}` },
  body: JSON.stringify({ customerName: 'Load Test', customerPhone: '+8801',
    items: [{ name: 'Shirt', quantity: 1, unitPrice: 120 }] })
})));

writeFileSync('/tmp/opencode/load-report.json', JSON.stringify(report, null, 2));
console.log('Saved /tmp/opencode/load-report.json');
