// Regenerates the README screenshots and demo GIF from a running demo stack.
// Run from anywhere: node frontend/capture-docs.mjs
// Requires the demo stack at http://localhost:8081 and ImageMagick (`magick`)
// for the GIF; screenshots alone work without it.
import { chromium } from '@playwright/test';
import fs from 'node:fs';
import path from 'node:path';
import os from 'node:os';
import { execFileSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const shots = path.join(root, 'docs', 'screenshots');
const frames = fs.mkdtempSync(path.join(os.tmpdir(), 'washtag-frames-'));
fs.mkdirSync(shots, { recursive: true });

const BASE = process.env.E2E_BASE_URL || 'http://localhost:8081';
const browser = await chromium.launch();

async function login(page) {
  await page.goto(`${BASE}/login`);
  await page.locator('input[type=email]').fill('demo@washtagbd.com');
  await page.locator('input[type=password]').fill('demo1234');
  await page.getByRole('button', { name: 'Log in' }).click();
  await page.waitForURL(/\/app$/);
  // The welcome toast auto-dismisses after 4s; keep docs screenshots clean.
  await page.waitForTimeout(4300);
}

const desktop = await browser.newPage({ viewport: { width: 1400, height: 900 }, deviceScaleFactor: 1 });
await login(desktop);

// Dashboard — wait for the SVG donut so all data is loaded.
await desktop.waitForSelector('.donut svg circle', { timeout: 15000 });
await desktop.waitForTimeout(500);
await desktop.screenshot({ path: path.join(shots, 'dashboard.png') });

// Assistant — wait for the index status, ask a question, then blur the input.
await desktop.goto(`${BASE}/app/assistant`);
await desktop.waitForSelector('.chat-input input', { timeout: 15000 });
await desktop.waitForFunction(() => !document.body.innerText.includes('Checking index'), null, { timeout: 15000 });
await desktop.locator('.chat-input input').fill('How many unpaid orders are there?');
await desktop.locator('.chat-input input').press('Enter');
await desktop.waitForTimeout(3500);
await desktop.locator('.chat-input input').evaluate((el) => el.blur());
await desktop.screenshot({ path: path.join(shots, 'assistant.png') });

// Admin — wait for the Ant Design table.
await desktop.goto(`${BASE}/app/admin`);
await desktop.waitForSelector('.ant-table-tbody tr.ant-table-row', { timeout: 15000 });
await desktop.screenshot({ path: path.join(shots, 'admin.png') });

// Billing (extra GIF frame only).
await desktop.goto(`${BASE}/app/billing`);
await desktop.waitForSelector('.billing-grid', { timeout: 15000 });
await desktop.waitForTimeout(400);
await desktop.screenshot({ path: path.join(frames, 'billing.png') });

// Public tracking — mobile viewport, full page.
const token = await desktop.evaluate(async () => {
  const login = await fetch('/api/auth/login', {
    method: 'POST', headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ email: 'demo@washtagbd.com', password: 'demo1234', sessionSecret: 'docs-capture' })
  }).then((r) => r.json());
  const list = await fetch('/api/orders?pageSize=1', { headers: { Authorization: `Bearer ${login.token}` } })
    .then((r) => r.json());
  return list.items[0].publicToken;
});
const mobile = await browser.newPage({ viewport: { width: 390, height: 844 }, deviceScaleFactor: 1 });
await mobile.goto(`${BASE}/track/${token}`);
await mobile.waitForSelector('.track-card', { timeout: 15000 });
await mobile.waitForTimeout(600);
await mobile.screenshot({ path: path.join(shots, 'customer-tracking.png'), fullPage: true });
await mobile.screenshot({ path: path.join(frames, 'tracking.png') });
await browser.close();

// Copy the already-verified screenshots into the GIF sequence, then build it.
for (const [from, to] of [['dashboard.png', 'dashboard.png'], ['assistant.png', 'assistant.png'], ['admin.png', 'admin.png']]) {
  fs.copyFileSync(path.join(shots, from), path.join(frames, to));
}
const gif = path.join(root, 'docs', 'demo.gif');
try {
  execFileSync('magick', [
    '-delay', '220',
    path.join(frames, 'dashboard.png'), path.join(frames, 'assistant.png'),
    path.join(frames, 'admin.png'), path.join(frames, 'billing.png'), path.join(frames, 'tracking.png'),
    '-resize', '900x570', '-gravity', 'center', '-background', '#0b1f27',
    '-extent', '900x570', '-loop', '0', '-layers', 'Optimize', gif
  ], { stdio: 'inherit' });
  console.log(`Wrote ${gif}`);
} catch {
  console.warn('magick not found — screenshots were updated but the GIF was not rebuilt.');
}
fs.rmSync(frames, { recursive: true, force: true });
console.log('Screenshots written to', shots);
