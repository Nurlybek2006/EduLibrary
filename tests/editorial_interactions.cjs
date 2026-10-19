/** Targeted interaction checks: no successful application-data writes. */
const fs = require('node:fs');
const path = require('node:path');
const { chromium } = require('../.tools/browser/node_modules/playwright');
const config = JSON.parse(fs.readFileSync(path.resolve(__dirname, '../appsettings.Local.json'), 'utf8').replace(/^\uFEFF/, ''));
const output = path.resolve(__dirname, '../artifacts/editorial-ui');
const checks = [];
const check = (name, passed) => { checks.push({ name, passed: !!passed }); console.log(`${passed ? 'PASS' : 'FAIL'} ${name}`); };
(async () => {
 const browser = await chromium.launch({ channel: 'chrome', headless: true });
 try {
  const context = await browser.newContext({ baseURL: process.argv[2] || 'http://localhost:5298', viewport: { width: 1440, height: 1000 } });
  const page = await context.newPage();
  await page.goto('/Account/Login'); await page.locator('#Email').fill(config.SeedUsers.Admin.Email); await page.locator('#Password').fill(config.SeedUsers.Admin.Password);
  await Promise.all([page.waitForURL(url => !url.pathname.startsWith('/Account/Login')), page.locator('form button[type=submit]').click()]);
  await page.goto('/Books');
  const category = await page.locator('#catalog-category option[value]:not([value=""])').first().getAttribute('value');
  await page.locator('#catalog-category').selectOption(category);
  await page.waitForURL(url => url.searchParams.get('CategoryId') === category);
  check('Category filter updates AJAX URL and count', await page.locator('.filter-active-count').innerText() === '1' && await page.locator('#catalog-results [data-result-count]').getAttribute('data-result-count') !== '0');
  await page.locator('.catalog-filter-drawer summary').click();
  check('Filter drawer opens', await page.locator('.catalog-filter-drawer').getAttribute('open') !== null);
  await page.keyboard.press('Escape');
  check('Escape closes filters and returns focus', await page.locator('.catalog-filter-drawer').getAttribute('open') === null && await page.locator('.catalog-filter-drawer summary').evaluate(e => e === document.activeElement));
  await page.locator('.notification-bell').click();
  check('Notification bell opens persisted notifications', await page.locator('.notification-dropdown').isVisible());
  await page.keyboard.press('Escape');
  await page.locator('.profile-menu-button').click();
  check('User menu opens profile and logout', await page.locator('.profile-dropdown a[href="/Account/Profile"]').isVisible() && await page.locator('.profile-dropdown form[action="/Account/Logout"]').isVisible());
  await page.keyboard.press('Escape');
  const book = await page.locator('#catalog-results a[href*="/Books/Details/"]').first().getAttribute('href');
  await page.goto(book);
  let archivePosts = 0; page.on('request', r => { if (r.method() === 'POST' && r.url().includes('/Books/Archive')) archivePosts++; });
  await page.locator('form[data-confirm] button').click();
  check('Archive opens confirmation', await page.locator('#confirm-dialog').evaluate(e => e.open));
  await page.locator('#confirm-dialog button[value=cancel]').click();
  check('Cancel preserves book without archive request', !await page.locator('#confirm-dialog').evaluate(e => e.open) && archivePosts === 0);
  await page.goto('/Books/Create');
  await page.locator('.book-editor button[type=submit]').click();
  check('Invalid book form retains required-field errors', await page.locator('.input-validation-error').count() > 0 && !await page.locator('.book-editor button[type=submit]').isDisabled());
  await page.goto('/Books');
  const fallback = await page.evaluate(async () => {
   const cover = document.querySelector('.book-jacket');
   const img = document.createElement('img'); img.className = 'book-jacket-image'; img.setAttribute('data-cover-image', ''); cover.append(img); img.src = '/images/missing-cover-test-4e8b.png';
   await new Promise(resolve => { if (img.complete) resolve(); else img.addEventListener('error', resolve, { once: true }); });
   return img.hidden && getComputedStyle(img).display === 'none' && getComputedStyle(cover.querySelector('.book-jacket-art')).display !== 'none';
  });
  check('Failed cover image reveals typographic fallback', fallback);
  await page.goto('/Analytics'); await page.waitForLoadState('networkidle');
  for (const theme of ['light', 'dark']) {
   await page.evaluate(async t => { document.documentElement.dataset.theme = t; await new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve))); for (const chart of Object.values(window.Chart?.instances || {})) chart.update('none'); }, theme);
   check(`Seven real analytics charts ${theme}`, await page.locator('canvas[id^=chart-]').count() === 7);
   await page.screenshot({ path: path.join(output, `Admin-Analytics-1440-${theme}.png`), fullPage: true, animations: 'disabled' });
  }
  const dateSelect = page.locator('#analytics-filter select[name=Range]');
  if (await dateSelect.count()) {
   await dateSelect.selectOption('7');
   await Promise.all([page.waitForResponse(r => r.url().includes('/Analytics/Data') && r.status() === 200), page.locator('#analytics-filter button[type=submit]').click()]);
   check('Analytics period filter refreshes JSON charts', await page.locator('canvas[id^=chart-]').count() === 7);
  }
  await context.close();
  const plainContext = await browser.newContext({ baseURL: process.argv[2] || 'http://localhost:5298', javaScriptEnabled: false });
  const plain = await plainContext.newPage(); await plain.goto('/Books'); await plain.locator('#catalog-category').selectOption(category);
  await Promise.all([plain.waitForURL(url => url.searchParams.get('CategoryId') === category), plain.locator('#catalog-filter button[type=submit]').first().click()]);
  check('Catalog GET filters work without JavaScript', await plain.locator('#catalog-results .book-jacket').count() > 0);
  await plainContext.close();
  const publicContext = await browser.newContext({ baseURL: process.argv[2] || 'http://localhost:5298' });
  const publicPage = await publicContext.newPage();
  for (const route of ['/Account/Login', '/Account/Register', '/Account/ForgotPassword']) for (const width of [360, 1440]) for (const theme of ['light', 'dark']) {
   await publicPage.setViewportSize({ width, height: 1000 }); await publicPage.goto(route);
   await publicPage.evaluate(async t => { document.documentElement.dataset.theme = t; await document.fonts.ready; }, theme);
   await publicPage.screenshot({ path: path.join(output, `public-${route.split('/').pop()}-${width}-${theme}.png`), fullPage: true, animations: 'disabled' });
  }
  await publicContext.close();
 } catch (e) { check('Interaction suite completed', false); console.error(e.message.replace(/relevance=[^\s&]+/g, 'relevance=[redacted]')); }
 finally { await browser.close(); fs.writeFileSync(path.join(output, 'interactions.results.json'), JSON.stringify({ checks, passed: checks.filter(x => x.passed).length, failed: checks.filter(x => !x.passed).length }, null, 2)); }
 console.log(`RESULT ${checks.filter(x => x.passed).length} passed; ${checks.filter(x => !x.passed).length} failed`);
 process.exitCode = checks.some(x => !x.passed) ? 1 : 0;
})();

