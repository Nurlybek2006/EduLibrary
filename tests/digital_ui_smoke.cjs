/** Actual reader/progress/bookmark workflows, only against the disposable app.
 * Run: node tests/digital_ui_smoke.cjs http://localhost:5399
 * Reads private local seed credentials; never prints credentials or antiforgery.
 * Creates/removes only its own reading bookmarks. Creates no books or loans.
 */
const fs = require('node:fs');
const path = require('node:path');
const assert = require('node:assert/strict');
const { chromium } = require('../.tools/browser/node_modules/playwright');
const root = path.resolve(__dirname, '..');
const baseURL = process.argv[2] || 'http://localhost:5399';
const target = new URL(baseURL);
assert(['localhost','127.0.0.1'].includes(target.hostname) && target.port === '5399', 'Reader writes require the disposable app on port5399.');
const config = JSON.parse(fs.readFileSync(path.join(root,'appsettings.Local.json'),'utf8').replace(/^\uFEFF/,''));
const output = path.join(root,'artifacts','digital-ui'); fs.mkdirSync(output,{recursive:true});
const checks=[], errors=[];
function check(name, condition) { checks.push({name,passed:!!condition}); console.log(`${condition?'PASS':'FAIL'} ${name}`); assert(condition,name); }
async function login(browser,role) {
 const context=await browser.newContext({baseURL,viewport:{width:1440,height:1000}});
 await context.addCookies([{name:'.AspNetCore.Culture',value:'c=en-US|uic=en-US',url:baseURL}]);
 const page=await context.newPage();page.on('pageerror',()=>errors.push('Reader browser script error'));
 await page.goto('/Account/Login');await page.locator('[name=Email]').fill(config.SeedUsers[role].Email);await page.locator('[name=Password]').fill(config.SeedUsers[role].Password);
 await Promise.all([page.waitForURL(url=>!url.pathname.startsWith('/Account/Login')),page.locator('form:has(input[name="Email"]) button[type="submit"]').click()]);
 return {context,page};
}
async function pdfReady(page) { await page.waitForFunction(()=>document.querySelector('#reader-canvas')?.width>0 && !document.querySelector('#reader-pdf-page').hidden && document.querySelector('#reader-text-layer')?.children.length>0,{},{timeout:30000}); }
async function saved(context,id,predicate) {
 for(let attempt=0;attempt<35;attempt++) { const response=await context.request.get(`/Reader/State/${id}`);if(response.ok()) {const data=await response.json();if(predicate(data))return data;}await new Promise(resolve=>setTimeout(resolve,200)); }
 throw new Error('Reader state did not reach expected persisted SQL value.');
}
async function addBookmark(page,title,note) {
 if(await page.locator('#reader-sidebar').isHidden())await page.locator('#reader-bookmarks-toggle').click();
 await page.locator('#reader-add-bookmark').click();await page.locator('#reader-bookmark-title').fill(title);await page.locator('#reader-bookmark-note').fill(note);
 const response=page.waitForResponse(r=>r.url().endsWith('/Reader/Bookmark')&&r.request().method()==='POST');
 await page.locator('#reader-bookmark-save').click();const result=await response;assert(result.ok());return (await result.json()).bookmark;
}
async function signInAgain(page) {
 await page.goto('/Account/Login');await page.locator('[name=Email]').fill(config.SeedUsers.Student.Email);await page.locator('[name=Password]').fill(config.SeedUsers.Student.Password);
 await Promise.all([page.waitForURL(url=>!url.pathname.startsWith('/Account/Login')),page.locator('form:has(input[name="Email"]) button[type="submit"]').click()]);
}
async function language(page,culture) {
 await Promise.all([page.waitForNavigation({waitUntil:'load'}),page.locator('#site-language').selectOption(culture)]);
 check(`Language switch applies ${culture}`,await page.locator('html').getAttribute('data-culture')===culture);
}
async function originalAvatar(page,variant) {
 const base64=await page.evaluate(value=>{const canvas=document.createElement('canvas');canvas.width=800;canvas.height=500;const ctx=canvas.getContext('2d');ctx.fillStyle=value===1?'#18382f':'#b56b4e';ctx.fillRect(0,0,800,500);ctx.fillStyle=value===1?'#d6e4c7':'#e8d8a8';ctx.fillRect(420,0,380,500);ctx.fillStyle='#fff';ctx.beginPath();ctx.arc(400,250,130,0,Math.PI*2);ctx.fill();return canvas.toDataURL('image/png').split(',')[1];},variant);
 return {name:`original-ui-avatar-${variant}.png`,mimeType:'image/png',buffer:Buffer.from(base64,'base64')};
}
async function avatarWorkflow(page,context) {
 await page.goto('/Account/Profile');
 const original=await originalAvatar(page,1);
 await page.locator('#avatar-file').setInputFiles(original);await page.waitForSelector('.avatar-editor:not([hidden])');
 await page.locator('[data-avatar-cancel]').click();check('Avatar crop can be cancelled without uploading',await page.locator('.avatar-editor').isHidden()&&await page.locator('#avatar-file').inputValue()==='');
 await page.locator('#avatar-file').setInputFiles(original);await page.waitForSelector('.avatar-editor:not([hidden])');
 await page.locator('#avatar-zoom').evaluate(node=>{node.value='1.7';node.dispatchEvent(new Event('input',{bubbles:true}));});
 await page.locator('#avatar-position-x').evaluate(node=>{node.value='70';node.dispatchEvent(new Event('input',{bubbles:true}));});
 await page.locator('[data-avatar-canvas]').press('ArrowDown');
 check('Crop controls compute normalized server coordinates',Number(await page.locator('[name=CropX]').inputValue())>0&&Number(await page.locator('[name=CropSize]').inputValue())<1);
 await Promise.all([page.waitForNavigation({waitUntil:'load'}),page.locator('[data-avatar-confirm]').click()]);
 const first=await page.locator('#profile-photo img[data-user-avatar]').first().getAttribute('src');
 check('Crop upload persists an opaque protected avatar URL',/^\/Account\/Avatar\/[a-f0-9]{32}\.webp$/i.test(first));
 const dimensions=await page.evaluate(async url=>{const response=await fetch(url);const type=response.headers.get('content-type');const bitmap=await createImageBitmap(await response.blob());return {type,width:bitmap.width,height:bitmap.height};},first);
 check('Server re-encodes actual avatar to256px WebP',dimensions.width===256&&dimensions.height===256&&dimensions.type.startsWith('image/webp'));
 await page.reload();check('Avatar survives profile refresh',await page.locator('#profile-photo img[data-user-avatar]').first().getAttribute('src')===first);
 await page.locator('#avatar-file').setInputFiles(await originalAvatar(page,2));await page.waitForSelector('.avatar-editor:not([hidden])');
 await Promise.all([page.waitForNavigation({waitUntil:'load'}),page.locator('[data-avatar-confirm]').click()]);
 const second=await page.locator('#profile-photo img[data-user-avatar]').first().getAttribute('src');check('Replacing avatar stores a new opaque image',second!==first&&/\.webp$/.test(second));
 check('Replacing avatar removes only its previous image',(await context.request.get(first)).status()===404);
 await page.locator('#PreferredLanguage').selectOption('en-US');await page.locator('#PreferredTheme').selectOption('dark');
 await Promise.all([page.waitForNavigation({waitUntil:'load'}),page.locator('[data-profile-preferences] button[type=submit]').click()]);
 await page.reload();check('Profile language and theme persist after refresh',await page.locator('#PreferredLanguage').inputValue()==='en-US'&&await page.locator('#PreferredTheme').inputValue()==='dark'&&await page.locator('html').getAttribute('data-theme')==='dark');
 await page.locator('.avatar-remove-form button').click();const confirm=page.locator('#confirm-dialog[open] button[value=confirm]');
 if(await confirm.count())await Promise.all([page.waitForNavigation({waitUntil:'load'}),confirm.click()]);else await page.waitForLoadState('load');
 check('Removing avatar restores initials without a broken image',await page.locator('#profile-photo img[data-user-avatar]').count()===0&&(await context.request.get(second)).status()===404);
}
async function submitAsset(page) {
 await Promise.all([page.waitForNavigation({waitUntil:'load'}),page.locator('#digital-asset-form button[type=submit]').click()]);
 check('Digital management form persists and redirects to details',new URL(page.url()).pathname.startsWith('/DigitalLibrary/Details/'));
 return Number(await page.locator('#digital-edition-details').getAttribute('data-asset-id'));
}
async function managementWorkflow(browser,studentContext) {
 const admin=await login(browser,'Admin');const {page,context}=admin;
 const manifest=JSON.parse(fs.readFileSync(path.join(root,'Data','DigitalDemo','manifest.json'),'utf8'));
 const original=manifest.find(item=>item.slug==='csharp-basics');assert(original);
 await page.goto('/DigitalLibrary/Upload');
 const bookId=await page.locator('#BookId option').evaluateAll((nodes,title)=>nodes.find(node=>node.textContent.startsWith(title+' · '))?.value,original.title);
 assert(bookId,'The original demo book must exist. Commercial books are never used as upload fixtures.');
 await page.locator('#BookId').selectOption(bookId);await page.locator('#FileFormat').selectOption('Pdf');await page.locator('#Language').fill('Қазақша');
 await page.locator('#File').setInputFiles(path.join(root,'Data','DigitalDemo','csharp-basics.pdf'));
 await page.locator('#LicenseType').selectOption('Unknown');await page.locator('#LicenseName').fill('');await page.locator('#RightsHolder').fill('');await page.locator('#IsPublished').uncheck();await page.locator('#RightsConfirmed').uncheck();
 const assetId=await submitAsset(page);check('Incomplete Unknown-license draft is saved privately',(await studentContext.request.get(`/Reader/File/${assetId}`)).status()===404);
 await page.goto(`/DigitalLibrary/Edit/${assetId}`);check('Draft preserves blank optional license metadata',await page.locator('#LicenseName').inputValue()===''&&await page.locator('#RightsHolder').inputValue()===''&&!(await page.locator('#IsPublished').isChecked()));
 await page.locator('#LicenseType').selectOption('Original');await page.locator('#LicenseName').fill('Creative Commons Attribution 4.0 International');
 await page.locator('#LicenseUrl').fill('https://creativecommons.org/licenses/by/4.0/');await page.locator('#SourceUrl').fill('/digital-demo/provenance.html');await page.locator('#RightsHolder').fill('EduLibrary Demo Content');
 await page.locator('#RightsConfirmed').check();await page.locator('#AccessLevel').selectOption('Authenticated');await page.locator('#AllowDownload').check();await page.locator('#IsPublished').check();
 check('Admin binds original demo PDF and complete rights metadata',await page.locator('#BookId').inputValue()===bookId&&await page.locator('#RightsConfirmed').isChecked());
 await page.screenshot({path:path.join(output,'Admin-upload-1440.png'),fullPage:true,animations:'disabled'});
 check('Completing draft rights publishes the same edition',await submitAsset(page)===assetId);
 const range=await studentContext.request.get(`/Reader/File/${assetId}`,{headers:{Range:'bytes=0-127'}});
 check('Published original PDF streams with authorized206 byte range',range.status()===206&&range.headers()['content-range']?.startsWith('bytes 0-127/')&&(await range.body()).subarray(0,5).toString()==='%PDF-');
 const download=await studentContext.request.get(`/Reader/Download/${assetId}`,{headers:{Range:'bytes=0-63'}});
 check('Allowed download returns actual PDF and attachment headers',download.status()===206&&/attachment/i.test(download.headers()['content-disposition']||'')&&(await download.body()).length===64);
 await page.goto(`/DigitalLibrary/Edit/${assetId}`);const firstVersion=await page.locator('[name=RowVersion]').inputValue();
 await page.locator('#IsPublished').uncheck();await submitAsset(page);
 check('Unpublished digital asset is denied to students',(await studentContext.request.get(`/Reader/File/${assetId}`)).status()===404);
 await page.goto(`/DigitalLibrary/Edit/${assetId}`);const secondVersion=await page.locator('[name=RowVersion]').inputValue();check('Metadata updates advance SQL rowversion',firstVersion!==secondVersion);
 await page.locator('#IsPublished').check();await submitAsset(page);check('Republishing restores authorized access',(await studentContext.request.get(`/Reader/File/${assetId}`,{headers:{Range:'bytes=0-15'}})).status()===206);
 await page.goto(`/DigitalLibrary/Edit/${assetId}`);
 await page.locator('#File').setInputFiles({name:'csharp-basics-replacement.pdf',mimeType:'application/pdf',buffer:fs.readFileSync(path.join(root,'Data','DigitalDemo','csharp-basics.pdf'))});
 await submitAsset(page);await page.goto(`/DigitalLibrary/Edit/${assetId}`);
 check('Actual replacement upload preserves edition and new original filename',(await page.locator('.digital-current-file').innerText()).includes('csharp-basics-replacement.pdf')&&await page.locator('[name=Id]').inputValue()===String(assetId));
 await page.goto(`/DigitalLibrary/Details/${assetId}`);await page.locator('form[action*="Archive"] button[type=submit]').click();
 const confirm=page.locator('#confirm-dialog[open] button[value=confirm]');if(await confirm.count())await Promise.all([page.waitForNavigation({waitUntil:'load'}),confirm.click()]);else await page.waitForLoadState('load');
 check('Admin archive persists and removes reader access',new URL(page.url()).pathname==='/DigitalLibrary/Manage'&&(await studentContext.request.get(`/Reader/File/${assetId}`)).status()===404);
 await page.setViewportSize({width:360,height:1000});await page.goto('/DigitalLibrary/Upload');check('Admin digital upload form fits360px',await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth+1));await page.screenshot({path:path.join(output,'Admin-upload-360.png'),fullPage:true,animations:'disabled'});
 await context.close();
}
(async()=>{
 const browser=await chromium.launch({channel:'chrome',headless:true});let failure=null;
 try {
  const student=await login(browser,'Student');const {page,context}=student;
  const catalog=await page.goto('/DigitalLibrary');check('Digital catalog contains real PDF and EPUB editions',catalog.status()===200&&await page.locator('[data-format=PDF]').count()>0&&await page.locator('[data-format=EPUB]').count()>0);
  const pdfUrl=await page.locator('[data-format=PDF] .digital-read-link').first().getAttribute('href');
  const epubUrl=await page.locator('[data-format=EPUB] .digital-read-link').first().getAttribute('href');
  await page.goto(pdfUrl);await pdfReady(page);
  const pdfId=await page.locator('#reader-config').evaluate(node=>JSON.parse(node.textContent).assetId);
  check('PDF.js renders an actual page and accessible text layer',await page.locator('#reader-text-layer span').count()>0);
  await page.locator('#reader-page').fill('3');await page.locator('#reader-page').press('Enter');
  await saved(context,pdfId,data=>data.progress.currentPage===3);check('PDF page navigation persists page3 in SQL',true);
  await page.reload();await pdfReady(page);check('Reopened PDF resumes saved page3',await page.locator('#reader-page').inputValue()==='3');
  const width=await page.locator('#reader-canvas').evaluate(node=>node.width);await page.locator('#reader-zoom-out').click();
  await page.waitForFunction(old=>document.querySelector('#reader-canvas').width<old,width);check('PDF zoom changes rendered canvas',true);await page.locator('#reader-fit').click();
  await page.locator('#reader-search').fill('дерек');await page.locator('#reader-search-form').evaluate(form=>form.requestSubmit());
  await page.waitForFunction(()=>/Match|No matching/.test(document.querySelector('#reader-status').textContent),{},{timeout:30000});check('PDF search scans actual document text',await page.locator('#reader-status').innerText().then(text=>text.includes('Match')));
  const title='Digital UI PDF '+Date.now();const bookmark=await addBookmark(page,title,'Original browser workflow note');
  let state=await saved(context,pdfId,data=>data.bookmarks.some(row=>row.id===bookmark.id));check('PDF bookmark and note persist to SQL',state.bookmarks.some(row=>row.id===bookmark.id&&row.note==='Original browser workflow note'&&row.pageNumber>0));
  const article=page.locator(`[data-bookmark-id="${bookmark.id}"]`);await article.getByRole('button',{name:'Edit',exact:true}).click();await page.locator('#reader-bookmark-title').fill(title+' renamed');await page.locator('#reader-bookmark-note').fill('Renamed persisted note');await page.locator('#reader-bookmark-save').click();
  state=await saved(context,pdfId,data=>data.bookmarks.some(row=>row.id===bookmark.id&&row.title.endsWith('renamed')));check('Bookmark rename and note editing persist',state.bookmarks.find(row=>row.id===bookmark.id).note==='Renamed persisted note');
  await page.goto(epubUrl);await page.waitForSelector('#reader-epub-view iframe',{timeout:30000});await page.waitForFunction(()=>!document.querySelector('#reader-add-bookmark').disabled,{},{timeout:30000});
  const epubId=await page.locator('#reader-config').evaluate(node=>JSON.parse(node.textContent).assetId);
  const frame=page.frameLocator('#reader-epub-view iframe').first();
  check('EPUB.js renders actual book text in scriptless sandbox',await page.locator('#reader-epub-view iframe').first().getAttribute('sandbox')==='allow-same-origin'&&(await frame.locator('body').innerText()).trim().length>30);
  const chapters=await page.locator('#reader-toc option').count();check('EPUB has a real chapter table of contents',chapters>=6);
  const chapter=await page.locator('#reader-toc option').evaluateAll(nodes=>nodes.find(node=>node.value.includes('chapter2'))?.value);assert(chapter);await page.locator('#reader-toc').selectOption(chapter);
  let epubState=await saved(context,epubId,data=>data.progress.epubCfi?.startsWith('epubcfi(')&&data.progress.progressPercentage>0);check('EPUB saves exact CFI and positive text-derived percentage in SQL',!!epubState.progress.epubCfi&&epubState.progress.progressPercentage>0);
  const fontBefore=await frame.locator('body').evaluate(node=>parseFloat(getComputedStyle(node).fontSize));await page.locator('#reader-font').evaluate(node=>{node.value='150';node.dispatchEvent(new Event('input',{bubbles:true}));});
  await page.waitForTimeout(350);check('EPUB font control changes actual rendered text',await frame.locator('body').evaluate(node=>parseFloat(getComputedStyle(node).fontSize))>fontBefore);
  await page.locator('#reader-spacing').selectOption('2.1');await page.locator('#reader-theme').selectOption('sepia');check('EPUB reading theme applies to shell',await page.locator('#reader-shell').getAttribute('data-reader-theme')==='sepia');
  const epubBookmark=await addBookmark(page,'Digital UI EPUB '+Date.now(),'CFI bookmark');epubState=await saved(context,epubId,data=>data.bookmarks.some(row=>row.id===epubBookmark.id));check('EPUB bookmark uses CFI without a fake PDF page',epubState.bookmarks.some(row=>row.id===epubBookmark.id&&row.epubCfi&&row.pageNumber===null));
  await page.waitForTimeout(1200);const beforeLogout=await (await context.request.get(`/Reader/State/${epubId}`)).json();
  const logoutToken=await page.locator('#reader-token input').inputValue();await context.request.post('/Account/Logout',{form:{__RequestVerificationToken:logoutToken}});await signInAgain(page);
  const afterLogin=await (await context.request.get(`/Reader/State/${epubId}`)).json();check('EPUB exact CFI and bookmarks survive sign out and sign in',afterLogin.progress.epubCfi===beforeLogout.progress.epubCfi&&afterLogin.bookmarks.some(row=>row.id===epubBookmark.id));
  await page.goto(epubUrl);await page.waitForSelector('#reader-epub-view iframe');check('EPUB reader receives SQL saved locator after new sign in',await page.locator('#reader-config').evaluate((node,cfi)=>JSON.parse(node.textContent).initialProgress.epubCfi===cfi,beforeLogout.progress.epubCfi));
  await page.goto('/DigitalLibrary');await language(page,'ru-RU');check('Digital page renders Russian UI',await page.locator('h1').innerText()==='Электронная библиотека');await page.reload();check('Russian culture survives refresh',await page.locator('html').getAttribute('data-culture')==='ru-RU');await language(page,'en-US');check('Digital page renders English UI',await page.locator('h1').innerText()==='Digital library');
  for(const [format,route] of [['PDF',pdfUrl],['EPUB',epubUrl]])for(const width of [360,390,768,1024,1440,1920]){
   await page.setViewportSize({width,height:1000});await page.goto(route);if(format==='PDF')await pdfReady(page);else await page.waitForSelector('#reader-epub-view iframe');
   check(`${format} reader ${width}px fits viewport`,await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth+1));
   if(width===360||width===1440)await page.screenshot({path:path.join(output,`${format}-${width}.png`),fullPage:true,animations:'disabled'});
  }
  await page.goto('/DigitalLibrary/My');check('Personal library shows real reading history',await page.locator('#my-digital-library').count()===1&&await page.locator(`[data-asset-id="${pdfId}"]`).count()>0&&await page.locator(`[data-asset-id="${epubId}"]`).count()>0);
  await avatarWorkflow(page,context);await page.goto('/DigitalLibrary');await language(page,'kk-KZ');check('Digital page returns to Kazakh UI',await page.locator('h1').innerText()==='Электрондық кітапхана');await language(page,'en-US');
  const noCsrf=await context.request.post('/Reader/Progress',{form:{AssetId:String(pdfId),CurrentPage:'1',TotalPages:'8',ProgressPercentage:'12.5'}});check('Reader progress rejects missing antiforgery',noCsrf.status()===400);
  const staff=await login(browser,'Librarian');for(const route of ['/DigitalLibrary/Manage','/DigitalLibrary/Upload']){const result=await staff.page.goto(route);check(`Staff ${route} form loads`,result.status()===200);}
  const denied=await context.request.get('/DigitalLibrary/Manage');check('Student cannot manage digital assets',denied.status()===403||new URL(denied.url()).pathname==='/Account/AccessDenied');
  const publicContext=await browser.newContext({baseURL});const deniedFile=await publicContext.request.get(`/Reader/File/${pdfId}`);check('Anonymous cannot stream protected digital files',new URL(deniedFile.url()).pathname==='/Account/Login');
  await managementWorkflow(browser,context);
  // Remove only bookmarks created by this run, retaining reading history for resume verification.
  await page.goto(pdfUrl);await pdfReady(page);const csrf=await page.locator('#reader-token input').inputValue();
  for(const row of [bookmark,epubBookmark]){const response=await context.request.post('/Reader/RemoveBookmark',{form:{id:String(row.id),__RequestVerificationToken:csrf}});check('Own workflow bookmark cleanup',response.ok());}
  check('No browser script errors',errors.length===0);
  await publicContext.close();await staff.context.close();await context.close();
 }catch(error){failure=error;console.error('FAIL Digital reader workflow:',error.message);}
 finally{await browser.close();fs.writeFileSync(path.join(output,'results.json'),JSON.stringify({checks,errors,failure:failure?.message||null},null,2));}
 console.log(`${checks.filter(row=>row.passed).length}/${checks.length} digital browser checks passed.`);if(failure||checks.some(row=>!row.passed))process.exitCode=1;
})();
