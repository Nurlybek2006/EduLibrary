(() => {
    "use strict";
    const form = document.querySelector("#catalog-filter");
    const results = document.querySelector("#catalog-results");
    if (!form || !results) return;
    const status = document.querySelector("#catalog-status");
    const drawer = form.querySelector(".catalog-filter-drawer");
    let pending, timer, sequence = 0;
    let view = "grid";
    try { view = localStorage.getItem("edulibrary-catalog-view") === "list" ? "list" : "grid"; } catch {}

    const closeDrawer = (focusSummary = false) => {
        if (!drawer?.open) return;
        drawer.open = false;
        if (focusSummary) drawer.querySelector("summary")?.focus();
    };
    const updateFilterCount = () => {
        if (!drawer) return;
        const names = ["AuthorId", "PublisherId", "CategoryId", "SubjectId", "Language"];
        let count = names.filter(name => Boolean(form.elements.namedItem(name)?.value)).length;
        for (const name of ["AvailableOnly", "IncludeArchived"]) {
            if (form.elements.namedItem(name)?.checked) count += 1;
        }
        let badge = drawer.querySelector(".filter-active-count");
        if (!count) { badge?.remove(); return; }
        if (!badge) {
            badge = document.createElement("span");
            badge.className = "filter-active-count";
            drawer.querySelector("summary").append(badge);
        }
        badge.textContent = String(count);
        badge.setAttribute("aria-label", count + " белсенді сүзгі");
    };
    const announce = (text, failed = false) => {
        if (!status) return;
        status.classList.toggle("visually-hidden", !failed);
        status.classList.toggle("catalog-error-status", failed);
        status.setAttribute("role", failed ? "alert" : "status");
        status.setAttribute("aria-live", failed ? "assertive" : "polite");
        status.textContent = text;
    };
    const updateView = () => {
        results.classList.toggle("list-view", view === "list");
        document.querySelectorAll("[data-catalog-view]").forEach(button => {
            button.classList.toggle("active", button.dataset.catalogView === view);
            button.setAttribute("aria-pressed", String(button.dataset.catalogView === view));
        });
    };
    async function load(url, push = true) {
        pending?.abort();
        pending = new AbortController();
        const own = ++sequence;
        results.setAttribute("aria-busy", "true");
        results.classList.add("is-fetching");
        announce("Кітаптар ізделуде…");
        try {
            const endpoint = new URL("/Books/Results", location.origin);
            endpoint.search = new URL(url, location.origin).search;
            const response = await fetch(endpoint, { signal: pending.signal, headers: { "X-Requested-With": "XMLHttpRequest" } });
            if (!response.ok) throw new Error("Search request failed");
            const html = await response.text();
            if (own !== sequence) return;
            results.innerHTML = html;
            window.lucide?.createIcons();
            updateView();
            updateFilterCount();
            const count = results.querySelector("[data-result-count]")?.dataset.resultCount;
            announce(count ? count + " кітап табылды." : "Іздеу аяқталды.");
            if (push) history.pushState(null, "", url);
        } catch (error) {
            if (own === sequence && error.name !== "AbortError") {
                announce("Іздеу орындалмады. Іздеу батырмасымен қайталап көріңіз.", true);
            }
        } finally {
            if (own === sequence) {
                results.removeAttribute("aria-busy");
                results.classList.remove("is-fetching");
            }
        }
    }
    function search() {
        updateFilterCount();
        const query = new URLSearchParams(new FormData(form));
        load("/Books?" + query);
    }
    form.addEventListener("submit", event => {
        event.preventDefault();
        clearTimeout(timer);
        closeDrawer();
        search();
    });
    form.querySelectorAll("select,input[type=checkbox]").forEach(input => input.addEventListener("change", search));
    form.querySelector("[name=Search]")?.addEventListener("input", () => {
        clearTimeout(timer);
        timer = setTimeout(search, 320);
    });
    results.addEventListener("click", event => {
        const link = event.target.closest(".pagination a");
        if (!link || event.button !== 0 || event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) return;
        event.preventDefault();
        closeDrawer();
        load(link.href);
    });
    document.querySelectorAll("[data-catalog-view]").forEach(button => button.addEventListener("click", () => {
        view = button.dataset.catalogView === "list" ? "list" : "grid";
        try { localStorage.setItem("edulibrary-catalog-view", view); } catch {}
        updateView();
    }));
    document.addEventListener("keydown", event => {
        if (event.key === "Escape" && drawer?.open) {
            closeDrawer(true);
            event.preventDefault();
        }
    });
    document.addEventListener("click", event => {
        if (drawer?.open && !drawer.contains(event.target)) closeDrawer();
    });
    window.addEventListener("popstate", () => {
        const query = new Map([...new URLSearchParams(location.search)].map(([key, value]) => [key.toLowerCase(), value]));
        for (const input of form.elements) {
            if (!input.name) continue;
            const value = query.get(input.name.toLowerCase());
            if (input.type === "checkbox") input.checked = value?.toLowerCase() === "true";
            else if (input.tagName === "INPUT" || input.tagName === "SELECT") input.value = value || (input.name === "Sort" ? "title" : "");
        }
        closeDrawer();
        updateFilterCount();
        load(location.href, false);
    });
    updateFilterCount();
    updateView();
})();
