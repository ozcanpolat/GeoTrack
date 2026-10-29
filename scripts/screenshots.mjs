// Uygulama ekran görüntülerini docs/screenshots altına üretir.
// Kullanım (uygulama http://localhost:8080 adresinde çalışırken):
//   npm i -D playwright && npx playwright install chromium
//   $env:ADMIN_EMAIL="..."; $env:ADMIN_PASSWORD="..."; node scripts/screenshots.mjs
import { chromium } from 'playwright';
const base = process.env.BASE_URL || 'http://localhost:8080';
const pages = [
  ['giris', '/Account/Login', false],
  ['panel', '/', true],
  ['olaylar', '/Incidents', true],
  ['harita', '/Incidents/Map', true],
  ['yeni-olay', '/Incidents/Create', true],
  ['analitik', '/Analytics', true],
  ['arama', '/Search', true],
  ['bildirimler', '/Notifications', true],
  ['yonetici', '/Admin', true],
];
const browser = await chromium.launch();
const ctx = await browser.newContext({ viewport: { width: 1440, height: 900 }, locale: 'tr-TR' });
const page = await ctx.newPage();
await page.goto(base + '/Account/Login');
await page.screenshot({ path: 'docs/screenshots/giris.png' });
await page.fill('input[type=email]', process.env.ADMIN_EMAIL);
await page.fill('input[type=password]', process.env.ADMIN_PASSWORD);
await Promise.all([page.waitForNavigation(), page.click('button[type=submit]')]);
for (const [name, path, auth] of pages) {
  if (!auth) continue;
  await page.goto(base + path, { waitUntil: 'networkidle' });
  await page.waitForTimeout(1200); // harita döşemeleri için
  await page.screenshot({ path: `docs/screenshots/${name}.png`, fullPage: name !== 'harita' });
  console.log('ok', name);
}
await browser.close();
