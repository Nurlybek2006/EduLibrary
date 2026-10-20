import { createReader } from "./reader-common.js";
const reader = createReader();
const stage = document.getElementById("reader-stage");
const target = document.getElementById("reader-epub-view");
const previous = document.getElementById("reader-prev");
const next = document.getElementById("reader-next");
const toc = document.getElementById("reader-toc");
let book = null;
let rendition = null;
let locationsReady = false;
let lastLocation = null;
let resizeTimer = null;
const themes = {
    light: { body: { color: "#24382e", background: "#ffffff" }, a: { color: "#28543f" } },
    sepia: { body: { color: "#4b3d29", background: "#f4ecd9" }, a: { color: "#7c4e28" } },
    dark: { body: { color: "#e5e8df", background: "#1b2521" }, a: { color: "#bad6bf" } }
};
const updateProgress = location => {
    lastLocation = location;
    const cfi = location?.start?.cfi;
    if (!cfi) return;
    let percent = locationsReady ? book.locations.percentageFromCfi(cfi) * 100 : Number(location.start.percentage || 0) * 100;
    if (location.atEnd) percent = 100;
    percent = Math.round(Math.max(0, Math.min(100, percent)) * 100) / 100;
    reader.setLocator({ CurrentPage: 0, TotalPages: 0, EpubCfi: cfi, ProgressPercentage: percent });
    previous.disabled = Boolean(location.atStart); next.disabled = Boolean(location.atEnd);
};
const navigate = async locator => {
    try { await rendition.display(locator); } catch { reader.report(reader.messages.loadFailed, true); }
};
try {
    const response = await fetch(reader.config.fileUrl, { credentials: "same-origin", headers: { "Accept": "application/epub+zip" } });
    if (!response.ok) throw new Error("File unavailable");
    const bytes = await response.arrayBuffer();
    book = window.ePub(bytes, { replacements: "blobUrl" });
    await book.ready;
    rendition = book.renderTo(target, { width: "100%", height: Math.max(450, stage.clientHeight - 20), flow: "paginated", spread: "none", allowScriptedContent: false, allowPopups: false });
    Object.entries(themes).forEach(([name, rules]) => rendition.themes.register(name, rules));
    rendition.themes.select("light"); rendition.themes.fontSize("110%"); rendition.themes.override("line-height", "1.8");
    rendition.hooks.content.register(contents => {
        // EPUB content is already reconstructed by the server. Keep the iframe
        // without allow-scripts; same-origin is needed for CFI and typography.
        const iframe = contents.document.defaultView?.frameElement;
        if (iframe) { iframe.setAttribute("sandbox", "allow-same-origin"); iframe.setAttribute("title", reader.messages.chapter); }
        contents.document.querySelectorAll("script,iframe,object,embed,form").forEach(node => node.remove());
        contents.document.querySelectorAll("a").forEach(link => link.addEventListener("click", event => {
            const href = link.getAttribute("href") || "";
            if (/^(?:[a-z][a-z0-9+.-]*:|\/\/)/i.test(href)) event.preventDefault();
        }, true));
    });
    rendition.on("relocated", updateProgress);
    const navigation = await book.loaded.navigation;
    const addToc = (items, depth = 0) => items.forEach(item => { const option = new Option("  ".repeat(depth) + item.label.trim(), item.href); toc.add(option); if (item.subitems?.length) addToc(item.subitems, depth + 1); });
    addToc(navigation.toc);
    const initialCfi = reader.config.initialProgress.epubCfi;
    try { await rendition.display(initialCfi || undefined); }
    catch { await rendition.display(); }
    reader.ready();
    previous.disabled = false; next.disabled = false;
    // Actual text-derived locations are generated from this exact sanitized file,
    // so saved CFI positions remain stable across devices and font preferences.
    book.locations.generate(800).then(() => { locationsReady = true; if (lastLocation) updateProgress(lastLocation); }).catch(() => {});
    reader.setNavigate(navigate);
    previous.addEventListener("click", () => rendition.prev().catch(() => reader.report(reader.messages.loadFailed, true)));
    next.addEventListener("click", () => rendition.next().catch(() => reader.report(reader.messages.loadFailed, true)));
    toc.addEventListener("change", () => { if (toc.value) navigate(toc.value); });
    document.getElementById("reader-font").addEventListener("input", event => rendition.themes.fontSize(event.target.value + "%"));
    document.getElementById("reader-spacing").addEventListener("change", event => rendition.themes.override("line-height", event.target.value));
    reader.shell.addEventListener("readertheme", event => rendition.themes.select(event.detail));
    const resize = () => { clearTimeout(resizeTimer); resizeTimer = setTimeout(() => rendition.resize(target.clientWidth, Math.max(450, stage.clientHeight - 20)), 180); };
    window.addEventListener("resize", resize); window.addEventListener("readerresize", resize); document.addEventListener("fullscreenchange", resize);
    window.addEventListener("pagehide", () => { rendition?.destroy(); book?.destroy(); });
} catch { reader.failed(); }
