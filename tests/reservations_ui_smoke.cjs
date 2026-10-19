/** Successful reservation UI workflows ONLY on the root-created disposable app.
 * node tests/reservations_ui_smoke.cjs http://localhost:5399
 * Requires ignored artifacts/reservation-ui-fixture.json: {bookId,students:[{email,password},{email,password}]}.
 * Never creates/deletes a database. Never logs credentials, pickup codes or antiforgery tokens.
 */
const fs = require('node:fs');
const path = require('node:path');
const assert = require('node:assert/strict');
const { chromium } = require('../.tools/browser/node_modules/playwright');
const root = path.resolve(__dirname, '..');
const baseURL = process.argv[2] || 'http://localhost:5399';
const target = new URL(baseURL);
assert(['localhost', '127.0.0.1'].includes(target.hostname) && target.port === '5399', 'Reservation writes require the disposable app on port 5399.');
const config = JSON.parse(fs.readFileSync(path.join(root, 'appsettings.Local.json'), 'utf8').replace(/^\uFEFF/, ''));
const fixture = JSON.parse(fs.readFileSync(path.join(root, 'artifacts', 'reservation-ui-fixture.json'), 'utf8').replace(/^\uFEFF/, ''));
assert(Number.isInteger(fixture.bookId) && fixture.students?.length === 2, 'Disposable reservation fixtures are required.');
const output = path.join(root, 'artifacts', 'reservation-ui');
fs.mkdirSync(output, { recursive: true });
const checks = [], pageErrors = [];
function check(name, passed) { checks.push({ name, passed: !!passed }); console.log(`${passed ? 'PASS' : 'FAIL'} ${name}`); assert(passed, name); }
async function settle(page) { await page.waitForLoadState('load'); await page.evaluate(async () => { await document.fonts.ready; await new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve))); }); }
async function login(browser, credentials) {
    const context = await browser.newContext({ baseURL, viewport: { width: 1440, height: 1000 } });
    const page = await context.newPage();
    page.on('pageerror', () => pageErrors.push('browser script error'));
    await page.goto('/Account/Login');
    await page.locator('[name=Email]').fill(credentials.email ?? credentials.Email);
    await page.locator('[name=Password]').fill(credentials.password ?? credentials.Password);
    await Promise.all([page.waitForURL(url => !url.pathname.startsWith('/Account/Login')), page.locator('form button[type=submit]').click()]);
    return { context, page };
}
async function submit(page, selector) {
    await Promise.all([
        page.waitForNavigation({ waitUntil: 'load' }),
        (async () => {
            await page.locator(selector).click();
            const confirm = page.locator('#confirm-dialog[open] button[value=confirm]');
            if (await confirm.count()) await confirm.click();
        })()
    ]);
    await settle(page);
}
async function status(page, id, expected, label) {
    const response = await page.goto(`/Reservations/Details/${id}`); await settle(page);
    check(label, response.status() === 200 && await page.locator('#reservation-details').getAttribute('data-reservation-status') === expected);
}
async function create(page, expected) {
    const response = await page.goto(`/Reservations/Create?bookId=${fixture.bookId}`); await settle(page);
    check(`${expected} booking form loads`, response.status() === 200 && await page.locator('#reservation-create-form').count() === 1);
    const queued = await page.locator('#reservation-create-form').getAttribute('data-queue') === 'true';
    check(`${expected} booking mode matches physical availability`, queued === (expected === 'Queued'));
    await page.locator('#PickupLocationId').selectOption({ index: 1 });
    if (!queued) {
        const dates = await page.locator('#PickupDate option').evaluateAll(nodes => nodes.filter(e => e.value && !e.disabled).map(e => e.value));
        check('Booking offers configured working dates', dates.length > 0);
        await page.locator('#PickupDate').selectOption(dates[Math.min(1, dates.length - 1)]);
        await page.waitForFunction(() => document.querySelector('#reservation-create-form').getAttribute('aria-busy') !== 'true' && !document.querySelector('#PickupTime').disabled);
        const slots = await page.locator('#PickupTime option').evaluateAll(nodes => nodes.filter(e => e.value && !e.disabled).map(e => e.value));
        check('Changing date refreshes real available appointment slots', slots.length > 0 && slots.every(s => /^\d{2}:\d{2}$/.test(s)));
        await page.locator('#PickupTime').selectOption(slots[0]);
        check('Complete booking enables confirmation', await page.locator('#reservation-submit').isEnabled());
    } else check('Queue form does not pretend an appointment is allocated', await page.locator('#PickupDate,#PickupTime').count() === 0);
    await submit(page, '#reservation-submit');
    const id = Number(await page.locator('#reservation-details').getAttribute('data-reservation-id'));
    check(`${expected} booking persists and redirects to owned details`, Number.isInteger(id) && id > 0 && await page.locator('#reservation-details').getAttribute('data-reservation-status') === expected);
    return id;
}
async function cancel(page, id) { await page.goto(`/Reservations/Details/${id}`); await submit(page, '#reservation-cancel-submit'); check('Cancellation persists in reservation history', await page.locator('#reservation-details').getAttribute('data-reservation-status') === 'Cancelled'); }
async function layouts(page, role, routes) {
    for (const [label, route] of routes) for (const width of [360, 390, 768, 1024, 1440, 1920]) for (const theme of ['light', 'dark']) {
        await page.setViewportSize({ width, height: 1000 });
        const response = await page.goto(route); await settle(page);
        await page.evaluate(value => document.documentElement.dataset.theme = value, theme);
        const dimensions = await page.evaluate(() => ({ overflow: document.documentElement.scrollWidth > innerWidth + 1, staff: !!document.querySelector('.management-rail'), reader: !!document.querySelector('.reader-navigation') }));
        check(`${role} ${label} ${width} ${theme} layout`, response.status() === 200 && !dimensions.overflow && dimensions.staff === (role === 'Staff') && dimensions.reader === (role === 'Student'));
        if (width === 360 || width === 1440) {
            await page.locator('.reservation-qr,#reservation-pickup-code-value').evaluateAll(nodes => nodes.forEach(node => node.style.visibility = 'hidden'));
            await page.screenshot({ path: path.join(output, `${role}-${label}-${width}-${theme}.png`), fullPage: true, animations: 'disabled' });
        }
    }
    await page.setViewportSize({ width: 1440, height: 1000 });
}
(async () => {
    const browser = await chromium.launch({ channel: 'chrome', headless: true });
    let failed = false;
    try {
        const first = await login(browser, fixture.students[0]);
        const second = await login(browser, fixture.students[1]);
        const staff = await login(browser, config.SeedUsers.Librarian);
        const publicContext = await browser.newContext({ baseURL });
        check('Two independent readers and librarian sign in', ![first, second, staff].some(s => s.page.url().includes('/Account/Login')));

        await first.page.goto(`/Books/Details/${fixture.bookId}`);
        check('Real book details exposes reservation action', await first.page.locator('.book-reserve-action[href*="/Reservations/Create"]').count() > 0);
        const id = await create(first.page, 'Pending');
        const code = (await first.page.locator('#reservation-pickup-code-value').textContent()).trim();
        const ownerVersion = await first.page.locator('#reservation-cancel-form [name=rowVersion]').inputValue();
        const ownerToken = await first.page.locator('#reservation-cancel-form [name=__RequestVerificationToken]').inputValue();
        check('Owner sees opaque pickup code without personal information', /^ELR-[0-9A-F]{32}$/.test(code));
        const ownQr = await first.context.request.get(`/Reservations/Qr/${id}`);
        check('Owner QR endpoint returns SVG without cache storage', ownQr.status() === 200 && /image\/svg\+xml/.test(ownQr.headers()['content-type'] || '') && /no-store/i.test(ownQr.headers()['cache-control'] || '') && (await ownQr.text()).includes('<svg'));
        const publicQr = await publicContext.request.get(`/Reservations/Qr/${id}`);
        check('Anonymous QR access requires sign in', new URL(publicQr.url()).pathname === '/Account/Login');
        const foreignQr = await second.context.request.get(`/Reservations/Qr/${id}`);
        check('Foreign reader cannot retrieve reservation QR', foreignQr.status() === 404);
        const foreignDetails = await second.context.request.get(`/Reservations/Details/${id}`);
        check('Foreign reader cannot inspect reservation details', foreignDetails.status() === 404);
        const noCsrf = await first.context.request.post(`/Reservations/Cancel/${id}`, { form: { rowVersion: 'AAAAAAAAAAA=' } });
        check('Reservation mutations reject missing antiforgery token', noCsrf.status() === 400);
        const forbidden = await first.context.request.get(`/Reservations/Prepare/${id}`);
        check('Student cannot access staff preparation route', forbidden.status() === 403 || new URL(forbidden.url()).pathname === '/Account/AccessDenied');
        await first.page.goto('/ReadingSpace');
        check('Reader workspace shows real reservation summary', await first.page.locator('#reservation-summary').count() === 1);
        await layouts(first.page, 'Student', [['Reservations', '/Reservations'], ['Pending', `/Reservations/Details/${id}`], ['ReadingSpace', '/ReadingSpace']]);

        const queued = await create(second.page, 'Queued');
        check('Queued reader sees position in FIFO', await second.page.locator('.reservation-workflow-note').innerText().then(text => /1/.test(text)));
        const otherToken = await second.page.locator('#reservation-cancel-form [name=__RequestVerificationToken]').inputValue();
        const foreignCancel = await second.context.request.post(`/Reservations/Cancel/${id}`, { form: { rowVersion: ownerVersion, __RequestVerificationToken: otherToken } });
        check('Foreign reader cannot cancel another reservation even with valid CSRF', foreignCancel.status() === 404);
        const studentFulfill = await second.context.request.post('/Reservations/Fulfill', { form: { Id: String(id), PickupCode: code, RowVersion: ownerVersion, DueDate: '2030-01-01', __RequestVerificationToken: otherToken } });
        check('Student POST cannot perform staff physical handover', studentFulfill.status() === 403 || new URL(studentFulfill.url()).pathname === '/Account/AccessDenied');
        const duplicate = await first.context.request.post('/Reservations/Create', { form: { BookId: String(fixture.bookId), JoinQueue: 'true', PickupLocationId: 'main', __RequestVerificationToken: ownerToken } });
        check('Duplicate active booking produces business validation without redirect', duplicate.status() === 200 && new URL(duplicate.url()).pathname === '/Reservations/Create' && (await duplicate.text()).includes('validation-summary-errors'));
        const badSlots = await first.context.request.get('/Reservations/Slots?pickupDate=malformed&pickupLocationId=main');
        check('Malformed slot request is rejected as JSON bad request', badSlots.status() === 400 && /json/.test(badSlots.headers()['content-type'] || ''));
        const staffCreate = await staff.context.request.get(`/Reservations/Create?bookId=${fixture.bookId}`);
        check('Staff does not create a student reservation using its own identity', staffCreate.status() === 403 || new URL(staffCreate.url()).pathname === '/Account/AccessDenied');
        await layouts(second.page, 'Student', [['Queued', `/Reservations/Details/${queued}`]]);
        await status(staff.page, id, 'Pending', 'Staff sees saved Pending reservation');
        check('Pending reservation has prepare action and no handover form', await staff.page.locator('#reservation-prepare-link').count() === 1 && await staff.page.locator('#reservation-fulfill-form').count() === 0);
        await layouts(staff.page, 'Staff', [['Reservations', '/Reservations'], ['Prepare', `/Reservations/Prepare/${id}`], ['Lookup', '/Reservations/Lookup']]);
        await staff.page.goto(`/Reservations/Prepare/${id}`);
        check('Staff chooses an actual physical copy', await staff.page.locator('#BookCopyId option[value]:not([value=""])').count() === 1);
        await submit(staff.page, '#reservation-ready-form button[type=submit]');
        check('Preparation persists ReadyForPickup', await staff.page.locator('#reservation-details').getAttribute('data-reservation-status') === 'ReadyForPickup');
        check('Staff must enter physically presented pickup code', await staff.page.locator('#reservation-pickup-code').inputValue() === '');
        await layouts(staff.page, 'Staff', [['Ready', `/Reservations/Details/${id}`]]);

        await staff.page.goto(`/Reservations/Details/${id}`);
        await staff.page.locator('#reservation-pickup-code').fill('ELR-' + '0'.repeat(32));
        await submit(staff.page, '#reservation-fulfill-submit');
        check('Wrong presented code produces visible validation and no loan', await staff.page.locator('#reservation-details').getAttribute('data-reservation-status') === 'ReadyForPickup' && (await staff.page.locator('#reservation-fulfill-form [role=alert]').innerText()).trim().length > 0);
        await staff.page.goto('/Reservations/Lookup');
        check('Code lookup is a CSRF-protected POST form', await staff.page.locator('#copy-code-form').getAttribute('method') === 'post' && await staff.page.locator('#copy-code-form [name=__RequestVerificationToken]').count() === 1);
        await staff.page.locator('#copy-code').fill(code);
        await submit(staff.page, '#copy-code-form button[type=submit]');
        check('Presented code lookup resolves correct saved reservation without URL secret', new URL(staff.page.url()).pathname === `/Reservations/Details/${id}` && !new URL(staff.page.url()).search);
        await staff.page.locator('#reservation-pickup-code').fill(code);
        await submit(staff.page, '#reservation-fulfill-submit');
        const loanId = Number(new URL(staff.page.url()).pathname.match(/^\/Loans\/Details\/(\d+)$/)?.[1]);
        check('Physical handover creates real loan and redirects to its detail', Number.isInteger(loanId) && loanId > 0);
        await status(first.page, id, 'Fulfilled', 'Owner history records Fulfilled after physical handover');
        check('Fulfilled history links the created loan', await first.page.locator(`a[href="/Loans/Details/${loanId}"]`).count() > 0);
        await first.page.goto(`/Loans/Details/${loanId}`);
        check('Owner can open new borrowing history record', await first.page.locator('h1').count() === 1 && !first.page.url().includes('AccessDenied'));
        await status(second.page, queued, 'Queued', 'Other reader remains queued while copy is on loan');
        await first.page.goto('/Notifications');
        check('Reservation workflow notifications appear in real reader inbox', (await first.page.locator('a[href*="/Reservations/Details/"]').count()) > 0);

        await staff.page.goto(`/Returns/Create/${loanId}`);
        await staff.page.locator('[name=ReturnCondition]').selectOption('Good');
        await submit(staff.page, 'form:has(input[name=LoanId]) button[type=submit]');
        await status(second.page, queued, 'Pending', 'Physical return atomically promotes oldest queued reader');
        await cancel(second.page, queued);
        const again = await create(first.page, 'Pending');
        const againQueued = await create(second.page, 'Queued');
        await cancel(first.page, again);
        await status(second.page, againQueued, 'Pending', 'Owner cancellation releases copy and promotes FIFO queue');
        await cancel(second.page, againQueued);
        await first.page.goto(`/Reservations/Create?bookId=${fixture.bookId}`);
        check('Cancelled final hold returns book to available reservation mode', await first.page.locator('#reservation-create-form').getAttribute('data-queue') === 'false');
        check('Reservation workflows have no browser script errors', pageErrors.length === 0);
        await publicContext.close();
        await first.context.close(); await second.context.close(); await staff.context.close();
    } catch (error) {
        failed = true;
        // Error details may contain submitted secrets in Playwright call logs; omit them.
        checks.push({ name: 'Workflow completed without an unexpected exception', passed: false });
        console.error('FAIL Reservation browser workflow stopped; inspect the last named check.');
        const sourceLine = String(error.stack || '').match(/reservations_ui_smoke\.cjs:\d+:\d+/)?.[0];
        console.error('Diagnostic: ' + (error.name || 'Error') + (sourceLine ? ' at ' + sourceLine : ''));
    } finally {
        await browser.close();
        const report = { passed: checks.filter(c => c.passed).length, failed: checks.filter(c => !c.passed).length, checks, pageErrorCount: pageErrors.length };
        fs.writeFileSync(path.join(output, 'report.json'), JSON.stringify(report, null, 2));
        console.log(`Reservation UI: ${report.passed} passed, ${report.failed} failed.`);
        process.exitCode = failed || report.failed ? 1 : 0;
    }
})();
