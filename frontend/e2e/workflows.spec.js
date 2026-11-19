import { test, expect, request } from '@playwright/test';

async function login(page) {
  await page.goto('/login');
  await page.locator('input[type=email]').fill('demo@washtagbd.com');
  await page.locator('input[type=password]').fill('demo1234');
  await page.getByRole('button', { name: 'Log in' }).click();
  await expect(page).toHaveURL(/\/app$/);
}

test('owner can log in and see the dashboard', async ({ page }) => {
  await login(page);
  await expect(page.getByText(/orders|অর্ডার/i).first()).toBeVisible();
});

test('create an order with a QR receipt, then record payment and print', async ({ page }) => {
  await login(page);
  await page.goto('/app/orders/new');
  await page.getByPlaceholder('e.g. Tasnim Rahman').fill('E2E Customer');
  await page.getByPlaceholder('+880 1xxx-xxxxxx').fill('+8801999000000');
  await page.getByPlaceholder('e.g. Cotton Shirt').fill('Cotton Shirt');
  await page.getByRole('button', { name: /Create order & QR/ }).click();

  await expect(page).toHaveURL(/\/app\/orders\/[0-9a-f-]{36}$/);
  await expect(page.locator('.qr-display img')).toBeVisible();
  await expect(page.getByText(/WT-\d+/).first()).toBeVisible();

  await page.getByRole('button', { name: 'Record payment' }).first().click();
  await page.locator('#payment-form input[type=number]').fill('120');
  await page.getByRole('button', { name: 'Save payment' }).click();
  await expect(page.getByText('Payment recorded.')).toBeVisible();

  await page.getByRole('link', { name: 'Print receipt' }).click();
  await expect(page).toHaveURL(/\/print\/[0-9a-f-]{36}$/);
  await expect(page.locator('.receipt-paper')).toBeVisible();
});

test('customer can open a public tracking page anonymously', async ({ page }) => {
  const api = await request.newContext({ baseURL: 'http://localhost:8081' });
  const loginResponse = await api.post('/api/auth/login', {
    data: { email: 'demo@washtagbd.com', password: 'demo1234' }
  });
  const { token } = await loginResponse.json();
  const list = await api.get('/api/orders', { headers: { Authorization: `Bearer ${token}` } });
  const { items } = await list.json();
  expect(items.length).toBeGreaterThan(0);

  await page.goto(`/track/${items[0].publicToken}`);
  await expect(page.getByText(/WT-\d+/).first()).toBeVisible();
  await expect(page.locator('.track-progress-track')).toBeVisible();
});

test('platform admin can open stats and run maintenance', async ({ page }) => {
  await login(page);
  await page.goto('/app/admin');
  await expect(page.getByRole('heading', { name: /Platform overview/i })).toBeVisible();
  await page.getByRole('button', { name: /Run maintenance/i }).click();
  await expect(page.getByRole('heading', { name: /Last maintenance run/i })).toBeVisible();
});
