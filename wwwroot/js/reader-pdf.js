import * as pdfjs from "../lib/pdfjs/pdf.min.mjs";
import { createReader } from "./reader-common.js";

const reader = createReader();
pdfjs.GlobalWorkerOptions.workerSrc = "/lib/pdfjs/pdf.worker.min.mjs";
const stage = document.getElementById("reader-stage");
const pageElement = document.getElementById("reader-pdf-page");
const canvas = document.getElementById("reader-canvas");
const textContainer = document.getElementById("reader-text-layer");
const input = document.getElementById("reader-page");
const previous = document.getElementById("reader-prev");
const next = document.getElementById("reader-next");
let pdf = null;
let currentPage = Math.max(1, reader.config.initialProgress.currentPage || 1);
let scale = 1;
let fit = true;
let renderTask = null;
let textLayer = null;
let generation = 0;
let resizeTimer = null;
let searchGeneration = 0;
let searchTerm = "";
let matches = [];
let matchIndex = -1;
const textCache = new Map();
const highlight = () => {
    textContainer.querySelectorAll("span").forEach(span => span.classList.toggle("reader-search-hit", Boolean(searchTerm && span.textContent.toLocaleLowerCase().includes(searchTerm))));
};
async function display(value, resetScroll = true) {
    if (!pdf) return;
    const number = Math.min(pdf.numPages, Math.max(1, Math.trunc(Number(value) || 1)));
    currentPage = number;
    const currentGeneration = ++generation;
    renderTask?.cancel();
    textLayer?.cancel();
    try {
        const page = await pdf.getPage(number);
        if (currentGeneration !== generation) return;
        if (fit) scale = Math.min(2, Math.max(0.25, (stage.clientWidth - (innerWidth < 600 ? 24 : 64)) / page.getViewport({ scale: 1 }).width));
        const viewport = page.getViewport({ scale });
        const ratio = Math.min(window.devicePixelRatio || 1, 2);
        canvas.width = Math.floor(viewport.width * ratio); canvas.height = Math.floor(viewport.height * ratio);
        canvas.style.width = viewport.width + "px"; canvas.style.height = viewport.height + "px";
        pageElement.style.width = viewport.width + "px"; pageElement.style.height = viewport.height + "px";
        textContainer.replaceChildren();
        textContainer.style.setProperty("--total-scale-factor", viewport.scale);
        textContainer.style.setProperty("--scale-factor", viewport.scale);
        pageElement.hidden = false;
        renderTask = page.render({ canvasContext: canvas.getContext("2d"), viewport, transform: ratio === 1 ? null : [ratio, 0, 0, ratio, 0, 0] });
        await renderTask.promise;
        if (currentGeneration !== generation) return;
        const text = await page.getTextContent();
        if (currentGeneration !== generation) return;
        textLayer = new pdfjs.TextLayer({ textContentSource: text, container: textContainer, viewport });
        await textLayer.render();
        if (currentGeneration !== generation) return;
        highlight();
        input.value = number; input.max = pdf.numPages;
        previous.disabled = number <= 1; next.disabled = number >= pdf.numPages;
        document.getElementById("reader-zoom").textContent = Math.round(scale * 100) + "%";
        if (resetScroll) stage.scrollTo({ top: 0, left: 0, behavior: "instant" });
        reader.ready();
        reader.setLocator({ CurrentPage: number, TotalPages: pdf.numPages, ProgressPercentage: Math.round(number / pdf.numPages * 10000) / 100 });
    } catch (error) {
        if (currentGeneration !== generation || error?.name === "RenderingCancelledException" || error?.name === "AbortException") return;
        reader.failed();
    }
}
previous.addEventListener("click", () => display(currentPage - 1));
next.addEventListener("click", () => display(currentPage + 1));
input.addEventListener("change", () => display(input.value));
input.addEventListener("keydown", event => { if (event.key === "Enter") { event.preventDefault(); display(input.value); } });
document.getElementById("reader-zoom-in").addEventListener("click", () => { fit = false; scale = Math.min(3, scale + .15); display(currentPage, false); });
document.getElementById("reader-zoom-out").addEventListener("click", () => { fit = false; scale = Math.max(.25, scale - .15); display(currentPage, false); });
document.getElementById("reader-fit").addEventListener("click", () => { fit = true; display(currentPage, false); });
const resize = () => { if (!fit) return; clearTimeout(resizeTimer); resizeTimer = setTimeout(() => display(currentPage, false), 180); };
window.addEventListener("resize", resize); window.addEventListener("readerresize", resize); document.addEventListener("fullscreenchange", resize);
document.getElementById("reader-search-form").addEventListener("submit", async event => {
    event.preventDefault();
    if (!pdf) return;
    const value = document.getElementById("reader-search").value.trim().toLocaleLowerCase();
    if (!value) { searchTerm = ""; matches = []; highlight(); return; }
    if (value === searchTerm && matches.length) {
        matchIndex = (matchIndex + 1) % matches.length;
        await display(matches[matchIndex]);
        reader.report(reader.format(reader.messages.match, matchIndex + 1, matches.length)); return;
    }
    const queryGeneration = ++searchGeneration;
    searchTerm = value; matches = []; matchIndex = -1;
    reader.report(reader.messages.searching);
    try {
        for (let number = 1; number <= pdf.numPages; number++) {
            if (queryGeneration !== searchGeneration) return;
            let text = textCache.get(number);
            if (!text) { const page = await pdf.getPage(number); const content = await page.getTextContent(); text = content.items.map(item => item.str || "").join(" ").toLocaleLowerCase(); if (textCache.size < 200) textCache.set(number, text); }
            if (text.includes(value)) matches.push(number);
        }
        if (queryGeneration !== searchGeneration) return;
        if (!matches.length) { highlight(); reader.report(reader.messages.noMatches); return; }
        matchIndex = 0; await display(matches[0]);
        reader.report(reader.format(reader.messages.match, 1, matches.length));
    } catch { reader.report(reader.messages.noMatches); }
});
document.getElementById("reader-search").addEventListener("input", event => { if (!event.target.value) { searchGeneration++; searchTerm = ""; matches = []; highlight(); } });
reader.setNavigate(page => display(page));
try {
    pdf = await pdfjs.getDocument({ url: reader.config.fileUrl, withCredentials: true, isEvalSupported: false, enableScripting: false,
        cMapUrl: "/lib/pdfjs/cmaps/", cMapPacked: true, standardFontDataUrl: "/lib/pdfjs/standard_fonts/", wasmUrl: "/lib/pdfjs/wasm/" }).promise;
    if (pdf.numPages !== reader.config.pageCount) throw new Error("Page metadata mismatch");
    document.getElementById("reader-total").textContent = pdf.numPages;
    await display(currentPage);
} catch { reader.failed(); }
