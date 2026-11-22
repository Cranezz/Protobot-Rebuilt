"""An original backing track for the Protobot trailer, synthesized from scratch (no samples, no licensed music):
120 BPM in A minor, laid out to the trailer's sections. Writes a 48 kHz stereo WAV.

    python -I Tools/Video/make_music.py OUT.wav [SECONDS]
"""
import sys
import wave

import numpy as np

SR = 48000
BPM = 120.0
BEAT = 60.0 / BPM
BAR = 4 * BEAT
rng = np.random.default_rng(7)


def t_of(seconds):
    return np.arange(int(seconds * SR)) / SR


def note(n):  # MIDI note -> Hz
    return 440.0 * 2 ** ((n - 69) / 12)


def lowpass(x, cutoff):
    # one-pole low-pass, cutoff in Hz (array or scalar)
    a = np.exp(-2 * np.pi * np.broadcast_to(cutoff, x.shape) / SR)
    y = np.empty_like(x)
    acc = 0.0
    for i in range(len(x)):
        acc = (1 - a[i]) * x[i] + a[i] * acc
        y[i] = acc
    return y


def lowpass_fast(x, cutoff):
    a = np.exp(-2 * np.pi * cutoff / SR)
    from scipy.signal import lfilter  # noqa
    return lfilter([1 - a], [1, -a], x)


try:
    import scipy.signal  # noqa: F401
    LP = lowpass_fast
except Exception:  # scipy missing: slower pure-numpy filter, constant cutoff only
    def LP(x, cutoff):
        return lowpass(x, cutoff)


def saw(freq, t, detune=0.0):
    ph = (freq * (1 + detune) * t) % 1.0
    return 2 * ph - 1


def supersaw(freq, t, voices=5, spread=0.012):
    out = np.zeros_like(t)
    for k in range(voices):
        d = (k - (voices - 1) / 2) / ((voices - 1) / 2) * spread
        out += saw(freq, t + rng.random() * 0.01, d)
    return out / voices


def env_adsr(n, a, d, s, r, length):
    t = np.arange(n) / SR
    e = np.ones(n) * s
    e[t < a] = t[t < a] / max(a, 1e-4)
    m = (t >= a) & (t < a + d)
    e[m] = 1 - (1 - s) * (t[m] - a) / max(d, 1e-4)
    rel = t >= length
    e[rel] = s * np.exp(-(t[rel] - length) / max(r, 1e-4))
    return e


def add(buf, start, sig, gain=1.0, pan=0.0):
    i = int(start * SR)
    if i >= buf.shape[1]:
        return
    j = min(buf.shape[1], i + len(sig))
    s = sig[: j - i] * gain
    buf[0, i:j] += s * np.sqrt(0.5 * (1 - pan))
    buf[1, i:j] += s * np.sqrt(0.5 * (1 + pan))


def kick():
    t = t_of(0.45)
    f = 45 + 110 * np.exp(-t * 28)
    ph = 2 * np.pi * np.cumsum(f) / SR
    return np.sin(ph) * np.exp(-t * 7.5) + 0.25 * np.sin(ph * 2) * np.exp(-t * 30)


def hat(open_=False):
    t = t_of(0.25 if open_ else 0.06)
    n = rng.standard_normal(len(t))
    n = n - LP(n, 7000)
    return n * np.exp(-t * (14 if open_ else 70))


def clap():
    t = t_of(0.3)
    n = rng.standard_normal(len(t))
    n = LP(n - LP(n, 900), 5000)
    e = np.exp(-t * 18) + 0.6 * np.exp(-np.maximum(t - 0.012, 0) * 22) * (t > 0.012)
    return n * e


def pluck(freq, length=0.32):
    t = t_of(length)
    x = supersaw(freq, t, voices=3, spread=0.006)
    cut = 600 + 5200 * np.exp(-t * 14)
    y = LP(x, float(np.mean(cut)))
    return y * np.exp(-t * 9)


def pad(freqs, length, bright=1800.0):
    t = t_of(length)
    x = sum(supersaw(f, t, 5, 0.01) for f in freqs) / len(freqs)
    x = LP(x, bright)
    return x * env_adsr(len(t), 0.6, 0.4, 0.8, 0.8, length - 0.8)


def bass(freq, length):
    t = t_of(length)
    x = np.sin(2 * np.pi * freq * t) + 0.3 * saw(freq, t)
    x = LP(x, 380)
    return x * env_adsr(len(t), 0.005, 0.1, 0.85, 0.06, length - 0.06)


