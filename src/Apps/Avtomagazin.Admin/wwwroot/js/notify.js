window.avtomagazinNotify = {
    softAlert() {
        try {
            const AudioCtx = window.AudioContext || window.webkitAudioContext;
            if (!AudioCtx) {
                return;
            }

            const ctx = new AudioCtx();
            const now = ctx.currentTime;
            const gain = ctx.createGain();
            gain.gain.setValueAtTime(0.0001, now);
            gain.gain.exponentialRampToValueAtTime(0.045, now + 0.02);
            gain.gain.exponentialRampToValueAtTime(0.0001, now + 0.38);
            gain.connect(ctx.destination);

            const tone = (freq, start, dur) => {
                const osc = ctx.createOscillator();
                osc.type = "sine";
                osc.frequency.setValueAtTime(freq, start);
                osc.connect(gain);
                osc.start(start);
                osc.stop(start + dur);
            };

            tone(660, now, 0.14);
            tone(520, now + 0.16, 0.18);

            setTimeout(() => ctx.close().catch(() => {}), 600);
        } catch {
            // autoplay / unsupported — ignore
        }
    }
};
