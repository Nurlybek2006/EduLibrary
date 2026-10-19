(() => {
    'use strict';
    const source = document.getElementById('Interests');
    const editor = document.getElementById('interest-editor');
    const fallback = document.getElementById('interest-textarea');
    const input = document.getElementById('new-interest');
    const chips = document.getElementById('interest-chips');
    const add = document.getElementById('add-interest');
    const error = document.getElementById('interest-editor-error');
    if (!source || !editor || !fallback || !input || !chips || !add || !error) return;
    let values = [...new Set(source.value.split(/[,;\n]/).map(value => value.trim()).filter(Boolean))];
    const render = () => {
        chips.replaceChildren();
        values.forEach((value, index) => {
            const chip = document.createElement('span');
            chip.className = 'recommendation-interest-chip';
            const label = document.createElement('span');
            label.textContent = value;
            const remove = document.createElement('button');
            remove.type = 'button';
            remove.textContent = '×';
            remove.setAttribute('aria-label', 'Қызығушылықты өшіру: ' + value);
            remove.addEventListener('click', () => {
                values.splice(index, 1);
                error.textContent = '';
                render();
                input.focus();
            });
            chip.append(label, remove);
            chips.append(chip);
        });
        source.value = values.join(', ');
    };
    const addInterest = () => {
        const next = input.value.trim();
        error.textContent = '';
        if (!next) return;
        if (next.length > 100 || /[,;\n]/.test(next)) { error.textContent = 'Бір атауды 100 таңбаға дейін енгізіңіз.'; return; }
        if (values.some(value => value.toLocaleLowerCase() === next.toLocaleLowerCase())) { error.textContent = 'Бұл қызығушылық тізімде бар.'; return; }
        if (values.length >= 20 || values.concat(next).join(', ').length > 1000) { error.textContent = 'Ең көбі 20 бағыт қосыңыз. Жалпы мәтін 1000 таңбадан аспасын.'; return; }
        values.push(next);
        input.value = '';
        render();
        input.focus();
    };
    add.addEventListener('click', addInterest);
    input.addEventListener('keydown', event => { if (event.key === 'Enter') { event.preventDefault(); addInterest(); } });
    source.form.addEventListener('submit', event => {
        if (input.value.trim()) {
            addInterest();
            if (input.value.trim()) { event.preventDefault(); input.focus(); }
        }
    });
    render();
    editor.hidden = false;
    fallback.hidden = true;
})();
