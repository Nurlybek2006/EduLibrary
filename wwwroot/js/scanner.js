(() => {
    "use strict";

    document.querySelectorAll("[data-print-label]").forEach(button => {
        button.addEventListener("click", () => window.print());
    });
    const panel = document.querySelector("[data-copy-scanner]");
    if (!panel) return;
    const video = document.getElementById("copy-scanner-video");
    const idle = document.getElementById("copy-scanner-idle");
    const startButton = document.getElementById("copy-scanner-start");
    const stopButton = document.getElementById("copy-scanner-stop");
    const status = document.getElementById("copy-scanner-status");
    const codeInput = document.getElementById("copy-code");
    const form = document.getElementById("copy-code-form");
    let stream = null;
    let controls = null;
    let timer = null;
    let session = 0;
    let found = false;

    const message = text => { status.textContent = text; };
    const stop = (showMessage = true) => {
        session += 1;
        if (timer !== null) window.clearTimeout(timer);
        timer = null;
        if (controls) { controls.stop(); controls = null; }
        if (stream) stream.getTracks().forEach(track => track.stop());
        stream = null;
        if (video.srcObject) video.srcObject.getTracks().forEach(track => track.stop());
        video.pause();
        video.srcObject = null;
        video.hidden = true;
        idle.hidden = false;
        startButton.disabled = false;
        stopButton.disabled = true;
        if (showMessage) message("Камера өшірілді. Кодты қолмен енгізуге болады.");
    };

    const acceptCode = raw => {
        const code = typeof raw === "string" ? raw.trim() : "";
        if (found || !code) return;
        if (code.length > 100 || /[\x00-\x1f\x7f]/.test(code)) {
            stop(false);
            message("Бұл QR код кітап данасының коды ретінде жарамсыз. Штрихкодты қолмен енгізіңіз.");
            codeInput.focus();
            return;
        }
        found = true;
        codeInput.value = code;
        stop(false);
        message("Код оқылды. Кітап данасы ізделуде…");
        // Decoded text is submitted as a value to our own route, never navigated as a URL.
        form.requestSubmit();
    };

    const nativeReader = async () => {
        if (!("BarcodeDetector" in window)) return null;
        try {
            const supported = await window.BarcodeDetector.getSupportedFormats();
            if (!supported.includes("qr_code")) return null;
            const formats = ["qr_code", "code_128", "code_39", "ean_13", "ean_8"].filter(format => supported.includes(format));
            return new window.BarcodeDetector({ formats });
        } catch { return null; }
    };

    const startZxing = async currentSession => {
        if (!window.ZXingBrowser?.BrowserQRCodeReader) {
            stop(false);
            message("Камералық сканер қолжетімсіз. Кодты қолмен немесе USB сканермен енгізіңіз.");
            return;
        }
        const reader = new window.ZXingBrowser.BrowserQRCodeReader();
        const newControls = await reader.decodeFromStream(stream, video, (result, error, callbackControls) => {
            if (currentSession !== session) { callbackControls.stop(); return; }
            if (result) acceptCode(result.getText());
        });
        if (currentSession !== session) newControls.stop();
        else controls = newControls;
    };

    const start = async () => {
        stop(false);
        found = false;
        const currentSession = session;
        if (!window.isSecureContext || !navigator.mediaDevices?.getUserMedia) {
            message("Камера үшін HTTPS немесе localhost қажет. Кодты қолмен енгізіңіз.");
            return;
        }
        startButton.disabled = true;
        stopButton.disabled = false;
        message("Камераға рұқсат күтілуде…");
        try {
            const acquired = await navigator.mediaDevices.getUserMedia({ video: { facingMode: { ideal: "environment" } }, audio: false });
            if (currentSession !== session) { acquired.getTracks().forEach(track => track.stop()); return; }
            stream = acquired;
            video.srcObject = stream;
            video.hidden = false;
            idle.hidden = true;
            await video.play();
            if (currentSession !== session) return;
            message("QR кодты камераға жақындатып, анық көрсетіңіз.");
            const detector = await nativeReader();
            if (currentSession !== session) return;
            if (!detector) { await startZxing(currentSession); return; }
            const scan = async () => {
                if (currentSession !== session || !stream) return;
                try {
                    if (video.readyState >= 2) {
                        const codes = await detector.detect(video);
                        if (currentSession !== session) return;
                        if (codes.length > 0) { acceptCode(codes[0].rawValue); return; }
                    }
                } catch {
                    if (currentSession === session) {
                        try { await startZxing(currentSession); }
                        catch {
                            stop(false);
                            message("QR сканерін іске қосу мүмкін болмады. Кодты қолмен енгізіңіз.");
                        }
                    }
                    return;
                }
                if (currentSession === session) timer = window.setTimeout(scan, 250);
            };
            await scan();
        } catch (error) {
            if (currentSession !== session) return;
            stop(false);
            message(error?.name === "NotAllowedError"
                ? "Камераға рұқсат берілмеді. Кодты қолмен немесе USB сканермен енгізіңіз."
                : "Камераны қосу мүмкін болмады. Құрылғыны тексеріңіз немесе кодты қолмен енгізіңіз.");
        }
    };

    startButton.addEventListener("click", start);
    stopButton.addEventListener("click", () => stop());
    form.addEventListener("submit", () => stop(false));
    window.addEventListener("pagehide", () => stop(false));
    window.addEventListener("beforeunload", () => stop(false));
    document.addEventListener("visibilitychange", () => { if (document.hidden) stop(); });
})();