def riser(length):
    t = t_of(length)
    n = rng.standard_normal(len(t))
    out = np.zeros_like(n)
    steps = 12
    for k in range(steps):
        seg = slice(int(k / steps * len(n)), int((k + 1) / steps * len(n)))
        out[seg] = LP(n[seg], 400 + 9000 * (k / steps) ** 2)
    return out * (t / length) ** 2 * 0.9


def impact():
    t = t_of(2.5)
    boom = np.sin(2 * np.pi * (38 + 60 * np.exp(-t * 6)) * t) * np.exp(-t * 1.6)
    n = rng.standard_normal(len(t))
    return boom + 0.35 * LP(n, 1200) * np.exp(-t * 3)


# A minor: Am - F - C - G (i - VI - III - VII), one chord a bar.
CHORDS = [[57, 60, 64], [53, 57, 60], [48, 52, 55], [55, 59, 62]]
ROOTS = [33, 29, 36, 31]


def build(seconds):
    buf = np.zeros((2, int((seconds + 3) * SR)))
    bars = int(seconds / BAR) + 1
    # Sections (bars of 2 s): intro 0-3, build 3-12, drop 12-30, break 30-33, final 33-38, outro.
    for b in range(bars):
        start = b * BAR
        chord = CHORDS[b % 4]
        root = ROOTS[b % 4]
        section = "intro" if b < 3 else "build" if b < 12 else "drop" if b < 30 else "break" if b < 33 else "final" if b < 38 else "outro"
        if section == "outro" and start > seconds - 1:
            break
        bright = {"intro": 700, "build": 1400, "drop": 3200, "break": 900, "final": 3600, "outro": 800}[section]
        add(buf, start, pad([note(n) for n in chord] + [note(chord[0] + 12)], BAR + 0.8, bright), 0.30)
        if section in ("build", "drop", "final"):
            # arpeggio in sixteenths
            seq = [chord[0] + 12, chord[1] + 12, chord[2] + 12, chord[1] + 24]
            for s16 in range(16):
                if section == "build" and s16 % 2:
                    continue
                f = note(seq[s16 % 4])
                add(buf, start + s16 * BEAT / 4, pluck(f), 0.16 if section != "build" else 0.12, pan=(-0.35 if s16 % 2 else 0.35))
        if section in ("drop", "final"):
            for beat in range(4):
                add(buf, start + beat * BEAT, kick(), 0.85)
                add(buf, start + beat * BEAT + BEAT / 2, hat(open_=True), 0.10, 0.2)
                for s in range(4):
                    add(buf, start + beat * BEAT + s * BEAT / 4, hat(), 0.07, -0.2)
                if beat in (1, 3):
                    add(buf, start + beat * BEAT, clap(), 0.35)
                add(buf, start + beat * BEAT, bass(note(root + 12), BEAT * 0.9), 0.55)
                add(buf, start + beat * BEAT + BEAT / 2, bass(note(root + 12), BEAT * 0.4), 0.35)
        elif section == "build" and b >= 7:
            for beat in range(4):
                add(buf, start + beat * BEAT, kick(), 0.55 if beat == 0 else 0.35)
        if b in (11, 32):  # risers into the drop and the final
            add(buf, start, riser(BAR), 0.35)
    for at in (12 * BAR, 33 * BAR):
        add(buf, at, impact(), 0.7)
    add(buf, 38 * BAR, impact(), 0.8)
    # gentle tape-style saturation and a fade at the very end
    buf = np.tanh(buf * 1.2) / np.tanh(1.2)
    end = int(seconds * SR)
    buf = buf[:, : end + int(2.5 * SR)]
    fade = np.ones(buf.shape[1])
    fade[end:] = np.linspace(1, 0, buf.shape[1] - end)
    buf *= fade
    return buf / max(1e-6, np.max(np.abs(buf))) * 0.89


def write(path, buf):
    data = (np.clip(buf.T, -1, 1) * 32767).astype(np.int16)
    with wave.open(path, "wb") as w:
        w.setnchannels(2)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes(data.tobytes())


if __name__ == "__main__":
    out = sys.argv[1]
    seconds = float(sys.argv[2]) if len(sys.argv) > 2 else 80.0
    write(out, build(seconds))
    print("wrote", out)
