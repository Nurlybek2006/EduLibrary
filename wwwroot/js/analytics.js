(() => {
    'use strict';
    const initial = document.getElementById('analytics-data');
    if (!initial || !window.Chart) return;
    let state = JSON.parse(initial.textContent);
    const charts = new Map();
    const reducedMotion = window.matchMedia('(prefers-reduced-motion: reduce)').matches;
    const palette = ['#7c6ce7', '#45b9ab', '#63a5ed', '#e9ad51', '#d7769c', '#91b65a', '#aa86da', '#50abc1'];
    function draw(data) {
        state = data;
        const css = getComputedStyle(document.documentElement);
        const text = css.getPropertyValue('--text').trim() || '#333';
        const muted = css.getPropertyValue('--muted').trim() || '#777';
        const border = css.getPropertyValue('--border').trim() || '#ddd';
        data.charts.forEach(series => {
            charts.get(series.id)?.destroy();
            const canvas = document.getElementById('chart-' + series.id);
            const hasData = series.values.some(value => value > 0);
            canvas.hidden = !hasData;
            document.getElementById('empty-' + series.id).hidden = hasData;
            const table = document.getElementById('data-' + series.id);
            table.replaceChildren(...series.labels.map((label, index) => {
                const row = document.createElement('tr');
                [label, series.isRating ? series.values[index].toFixed(2) : series.values[index].toLocaleString('kk-KZ')].forEach(value => {
                    const cell = document.createElement('td'); cell.textContent = value; row.append(cell);
                });
                return row;
            }));
            if (!hasData) { charts.delete(series.id); return; }
            const doughnut = series.type === 'doughnut';
            charts.set(series.id, new Chart(canvas, {
                type: series.type,
                data: { labels: series.labels, datasets: [{ label: series.title, data: series.values,
                    borderColor: doughnut ? palette : palette[0], backgroundColor: doughnut ? palette : series.fill ? '#7c6ce730' : '#7c6ce7b0',
                    borderWidth: doughnut ? 2 : 2, fill: series.fill, tension: 0.25, pointRadius: series.labels.length > 40 ? 0 : 3, borderRadius: series.type === 'bar' ? 4 : 0 }] },
                options: { responsive: true, maintainAspectRatio: false, animation: reducedMotion ? false : { duration: 350 },
                    indexAxis: series.horizontal ? 'y' : 'x',
                    plugins: { legend: { display: doughnut, position: 'bottom', labels: { color: text, boxWidth: 12 } }, tooltip: { callbacks: { label: context => context.dataset.label + ': ' + (series.isRating ? Number(context.raw).toFixed(2) + ' / 5' : Number(context.raw).toLocaleString('kk-KZ')) } } },
                    scales: doughnut ? {} : {
                        x: { beginAtZero: series.horizontal, grid: { color: border, display: series.horizontal }, ticks: { color: muted, maxTicksLimit: 10, precision: 0 } },
                        y: { beginAtZero: true, max: series.isRating ? 5 : undefined, grid: { color: border, display: !series.horizontal }, ticks: { color: muted, precision: 0 } }
                    }
                }
            }));
        });
    }
    draw(state);
    new MutationObserver(() => draw(state)).observe(document.documentElement, { attributes: true, attributeFilter: ['data-theme'] });
    const form = document.getElementById('analytics-filter');
    let request;
    form.addEventListener('submit', async event => {
        event.preventDefault();
        request?.abort(); request = new AbortController();
        const activeRequest = request;
        const status = document.getElementById('analytics-status');
        status.textContent = 'SQL деректері жүктелуде…';
        form.setAttribute('aria-busy', 'true');
        try {
            const query = new URLSearchParams(new FormData(form));
            const response = await fetch(form.dataset.endpoint + '?' + query, { signal: request.signal, headers: { Accept: 'application/json' } });
            if (!response.ok) throw new Error(await response.text());
            const data = await response.json();
            const statistics = document.getElementById('analytics-statistics');
            statistics.replaceChildren(...data.statistics.map(metric => {
                const card = document.createElement('div'); card.className = 'stat-card';
                [['span', metric.label], ['strong', metric.value], ['small', metric.hint]].forEach(([tag, value]) => {
                    const element = document.createElement(tag); element.textContent = value; if (tag === 'small') element.className = 'text-muted'; card.append(element);
                });
                return card;
            }));
            const format = value => value.slice(0, 10).split('-').reverse().join('.');
            document.getElementById('analytics-period').textContent = format(data.startDate) + ' – ' + format(data.endDate);
            form.elements.StartDate.value = data.startDate.slice(0, 10);
            form.elements.EndDate.value = data.endDate.slice(0, 10);
            draw(data);
            history.replaceState(null, '', form.action.split('?')[0] + '?' + query);
            status.textContent = 'Деректер жаңартылды.';
        } catch (error) {
            if (error.name !== 'AbortError') status.textContent = 'Жаңарту сәтсіз аяқталды. Кезеңді тексеріп, қайталаңыз.';
        } finally { if (request === activeRequest) form.removeAttribute('aria-busy'); }
    });
})();
