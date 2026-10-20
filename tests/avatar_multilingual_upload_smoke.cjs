/** Actual fractional-crop upload in every UI culture, only on disposable5399. */
const fs = require('node:fs');
const path = require('node:path');
const assert = require('node:assert/strict');
const { chromium } = require('../.tools/browser/node_modules/playwright');
const root = path.resolve(__dirname, '..');
const baseURL = process.argv[2] || 'http://localhost:5399';
const target = new URL(baseURL);
assert(['localhost', '127.0.0.1'].includes(target.hostname) && target.port === '5399', 'Avatar writes require disposable localhost:5399.');
const config = JSON.parse(fs.readFileSync(path.join(root, 'appsettings.Local.json'), 'utf8').replace(/^\uFEFF/, ''));
const output = path.join(root, 'artifacts', 'avatar-multilingual');
fs.mkdirSync(output, { recursive: true });
const checks = [], errors = [];
function check(name, passed) {
 checks.push({ name, passed: !!passed });
 console.log(`${passed ? 'PASS' : 'FAIL'} ${name}`);
 assert(passed, name);
}
(async () => {
 const browser = await chromium.launch({ channel: 'chrome', headless: true });
 try {
  for (const culture of ['kk-KZ', 'ru-RU', 'en-US']) {
   const context = await browser.newContext({ baseURL, viewport: { width: 1440, height: 1000 } });
   await context.addCookies([{ name: '.AspNetCore.Culture', value: `c=${culture}|uic=${culture}`, url: baseURL }]);
   const page = await context.newPage();
   page.on('pageerror', () => errors.push({ culture, error: 'Browser JavaScript exception' }));
   await page.goto('/Account/Login');
   await page.locator('[name=Email]').fill(config.SeedUsers.Student.Email);
   await page.locator('[name=Password]').fill(config.SeedUsers.Student.Password);
   await Promise.all([page.waitForURL(url => !url.pathname.startsWith('/Account/Login')), page.locator('form:has(input[name="Email"]) button[type=submit]').click()]);
   await page.goto('/Account/Profile');
   check(`${culture} actual upload starts in requested culture`, await page.locator('html').getAttribute('data-culture') === culture);
   const image = await page.evaluate(() => {
    const canvas = document.createElement('canvas'); canvas.width = 800; canvas.height = 500;
    const paint = canvas.getContext('2d'); paint.fillStyle = '#18382f'; paint.fillRect(0, 0, 800, 500);
    paint.fillStyle = '#c56e4d'; paint.fillRect(400, 0, 400, 500);
    return canvas.toDataURL('image/png').split(',')[1];
   });
   await page.locator('#avatar-file').setInputFiles({ name: `crop-${culture}.png`, mimeType: 'image/png', buffer: Buffer.from(image, 'base64') });
   await page.waitForSelector('.avatar-editor:not([hidden])');
   for (const [selector, value] of [['#avatar-zoom', '1.7'], ['#avatar-position-x', '70'], ['#avatar-position-y', '35']])
    await page.locator(selector).evaluate((input, selected) => { input.value = selected; input.dispatchEvent(new Event('input', { bubbles: true })); }, value);
   const coordinates = await page.locator('[data-avatar-editor]').evaluate(form => [form.elements.CropX.value, form.elements.CropY.value, form.elements.CropSize.value]);
   check(`${culture} browser computes three noninteger dot fractions`, coordinates.every(value => /^0\.\d{10}$/.test(value) && Number(value) > 0 && Number(value) < 1));
   await page.locator('[data-avatar-editor]').evaluate(form => form.addEventListener('formdata', event => {
    sessionStorage.setItem('avatar-crop-wire-check', JSON.stringify(['CropX', 'CropY', 'CropSize'].map(name => event.formData.get(name))));
   }, { once: true }));
   const posted = page.waitForRequest(request => new URL(request.url()).pathname === '/Account/UploadAvatar' && request.method() === 'POST');
   await Promise.all([page.waitForNavigation({ waitUntil: 'load' }), page.locator('[data-avatar-confirm]').click()]);
   const request = await posted;
   const sent = await page.evaluate(() => { const values = JSON.parse(sessionStorage.getItem('avatar-crop-wire-check') || '[]'); sessionStorage.removeItem('avatar-crop-wire-check'); return values; });
   check(`${culture} native multipart contains original dot fractions`, request.headers()['content-type']?.startsWith('multipart/form-data;') && JSON.stringify(sent) === JSON.stringify(coordinates));
   const imageNode = page.locator('#profile-photo img[data-user-avatar]').first();
   await imageNode.waitFor({ state: 'visible' });
   const url = await imageNode.getAttribute('src');
   check(`${culture} successful upload persists opaque avatar`, /^\/Account\/Avatar\/[a-f0-9]{32}\.webp$/.test(url));
   const dimensions = await page.evaluate(async imageUrl => {
    const response = await fetch(imageUrl);
    const bitmap = await createImageBitmap(await response.blob());
    const result = { type: response.headers.get('content-type'), width: bitmap.width, height: bitmap.height };
    bitmap.close(); return result;
   }, url);
   check(`${culture} server decoded crop into actual256WebP`, dimensions.type?.startsWith('image/webp') && dimensions.width === 256 && dimensions.height === 256);
   await page.reload();
   check(`${culture} SQL avatar survives profile refresh`, await page.locator('#profile-photo img[data-user-avatar]').first().getAttribute('src') === url && await page.locator('html').getAttribute('data-culture') === culture);
   const navigation = page.waitForNavigation({ waitUntil: 'load' });
   await page.locator('.avatar-remove-form button[type=submit]').click();
   if (await page.locator('#confirm-dialog[open]').count()) await page.locator('#confirm-dialog button[value=confirm]').click();
   await navigation;
   check(`${culture} removal restores initials`, await page.locator('#profile-photo img[data-user-avatar]').count() === 0);
   check(`${culture} removal deletes generated file`, (await context.request.get(url)).status() === 404);
   await context.close();
  }
  check('No multilingual upload JavaScript exceptions', errors.length === 0);
 } catch (error) {
  if (!checks.length || checks.at(-1).passed) checks.push({ name: `Multilingual avatar suite completed (${error.name})`, passed: false });
 } finally {
  await browser.close();
  const result = { checks, passed: checks.filter(check => check.passed).length, failed: checks.filter(check => !check.passed).length, errors };
  fs.writeFileSync(path.join(output, 'results.json'), JSON.stringify(result, null, 2));
  console.log(`RESULT ${result.passed} passed; ${result.failed} failed`);
  process.exitCode = result.failed ? 1 : 0;
 }
})();
