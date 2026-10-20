/** Read-only file-picker/crop regression. Never submits an upload or profile form. */
const fs = require('node:fs');
const path = require('node:path');
const assert = require('node:assert/strict');
const { chromium } = require('../.tools/browser/node_modules/playwright');
const root = path.resolve(__dirname, '..');
const baseURL = process.argv[2] || 'http://localhost:5399';
const target = new URL(baseURL);
assert(['localhost', '127.0.0.1'].includes(target.hostname) && target.port === '5399', 'Requires disposable localhost:5399.');
const config = JSON.parse(fs.readFileSync(path.join(root, 'appsettings.Local.json'), 'utf8').replace(/^\uFEFF/, ''));
const output = path.join(root, 'artifacts', 'file-picker');
fs.mkdirSync(output, { recursive: true });
const labels = {
 'kk-KZ': ['Файлды таңдау', 'Файл таңдалмады'],
 'ru-RU': ['Выбрать файл', 'Файл не выбран'],
 'en-US': ['Choose file', 'No file selected']
};
const checks = [], errors = [];
function check(name, passed) {
 checks.push({ name, passed: !!passed });
 console.log(`${passed ? 'PASS' : 'FAIL'} ${name}`);
 assert(passed, name);
}
async function login(browser, role, culture) {
 const context = await browser.newContext({ baseURL, viewport: { width: 360, height: 1000 } });
 await context.addCookies([{ name: '.AspNetCore.Culture', value: `c=${culture}|uic=${culture}`, url: baseURL }]);
 const page = await context.newPage();
 page.on('pageerror', () => errors.push('Browser JavaScript exception'));
 await page.goto('/Account/Login');
 await page.locator('input[name=Email]').fill(config.SeedUsers[role].Email);
 await page.locator('input[name=Password]').fill(config.SeedUsers[role].Password);
 await Promise.all([page.waitForURL(url => !url.pathname.startsWith('/Account/Login')), page.locator('form:has(input[name="Email"]) button[type=submit]').click()]);
 return { context, page };
}
async function pickerLabels(page, culture) {
 return await page.locator('.localized-file-button').innerText() === labels[culture][0]
  && await page.locator('.localized-file-name').innerText() === labels[culture][1];
}
async function geometry(page) {
 return page.evaluate(() => {
  const input = document.querySelector('input[type=file]');
  const wrapper = input.closest('.localized-file-picker');
  const bounds = input.getBoundingClientRect(), outer = wrapper.getBoundingClientRect();
  return document.documentElement.scrollWidth <= innerWidth + 1 && bounds.width >= outer.width - 3 && bounds.height >= outer.height - 3 && getComputedStyle(input).opacity === '0';
 });
}
(async () => {
 const browser = await chromium.launch({ channel: 'chrome', headless: true });
 try {
  for (const culture of Object.keys(labels)) {
   const student = await login(browser, 'Student', culture);
   const page = student.page;
   await page.goto('/Account/Profile');
   check(`${culture} avatar picker translated`, await pickerLabels(page, culture));
   check(`${culture} avatar retains associated label and native focus`, await page.locator('#avatar-file').evaluate(input => input.labels.length > 0 && (input.focus(), document.activeElement === input)));
   check(`${culture} avatar native input covers translated picker at360`, await geometry(page));
   for (const theme of ['light', 'dark']) {
    await page.evaluate(value => document.documentElement.dataset.theme = value, theme);
    // Buttons animate their palette when switching themes.
    await page.waitForTimeout(250);
    check(`${culture} disabled upload follows ${theme} forest palette`, await page.locator('[data-avatar-confirm]').evaluate(button => {
     const actual = getComputedStyle(button);
     const probe = document.createElement('span'); probe.style.background = 'var(--color-primary)'; document.body.append(probe);
     const expected = getComputedStyle(probe).backgroundColor; probe.remove();
     return button.disabled && actual.backgroundColor === expected;
    }));
   }
   const image = await page.evaluate(() => {
    const canvas = document.createElement('canvas'); canvas.width = 800; canvas.height = 500;
    const context = canvas.getContext('2d'); context.fillStyle = '#18382f'; context.fillRect(0, 0, 800, 500);
    return canvas.toDataURL('image/png').split(',')[1];
   });
   const [chooser] = await Promise.all([page.waitForEvent('filechooser'), page.locator('.localized-file-picker').click()]);
   await chooser.setFiles({ name: 'selected-crop.png', mimeType: 'image/png', buffer: Buffer.from(image, 'base64') });
   await page.waitForSelector('.avatar-editor:not([hidden])');
   check(`${culture} avatar selected filename and live crop`, await page.locator('.localized-file-name').innerText() === 'selected-crop.png' && !await page.locator('[data-avatar-confirm]').isDisabled());
   await page.locator('#avatar-zoom').evaluate(input => { input.value = '1.7'; input.dispatchEvent(new Event('input', { bubbles: true })); });
   await page.locator('[data-avatar-canvas]').press('ArrowRight');
   check(`${culture} crop still updates normalized coordinates`, Number(await page.locator('[name=CropSize]').inputValue()) < 1 && Number(await page.locator('[name=CropX]').inputValue()) > 0);
   await page.locator('[data-avatar-cancel]').click();
   check(`${culture} avatar cancel clears file and localized filename`, await pickerLabels(page, culture) && await page.locator('#avatar-file').inputValue() === '' && await page.locator('.avatar-editor').isHidden());
   await page.locator('#avatar-file').setInputFiles({ name: 'invalid.txt', mimeType: 'text/plain', buffer: Buffer.from('invalid') });
   check(`${culture} invalid image clears selected filename`, await pickerLabels(page, culture) && await page.locator('#avatar-file').inputValue() === '');
   await page.screenshot({ path: path.join(output, `${culture}-profile360.png`), fullPage: true });
   await student.context.close();
   const staff = await login(browser, 'Librarian', culture);
   await staff.page.goto('/DigitalLibrary/Upload');
   check(`${culture} digital picker translated`, await pickerLabels(staff.page, culture));
   check(`${culture} digital input keeps required validation and label`, await staff.page.locator('input[type=file]').evaluate(input => input.required && input.labels.length > 0 && input.name === 'File'));
   check(`${culture} digital picker has no overflow at360`, await geometry(staff.page));
   await staff.page.locator('input[type=file]').setInputFiles({ name: 'selected-edition.pdf', mimeType: 'application/pdf', buffer: Buffer.from('%PDF-1.7\nPreview only; never submitted.') });
   check(`${culture} digital selected filename visible`, await staff.page.locator('.localized-file-name').innerText() === 'selected-edition.pdf');
   await staff.page.locator('#digital-asset-form').evaluate(form => form.reset());
   await staff.page.waitForFunction(() => !document.querySelector('input[type=file]').files.length);
   await staff.page.evaluate(() => new Promise(resolve => requestAnimationFrame(resolve)));
   check(`${culture} native form reset clears digital filename`, await pickerLabels(staff.page, culture));
   await staff.page.screenshot({ path: path.join(output, `${culture}-digital-upload360.png`), fullPage: true });
   await staff.context.close();
  }
  check('No picker JavaScript exceptions', errors.length === 0);
 } catch (error) {
  if (!checks.length || checks.at(-1).passed) checks.push({ name: `Picker suite completed (${error.name})`, passed: false });
 } finally {
  await browser.close();
  const result = { checks, passed: checks.filter(check => check.passed).length, failed: checks.filter(check => !check.passed).length, errors };
  fs.writeFileSync(path.join(output, 'results.json'), JSON.stringify(result, null, 2));
  console.log(`RESULT ${result.passed} passed; ${result.failed} failed`);
  process.exitCode = result.failed ? 1 : 0;
 }
})();
