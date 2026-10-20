export function createReader() {
    const shell = document.getElementById("reader-shell");
    const config = JSON.parse(document.getElementById("reader-config").textContent);
    const messages = config.messages;
    const token = document.querySelector("#reader-token input[name='__RequestVerificationToken']").value;
    const status = document.getElementById("reader-status");
    const retry = document.getElementById("reader-retry-save");
    const progressBar = document.getElementById("reader-progress");
    const progressLabel = document.getElementById("reader-progress-label");
    const addButton = document.getElementById("reader-add-bookmark");
    let bookmarks = config.bookmarks || [];
    let locator = null;
    let bookmarkLocator = null;
    let timer = null;
    let pending = null;
    let saving = false;
    let savedKey = "";
    let navigate = () => {};
    const format = (text, ...args) => text.replace(/\{(\d+)\}/g, (_, index) => args[Number(index)] ?? "");
    const report = (message, error = false) => {
        status.textContent = message;
        status.classList.toggle("is-error", error);
        status.setAttribute("role", error ? "alert" : "status");
    };
    const bodyFor = fields => {
        const body = new URLSearchParams({ __RequestVerificationToken: token });
        Object.entries(fields).forEach(([key, value]) => { if (value !== null && value !== undefined) body.set(key, String(value)); });
        return body;
    };
    const post = async (url, fields, keepalive = false) => {
        const response = await fetch(url, { method: "POST", credentials: "same-origin", headers: { "Accept": "application/json" }, body: bodyFor(fields), keepalive });
        const data = await response.json().catch(() => null);
        if (!response.ok || !data?.success) {
            const error = new Error();
            error.userMessage = typeof data?.message === "string" ? data.message : null;
            throw error;
        }
        return data;
    };
    const flush = async () => {
        if (saving || !pending) return;
        const snapshot = pending;
        pending = null;
        const key = JSON.stringify(snapshot);
        if (key === savedKey) return;
        saving = true;
        report(messages.saving);
        try {
            await post(config.endpoints.progress, { AssetId: config.assetId, ...snapshot });
            savedKey = key;
            retry.hidden = true;
            report(messages.saved);
        } catch {
            if (!pending) pending = snapshot;
            retry.hidden = false;
            report(messages.saveFailed, true);
        } finally {
            saving = false;
            if (pending && JSON.stringify(pending) !== key) flush();
        }
    };
    const setLocator = value => {
        locator = value;
        addButton.disabled = false;
        const percent = Math.max(0, Math.min(100, value.ProgressPercentage || 0));
        progressBar.value = percent;
        progressLabel.textContent = percent >= 100 ? messages.completed : format(messages.progress, Math.round(percent));
        pending = { ...value };
        window.clearTimeout(timer);
        timer = window.setTimeout(flush, 850);
    };
    const list = document.getElementById("reader-bookmark-list");
    const dialog = document.getElementById("reader-bookmark-dialog");
    const editor = document.getElementById("reader-bookmark-form");
    const titleInput = document.getElementById("reader-bookmark-title");
    const noteInput = document.getElementById("reader-bookmark-note");
    const bookmarkId = document.getElementById("reader-bookmark-id");
    const edit = bookmark => {
        bookmarkId.value = bookmark?.id || 0;
        bookmarkLocator = bookmark ? { CurrentPage: bookmark.pageNumber, EpubCfi: bookmark.epubCfi } : { ...locator };
        titleInput.value = bookmark?.title || (config.format === "pdf" ? format(messages.page, locator.CurrentPage) : messages.bookmarkTitle);
        noteInput.value = bookmark?.note || "";
        dialog.showModal();
        titleInput.focus();
    };
    const renderBookmarks = () => {
        list.replaceChildren();
        document.getElementById("reader-bookmark-count").textContent = bookmarks.length;
        if (!bookmarks.length) { const p = document.createElement("p"); p.textContent = messages.emptyBookmarks; list.append(p); return; }
        bookmarks.forEach(bookmark => {
            const article = document.createElement("article");
            article.dataset.bookmarkId = bookmark.id;
            const open = document.createElement("button");
            open.type = "button"; open.className = "reader-bookmark-open"; open.textContent = bookmark.title;
            open.addEventListener("click", () => navigate(bookmark.pageNumber || bookmark.epubCfi));
            article.append(open);
            if (bookmark.note) { const p = document.createElement("p"); p.textContent = bookmark.note; article.append(p); }
            const actions = document.createElement("div"); actions.className = "reader-bookmark-actions";
            const change = document.createElement("button"); change.type = "button"; change.textContent = messages.edit; change.addEventListener("click", () => edit(bookmark));
            const remove = document.createElement("button"); remove.type = "button"; remove.textContent = messages.remove;
            remove.addEventListener("click", async () => {
                if (!window.confirm(messages.removeConfirm)) return;
                remove.disabled = true;
                try { await post(config.endpoints.removeBookmark, { id: bookmark.id }); bookmarks = bookmarks.filter(item => item.id !== bookmark.id); renderBookmarks(); report(messages.bookmarkRemoved); }
                catch (error) { report(error.userMessage || messages.bookmarkFailed, true); remove.disabled = false; }
            });
            actions.append(change, remove); article.append(actions); list.append(article);
        });
    };
    addButton.addEventListener("click", () => { if (locator) edit(null); });
    editor.addEventListener("submit", async event => {
        event.preventDefault();
        if (!editor.reportValidity() || !bookmarkLocator) return;
        const button = document.getElementById("reader-bookmark-save"); button.disabled = true;
        try {
            const data = await post(config.endpoints.bookmark, { Id: Number(bookmarkId.value), AssetId: config.assetId,
                PageNumber: config.format === "pdf" ? bookmarkLocator.CurrentPage : null,
                EpubCfi: config.format === "epub" ? bookmarkLocator.EpubCfi : null, Title: titleInput.value.trim(), Note: noteInput.value.trim() });
            bookmarks = bookmarks.filter(item => item.id !== data.bookmark.id); bookmarks.push(data.bookmark);
            renderBookmarks(); dialog.close(); report(messages.bookmarkSaved);
        } catch (error) { report(error.userMessage || messages.bookmarkFailed, true); }
        finally { button.disabled = false; }
    });
    document.getElementById("reader-bookmark-cancel").addEventListener("click", () => dialog.close());
    document.querySelector("[data-reader-dialog-close]").addEventListener("click", () => dialog.close());
    const sidebar = document.getElementById("reader-sidebar");
    const toggle = document.getElementById("reader-bookmarks-toggle");
    const showSidebar = show => { sidebar.hidden = !show; toggle.setAttribute("aria-expanded", String(show)); shell.classList.toggle("reader-sidebar-open", show); window.dispatchEvent(new Event("readerresize")); };
    toggle.addEventListener("click", () => showSidebar(sidebar.hidden));
    document.getElementById("reader-sidebar-close").addEventListener("click", () => showSidebar(false));
    document.getElementById("reader-fullscreen").addEventListener("click", async () => {
        try { if (document.fullscreenElement) await document.exitFullscreen(); else await shell.requestFullscreen(); }
        catch { report(messages.fullscreenFailed, true); }
    });
    document.getElementById("reader-theme").addEventListener("change", event => { shell.dataset.readerTheme = event.target.value; shell.dispatchEvent(new CustomEvent("readertheme", { detail: event.target.value })); });
    retry.addEventListener("click", flush);
    document.addEventListener("visibilitychange", () => { if (document.hidden) { window.clearTimeout(timer); flush(); } });
    window.addEventListener("pagehide", () => {
        window.clearTimeout(timer);
        if (locator && JSON.stringify(locator) !== savedKey) post(config.endpoints.progress, { AssetId: config.assetId, ...locator }, true).catch(() => {});
    });
    renderBookmarks();
    return { config, messages, shell, format, report, setLocator, setNavigate: callback => { navigate = callback; },
        ready: () => { document.getElementById("reader-loading").hidden = true; report(messages.ready); },
        failed: () => { document.getElementById("reader-loading").hidden = true; report(messages.loadFailed, true); },
        flush };
}
