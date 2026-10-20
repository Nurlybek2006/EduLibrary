/** Read-only visual regression of the catalog, digital library and profile.
 * Uses only disposable5399 and existing login sessions; submits no profile/data forms.
 */
const fs = require('node:fs');
const path = require('node:path');
const assert = require('node:assert/strict');
const { chromium } = require('../.tools/browser/node_modules/playwright');
const root = path.resolve(__dirname, '..');
const baseURL = process.argv[2] || 'http://localhost:5399';
const target = new URL(baseURL);
assert(['localhost', '127.0.0.1'].includes(target.hostname) && target.port === '5399', 'Visual suite requires disposable localhost:5399.');
const config = JSON.parse(fs.readFileSync(path.join(root, 'appsettings.Local.json'), 'utf8').replace(/^\uFEFF/, ''));
const output = path.join(root, 'artifacts', 'localized-visual');
fs.mkdirSync(output, { recursive: true });
const checks = [], errors = [], pages = [];
function check(name, passed, details) {
 checks.push({ name, passed: !!passed, ...(details ? { details } : {}) });
 console.log(`${passed ? 'PASS' : 'FAIL'} ${name}${details ? ' ' + JSON.stringify(details) : ''}`);
}
const labels = {
 'kk-KZ': { profile: 'Менің профилім', digital: 'Электрондық кітапхана' },
 'ru-RU': { profile: 'Мой профиль', digital: 'Электронная библиотека' },
 'en-US': { profile: 'My profile', digital: 'Digital library' }
};
async function settle(page) {
 await page.waitForLoadState('load');
 await page.evaluate(async () => { await document.fonts.ready; await new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve))); });
}
(async () => {
 const browser = await chromium.launch({ channel: 'chrome', headless: true });
 try {
  for (const culture of Object.keys(labels)) {
   const context = await browser.newContext({ baseURL, viewport: { width: 1440, height: 1000 } });
   await context.addCookies([{ name: '.AspNetCore.Culture', value: `c=${culture}|uic=${culture}`, url: baseURL }]);
   const page = await context.newPage();
   page.on('pageerror', () => errors.push({ culture, message: 'Browser JavaScript exception' }));
   await page.goto('/Account/Login');
   await page.locator('input[name=Email]').fill(config.SeedUsers.Student.Email);
   await page.locator('input[name=Password]').fill(config.SeedUsers.Student.Password);
   await Promise.all([page.waitForURL(url => !url.pathname.startsWith('/Account/Login')), page.locator('form:has(input[name="Email"]) button[type=submit]').click()]);
   check(`${culture} explicit cookie survives sign in`, await page.locator('html').getAttribute('data-culture') === culture);
   for (const route of ['/Books', '/DigitalLibrary', '/Account/Profile']) {
    for (const width of [360, 1440]) for (const theme of ['light', 'dark']) {
     await page.setViewportSize({ width, height: 1000 });
     const response = await page.goto(route);
     await page.evaluate(value => document.documentElement.dataset.theme = value, theme);
     await settle(page);
     const metrics = await page.evaluate(() => ({
      width: innerWidth, scrollWidth: document.documentElement.scrollWidth,
      culture: document.documentElement.dataset.culture,
      selectedLanguage: document.getElementById('site-language')?.value,
      formWidth: document.querySelector('.profile-content')?.getBoundingClientRect().width,
      firstCover: document.querySelector('.book-jacket')?.getBoundingClientRect().top,
      cards: document.querySelectorAll('.book-jacket').length,
      avatars: document.querySelectorAll('.user-avatar').length,
      brokenAvatars: [...document.querySelectorAll('img[data-user-avatar]')].filter(image => image.complete && image.naturalWidth === 0 && !image.hidden).length
     }));
     const good = response.status() === 200 && metrics.scrollWidth <= width + 1 && metrics.culture === culture && metrics.selectedLanguage === culture && metrics.brokenAvatars === 0;
     check(`${culture} ${route} ${width} ${theme} layout and culture`, good, good ? undefined : metrics);
     if (route === '/Account/Profile') check(`${culture} profile heading and avatar ${width} ${theme}`, (await page.locator('h1').innerText()) === labels[culture].profile && await page.locator('.profile-current-avatar .user-avatar').count() === 1 && await page.locator('[data-avatar-editor]').count() === 1);
     if (route === '/DigitalLibrary') check(`${culture} digital heading and registered books ${width} ${theme}`, (await page.locator('h1').innerText()) === labels[culture].digital && await page.locator('#digital-book-grid .digital-card').count() > 0);
     await page.screenshot({ path: path.join(output, `${culture}-${route.replaceAll('/', '-').replace(/^-/, '')}-${width}-${theme}.png`), fullPage: true, animations: 'disabled' });
     pages.push({ culture, route, width, theme, ...metrics });
    }
   }
   await context.close();
  }
  check('No localized-page JavaScript exceptions', errors.length === 0);
 } catch (error) {
  // Never write credentials or token-bearing URLs into the report.
  check('Localized visual suite completed', false, { error: error.name || 'Browser failure' });
 } finally {
  await browser.close();
  fs.writeFileSync(path.join(output, 'results.json'), JSON.stringify({ checks, pages, errors, passed: checks.filter(c => c.passed).length, failed: checks.filter(c => !c.passed).length }, null, 2));
 }
 console.log(`RESULT ${checks.filter(c => c.passed).length} passed; ${checks.filter(c => !c.passed).length} failed`);
 process.exitCode = checks.some(c => !c.passed) ? 1 : 0;
})();
