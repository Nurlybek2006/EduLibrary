/** Read-only editorial UI verification. Installs no app runtime dependency.
 * Setup: npm install --prefix .tools/browser --save-exact playwright@1.64.1 --ignore-scripts
 * Run: node tests/editorial_ui_smoke.cjs [http://localhost:5298]
 * Demo credentials are read privately; screenshots and JSON go to ignored artifacts/.
 */
const fs = require('node:fs');
const path = require('node:path');
const assert = require('node:assert/strict');
const { chromium } = require('../.tools/browser/node_modules/playwright');
const root = path.resolve(__dirname, '..');
const baseURL = process.argv[2] || 'http://localhost:5298';
const config = JSON.parse(fs.readFileSync(path.join(root, 'appsettings.Local.json'), 'utf8').replace(/^\uFEFF/, ''));
const output = path.join(root, 'artifacts', 'editorial-ui');
fs.mkdirSync(output, { recursive: true });
const checks = [], pages = [], errors = [];
function record(name, passed, detail) { checks.push({ name, passed: !!passed, ...(detail ? { detail } : {}) }); console.log(`${passed ? 'PASS' : 'FAIL'} ${name}${detail ? ' ' + JSON.stringify(detail) : ''}`); }
async function settle(page) { await page.waitForLoadState('load'); await page.evaluate(async () => { await document.fonts.ready; await new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve))); }); }
async function login(context, role) {
 const page = await context.newPage();
 await page.goto('/Account/Login');
 await page.locator('[name=Email]').fill(config.SeedUsers[role].Email);
 await page.locator('[name=Password]').fill(config.SeedUsers[role].Password);
 await Promise.all([page.waitForURL(url => !url.pathname.startsWith('/Account/Login')), page.locator('form button[type=submit]').click()]);
 record(`${role} sign in`, !page.url().includes('/Account/Login'));
 return page;
}
(async () => {
 const browser = await chromium.launch({ channel: 'chrome', headless: true });
 try {
  const publicContext = await browser.newContext({ baseURL, viewport: { width: 1440, height: 1000 } });
  const publicPage = await publicContext.newPage();
  await publicPage.goto('/Account/Login'); await settle(publicPage);
  record('Light is default', await publicPage.locator('html').getAttribute('data-theme') === 'light');
  await publicPage.locator('[data-password-toggle=Password]').click();
  record('Password visibility toggle', await publicPage.locator('#Password').getAttribute('type') === 'text');
  await publicPage.locator('[data-password-toggle=Password]').click();
  record('Password returns to hidden', await publicPage.locator('#Password').getAttribute('type') === 'password');
  await publicPage.locator('#theme-toggle').click(); await publicPage.reload();
  record('Theme persists after reload', await publicPage.locator('html').getAttribute('data-theme') === 'dark');
  const publicRoutes = ['/Account/Login', '/Account/Register', '/Account/ForgotPassword'];
  for (const route of publicRoutes) for (const width of [360, 1440]) for (const theme of ['light', 'dark']) {
   await publicPage.setViewportSize({ width, height: 1000 }); await publicPage.goto(route); await publicPage.evaluate(t => document.documentElement.dataset.theme = t, theme); await settle(publicPage);
   const overflow = await publicPage.evaluate(() => document.documentElement.scrollWidth > innerWidth + 1);
   record(`Anonymous ${route} ${width} ${theme} overflow`, !overflow);
   await publicPage.screenshot({ path: path.join(output, `public-${route.split('/').pop()}-${width}-${theme}.png`), fullPage: true, animations: 'disabled' });
  }
  await publicContext.close();
  for (const role of ['Student', 'Admin', 'Librarian']) {
   const context = await browser.newContext({ baseURL, viewport: { width: 1440, height: 1000 } });
   const page = await login(context, role);
   page.on('pageerror', error => errors.push({ role, error: error.message }));
   await page.goto('/Books'); await settle(page);
   const bookLink = await page.locator('#catalog-results a[href*="/Books/Details/"]').first().getAttribute('href');
   assert(bookLink, 'Catalog should contain a real book link.');
   const routes = role === 'Student' ? ['/Books', bookLink, '/ReadingSpace', '/Recommendations', '/Loans', '/Fines', '/Account/Profile', '/Notifications'] : ['/Dashboard', '/Books', bookLink, '/Analytics', '/Reports', '/Books/Create', '/Copies/Lookup', '/Loans/Create', '/Students', '/Notifications', '/Account/Profile'];
   for (const route of routes) for (const width of [360, 390, 768, 1024, 1440, 1920]) for (const theme of ['light', 'dark']) {
    if (role === 'Librarian' && !['/Dashboard', '/Books', '/Analytics', '/Reports', '/Loans/Create'].includes(route)) continue;
    await page.setViewportSize({ width, height: 1000 });
    const response = await page.goto(route); await settle(page);
    await page.evaluate(t => document.documentElement.dataset.theme = t, theme);
    const measure = await page.evaluate(() => {
     const offenders = [...document.querySelectorAll('body *')].filter(e => { const b = e.getBoundingClientRect(); return b.width && (b.right > innerWidth + 2 || b.left < -2) && getComputedStyle(e).position !== 'fixed' && !e.closest('.table-responsive,.management-rail,.book-jacket,.curated-nav'); }).slice(0, 8).map(e => ({ tag: e.tagName, class: String(e.className).slice(0, 120), width: Math.round(e.getBoundingClientRect().width) }));
     const card = document.querySelector('#catalog-results .book-jacket');
     const grid = document.querySelector('#book-grid');
     return { overflow: document.documentElement.scrollWidth > innerWidth + 1, scrollWidth: document.documentElement.scrollWidth, width: innerWidth, offenders, coverY: card ? Math.round(card.getBoundingClientRect().top) : null, columns: grid ? getComputedStyle(grid).gridTemplateColumns.split(' ').length : null, staff: !!document.querySelector('.management-rail'), reader: !!document.querySelector('.reader-navigation'), font: document.fonts.check('500 16px Manrope', '\u04D8\u0492\u049A\u04A2\u04E8\u04B0\u04AE\u04BA\u0406') };
    });
    const good = response.status() === 200 && !page.url().includes('/Account/AccessDenied') && !measure.overflow && measure.font && measure.staff === (role !== 'Student') && measure.reader === (role === 'Student');
    record(`${role} ${route.split('?')[0]} ${width} ${theme}`, good, good ? null : { status: response.status(), ...measure });
    if (route === '/Books') {
     record(`${role} visible catalog covers ${width} ${theme}`, measure.coverY !== null && measure.coverY < 650, { coverY: measure.coverY, columns: measure.columns });
     if (width === 360) record(`${role} two mobile catalog columns ${theme}`, measure.columns === 2);
     if (width === 1440) record(`${role} four desktop catalog columns ${theme}`, measure.columns === 4);
    }
    if ((width === 360 || width === 1440) && role !== 'Librarian') {
     const label = route.split('?')[0].replaceAll('/', '-').replace(/^-/, '');
     await page.evaluate(() => { for (const chart of Object.values(window.Chart?.instances || {})) chart.update('none'); });
     await page.screenshot({ path: path.join(output, `${role}-${label}-${width}-${theme}.png`), fullPage: true, animations: 'disabled' });
    }
    pages.push({ role, route: route.split('?')[0], width, theme, ...measure });
   }
   // Native GET filters, AJAX search, history and view switching retain their contracts.
   await page.setViewportSize({ width: 1440, height: 1000 }); await page.goto('/Books'); await settle(page);
   const search = page.locator('#catalog-filter [name=Search]');
   await search.fill('edulibrary_visual_no_match_7c8d');
   await page.waitForURL('**/Books?*Search=edulibrary_visual_no_match_7c8d*'); await settle(page);
   record(`${role} AJAX empty search`, await page.locator('#catalog-results .book-jacket').count() === 0);
   await search.fill(''); await page.waitForURL(url => !url.searchParams.get('Search')); await settle(page);
   record(`${role} AJAX search restore`, await page.locator('#catalog-results .book-jacket').count() > 0);
   await page.locator('[data-catalog-view=list]').click();
   record(`${role} list view`, await page.locator('#catalog-results').evaluate(e => e.classList.contains('list-view')));
   await page.locator('[data-catalog-view=grid]').click();
   await page.setViewportSize({ width: 360, height: 1000 }); await page.locator('#menu-toggle').click();
   record(`${role} mobile navigation opens`, await page.locator('#menu-toggle').getAttribute('aria-expanded') === 'true');
   await page.keyboard.press('Escape');
   record(`${role} mobile navigation closes`, await page.locator('#menu-toggle').getAttribute('aria-expanded') === 'false');
   if (role === 'Student') {
    await page.goto('/Recommendations'); await settle(page);
    const suggested = page.locator('a[href*="relevance="]').first();
    if (await suggested.count()) { await suggested.click(); await settle(page); record('Signed recommendation detail context', await page.locator('.recommendation-context').count() === 1); }
    await page.goto('/ReadingSpace'); record('Reading space Student role', !page.url().includes('AccessDenied'));
   } else {
    await page.goto('/ReadingSpace'); record(`${role} reading space role boundary`, page.url().includes('AccessDenied'));
   }
   await context.close();
  }
  record('No browser JavaScript errors', errors.length === 0, errors.length ? errors : null);
 } catch (error) { record('UI suite completed', false, { error: error.message }); }
 finally { await browser.close(); fs.writeFileSync(path.join(output, 'results.json'), JSON.stringify({ checks, pages, errors, passed: checks.filter(c => c.passed).length, failed: checks.filter(c => !c.passed).length }, null, 2)); }
 console.log(`RESULT ${checks.filter(c => c.passed).length} passed; ${checks.filter(c => !c.passed).length} failed`);
 process.exitCode = checks.some(c => !c.passed) ? 1 : 0;
})();
