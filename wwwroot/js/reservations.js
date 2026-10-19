(() => {
    "use strict";
    const form = document.getElementById("reservation-create-form");
    if (!form || form.dataset.queue === "true") return;
    const location = document.getElementById("PickupLocationId");
    const date = document.getElementById("PickupDate");
    const time = document.getElementById("PickupTime");
    const submit = document.getElementById("reservation-submit");
    const status = document.getElementById("reservation-slot-status");
    if (!location || !date || !time || !submit || !status) return;
    let request = null;
    let generation = 0;
    let loading = false;
    const available = () => Array.from(time.options).some(option => option.value && !option.disabled);
    const message = (text, error = false) => {
        status.textContent = text;
        status.classList.toggle("is-error", error);
        status.setAttribute("role", error ? "alert" : "status");
    };
    const update = () => {
        time.disabled = loading || !location.value || !date.value || !available();
        submit.disabled = loading || time.disabled || !time.value || time.selectedOptions[0]?.disabled;
        form.setAttribute("aria-busy", loading ? "true" : "false");
    };
    const load = async () => {
        request?.abort();
        request = new AbortController();
        const current = ++generation;
        const previous = time.value;
        time.replaceChildren(new Option("Уақытты таңдаңыз", ""));
        if (!location.value || !date.value) {
            loading = false;
            message("Алып кету орнын және күнін таңдаңыз.");
            update();
            return;
        }
        loading = true;
        message("Қолжетімді уақыттар тексерілуде…");
        update();
        const url = new URL(form.dataset.slotsUrl, window.location.origin);
        url.searchParams.set("pickupDate", date.value);
        url.searchParams.set("pickupLocationId", location.value);
        try {
            const response = await fetch(url, { credentials: "same-origin", headers: { "Accept": "application/json" }, signal: request.signal });
            const data = await response.json();
            if (current !== generation) return;
            if (!response.ok || !Array.isArray(data)) {
                const failure = new Error();
                failure.userMessage = typeof data?.message === "string" ? data.message : "Уақыттарды жүктеу мүмкін болмады. Күнді қайта таңдаңыз.";
                throw failure;
            }
            data.forEach(slot => {
                const option = new Option(slot.label, slot.value);
                option.disabled = slot.availableCapacity <= 0;
                if (slot.value === previous && !option.disabled) option.selected = true;
                time.add(option);
            });
            message(available() ? "Қолжетімді уақытты таңдаңыз. Орын бронь расталғанда қайта тексеріледі." : "Бұл күні бос уақыт жоқ. Басқа күнді немесе алып кету орнын таңдаңыз.");
        } catch (error) {
            if (error.name === "AbortError" || current !== generation) return;
            message(error.userMessage || "Уақыттарды жүктеу мүмкін болмады. Байланысты тексеріп, күнді қайта таңдаңыз.", true);
        } finally {
            if (current === generation) { loading = false; update(); }
        }
    };
    location.addEventListener("change", load);
    date.addEventListener("change", load);
    time.addEventListener("change", update);
    form.addEventListener("submit", event => {
        if (loading || time.disabled || !time.value) { event.preventDefault(); message("Бос алып кету уақытын таңдаңыз.", true); time.focus(); }
    });
    update();
    if (location.value && date.value && time.options.length <= 1) load();
})();
