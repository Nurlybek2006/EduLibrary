(() => {
    'use strict';
    const hideBrokenAvatar = image => { if (image.matches?.('img[data-user-avatar]')) image.hidden = true; };
    document.addEventListener('error', event => hideBrokenAvatar(event.target), true);
    document.querySelectorAll('img[data-user-avatar]').forEach(image => { if (image.complete && image.naturalWidth === 0) hideBrokenAvatar(image); });

    const form = document.querySelector('[data-avatar-editor]');
    if (!form) return;
    const input = form.querySelector('input[type="file"]');
    const panel = form.querySelector('.avatar-editor');
    const canvas = form.querySelector('[data-avatar-canvas]');
    const context = canvas.getContext('2d');
    const preview = form.querySelector('[data-avatar-preview]');
    const previewContext = preview.getContext('2d');
    const zoom = form.querySelector('[data-avatar-zoom]');
    const positionX = form.querySelector('[data-avatar-x]');
    const positionY = form.querySelector('[data-avatar-y]');
    const status = form.querySelector('[data-avatar-status]');
    const confirm = form.querySelector('[data-avatar-confirm]');
    const cancel = form.querySelector('[data-avatar-cancel]');
    let photo = null;
    let sequence = 0;
    let drag = null;
    confirm.disabled = true;
    const clamp = number => Math.max(0, Math.min(100, number));
    const message = (text, error = false) => { status.textContent = text; status.classList.toggle('text-danger', error); };
    const crop = () => {
        const size = Math.min(photo.naturalWidth, photo.naturalHeight) / Number(zoom.value);
        return { size, x: (photo.naturalWidth - size) * Number(positionX.value) / 100, y: (photo.naturalHeight - size) * Number(positionY.value) / 100 };
    };
    function render() {
        if (!photo) return;
        const selection = crop();
        context.clearRect(0, 0, canvas.width, canvas.height);
        context.drawImage(photo, selection.x, selection.y, selection.size, selection.size, 0, 0, canvas.width, canvas.height);
        previewContext.clearRect(0, 0, preview.width, preview.height);
        previewContext.drawImage(canvas, 0, 0, preview.width, preview.height);
        form.elements.CropX.value = (selection.x / photo.naturalWidth).toFixed(10);
        form.elements.CropY.value = (selection.y / photo.naturalHeight).toFixed(10);
        form.elements.CropSize.value = (selection.size / Math.min(photo.naturalWidth, photo.naturalHeight)).toFixed(10);
    }
    function reset(clearFile = true) {
        sequence++;
        photo = null;
        drag = null;
        panel.hidden = true;
        cancel.hidden = true;
        confirm.disabled = true;
        zoom.value = '1'; positionX.value = '50'; positionY.value = '50';
        form.elements.CropX.value = '0'; form.elements.CropY.value = '0'; form.elements.CropSize.value = '0';
        if (clearFile) {
            input.value = '';
            input.dispatchEvent(new Event('edulibrary:file-reset', { bubbles: true }));
        }
    }
    input.addEventListener('change', async () => {
        reset(false);
        const currentSequence = sequence;
        const file = input.files?.[0];
        message('');
        if (!file) return;
        if (!/\.(jpe?g|png|webp)$/i.test(file.name) || !['image/jpeg', 'image/png', 'image/webp'].includes(file.type.toLowerCase())) {
            reset(); message(form.dataset.invalidFormat, true); return;
        }
        if (file.size === 0 || file.size > 2 * 1024 * 1024) { reset(); message(form.dataset.tooLarge, true); return; }
        const source = URL.createObjectURL(file);
        const image = new Image();
        try {
            image.src = source;
            await image.decode();
            if (currentSequence !== sequence) return;
            if (image.naturalWidth > 4096 || image.naturalHeight > 4096 || image.naturalWidth * image.naturalHeight > 16000000) {
                reset(); message(form.dataset.dimensions, true); return;
            }
            photo = image;
            panel.hidden = false;
            cancel.hidden = false;
            confirm.disabled = false;
            message(form.dataset.ready);
            render();
        } catch { if (currentSequence === sequence) { reset(); message(form.dataset.invalidImage, true); } }
        finally { URL.revokeObjectURL(source); }
    });
    [zoom, positionX, positionY].forEach(control => control.addEventListener('input', render));
    canvas.addEventListener('pointerdown', event => {
        if (!photo || (event.pointerType === 'mouse' && event.button !== 0)) return;
        const selected = crop();
        drag = { pointerX: event.clientX, pointerY: event.clientY, cropX: selected.x, cropY: selected.y, size: selected.size };
        canvas.setPointerCapture(event.pointerId);
    });
    canvas.addEventListener('pointermove', event => {
        if (!drag || !photo) return;
        const scale = drag.size / canvas.getBoundingClientRect().width;
        const nextX = drag.cropX - (event.clientX - drag.pointerX) * scale;
        const nextY = drag.cropY - (event.clientY - drag.pointerY) * scale;
        positionX.value = String(clamp((photo.naturalWidth === drag.size ? 0.5 : nextX / (photo.naturalWidth - drag.size)) * 100));
        positionY.value = String(clamp((photo.naturalHeight === drag.size ? 0.5 : nextY / (photo.naturalHeight - drag.size)) * 100));
        render();
    });
    canvas.addEventListener('pointerup', () => { drag = null; });
    canvas.addEventListener('pointercancel', () => { drag = null; });
    canvas.addEventListener('keydown', event => {
        if (!photo || !['ArrowLeft', 'ArrowRight', 'ArrowUp', 'ArrowDown'].includes(event.key)) return;
        event.preventDefault();
        const step = event.shiftKey ? 10 : 1;
        if (event.key === 'ArrowLeft') positionX.value = String(clamp(Number(positionX.value) - step));
        if (event.key === 'ArrowRight') positionX.value = String(clamp(Number(positionX.value) + step));
        if (event.key === 'ArrowUp') positionY.value = String(clamp(Number(positionY.value) - step));
        if (event.key === 'ArrowDown') positionY.value = String(clamp(Number(positionY.value) + step));
        render();
    });
    cancel.addEventListener('click', () => { reset(); message(''); input.focus(); });
    form.addEventListener('submit', event => {
        if (!photo || !input.files?.length) { event.preventDefault(); message(form.dataset.invalidImage, true); return; }
        render(); confirm.disabled = true; message(form.dataset.uploading);
    });
})();
