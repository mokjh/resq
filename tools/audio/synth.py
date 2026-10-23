#!/usr/bin/env python3
"""RESQ audio, synthesised from scratch (no samples, no generated-audio services): every sound is original and
licence-clean. Writes mono 16-bit WAVs into game/Assets/Resq/Content/Resources/ResqAudio (Unity compresses them to
Vorbis on import).

    uv run --with numpy tools/audio/synth.py            # everything
    uv run --with numpy tools/audio/synth.py sfx        # effects only

Direction (Resq_Production_Pack/docs/07 §11 and the world bible's sound notes): restrained, warm, original;
calm / problem / resolve stems per world at 80 BPM; critical signals stay distinguishable at low volume; no sudden
full-volume cracks. Each world has its own instrument family:
  W1 Alder Valley  plucked strings (Karplus-Strong), water over stone
  W2 Emberwood     soft mallets and frame drum, dry needles and restrained crackle
  W3 Frostline     glass bells and a string pad, wind and snow
  W4 Tidemark      marimba and buoy bells, water under piers
  W5 Cinder Reach  low drone and sparse metallic tones, geothermal rumble
"""
import sys, wave, pathlib
import numpy as np

SR = 32000
ROOT = pathlib.Path(__file__).resolve().parents[2]
OUT = ROOT / "game/Assets/Resq/Content/Resources/ResqAudio"
RNG = np.random.default_rng(20261024)   # fixed seed: rebuilding gives identical files
BPM = 80
BEAT = 60.0 / BPM
BARS = 8
LOOP = BARS * 4 * BEAT                   # 24 s loops


# ---------------------------------------------------------------- primitives
def t(dur):
    return np.arange(int(dur * SR)) / SR

def note(name):
    names = {"C": 0, "C#": 1, "D": 2, "D#": 3, "E": 4, "F": 5, "F#": 6, "G": 7, "G#": 8, "A": 9, "A#": 10, "B": 11}
    n, o = name[:-1], int(name[-1])
    return 440.0 * 2 ** ((names[n] + 12 * (o + 1) - 69) / 12)

def env(n, a=0.005, d=0.3, s=0.0, r=0.1, total=None):
    """Attack/decay/sustain/release envelope over n samples."""
    e = np.zeros(n)
    A, D, R = int(a * SR), int(d * SR), int(r * SR)
    A = max(A, 1)
    e[:A] = np.linspace(0, 1, A)[: len(e[:A])]
    end = min(n, A + D)
    if end > A: e[A:end] = np.linspace(1, s, end - A)
    if end < n: e[end:] = s
    if R > 0 and n > R: e[-R:] *= np.linspace(1, 0, R)
    return e

def lowpass(x, cutoff):
    """One-pole low-pass (cheap, smooth)."""
    a = np.exp(-2 * np.pi * cutoff / SR)
    y = np.empty_like(x); acc = 0.0
    for i in range(len(x)):
        acc = (1 - a) * x[i] + a * acc
        y[i] = acc
    return y

def lowpass_fast(x, cutoff):
    """Box-filter cascade approximating a low-pass for long buffers (pure numpy, fast)."""
    k = max(1, int(SR / (cutoff * 2.0)))
    ker = np.ones(k) / k
    y = np.convolve(x, ker, mode="same")
    return np.convolve(y, ker, mode="same")

def highpass_fast(x, cutoff):
    return x - lowpass_fast(x, cutoff)

def noise(dur):
    return RNG.standard_normal(int(dur * SR))

def sine(f, dur, phase=0.0):
    return np.sin(2 * np.pi * f * t(dur) + phase)

def pluck(f, dur, bright=0.5):
    """Karplus-Strong plucked string."""
    n = int(dur * SR); period = max(2, int(SR / f))
    buf = RNG.uniform(-1, 1, period) * 0.8
    out = np.empty(n)
    damp = 0.996 - (1 - bright) * 0.01
    for i in range(n):
        j = i % period
        out[i] = buf[j]
        buf[j] = damp * 0.5 * (buf[j] + buf[(j + 1) % period])
    return out * env(n, 0.001, dur, 0, 0.05)

def mallet(f, dur, hard=0.4):
    """Marimba-ish: fundamental plus 4th harmonic and a soft click."""
    tt = t(dur)
    body = np.sin(2 * np.pi * f * tt) * np.exp(-tt * 4) + 0.35 * np.sin(2 * np.pi * f * 3.98 * tt) * np.exp(-tt * 12)
    click = noise(dur) * np.exp(-tt * 300) * hard * 0.3
    return (body + click) * env(len(tt), 0.002, dur, 0, 0.03)

def bell(f, dur, ratio=3.5, index=2.2, decay=2.0):
    """FM bell (glass/metal)."""
    tt = t(dur)
    mod = index * np.exp(-tt * decay * 0.8) * np.sin(2 * np.pi * f * ratio * tt)
    return np.sin(2 * np.pi * f * tt + mod) * np.exp(-tt * decay)

def pad(freqs, dur, cutoff=1200, detune=0.004):
    """Soft string pad: detuned saws, low-passed, slow swell."""
    tt = t(dur); x = np.zeros(len(tt))
    for f in freqs:
        for d in (-detune, 0, detune):
            ph = (tt * f * (1 + d)) % 1.0
            x += (2 * ph - 1)
    x = lowpass_fast(x / (len(freqs) * 3), cutoff)
    return x * env(len(tt), dur * 0.35, 0, 1, dur * 0.4)

def drum(dur=0.5, f=90, tone=0.7):
    """Frame drum: pitched thump with a short noise skin."""
    tt = t(dur)
    pitch = f * (1 + 0.6 * np.exp(-tt * 30))
    body = np.sin(2 * np.pi * np.cumsum(pitch) / SR) * np.exp(-tt * 9) * tone
    skin = lowpass_fast(noise(dur), 2500) * np.exp(-tt * 25) * 0.4
    return body + skin

def mix(*xs):
    """Sum buffers of different lengths (shorter ones are zero-padded)."""
    n = max(len(x) for x in xs); out = np.zeros(n)
    for x in xs: out[:len(x)] += x
    return out

def place(buf, x, at):
    i = int(at * SR)
    if i >= len(buf): return
    n = min(len(x), len(buf) - i)
    buf[i:i + n] += x[:n]

def normalize(x, peak=0.89):
    m = np.max(np.abs(x))
    return x if m < 1e-9 else x * (peak / m)

def fade(x, a=0.01, r=0.03):
    n = len(x); A, R = int(a * SR), int(r * SR)
    if A: x[:A] *= np.linspace(0, 1, A)
    if R: x[-R:] *= np.linspace(1, 0, R)
    return x

def loop_seamless(x, xfade=1.5):
    """Crossfade the tail into the head so the loop has no seam."""
    n = int(xfade * SR)
    head, tail = x[:n].copy(), x[-n:].copy()
    w = np.linspace(0, 1, n)
    x[:n] = head * w + tail * (1 - w)
    return x[:-n]

def write(name, x, peak=0.89):
    OUT.mkdir(parents=True, exist_ok=True)
    x = normalize(np.asarray(x, dtype=np.float64), peak)
    pcm = (np.clip(x, -1, 1) * 32767).astype("<i2")
    with wave.open(str(OUT / f"{name}.wav"), "wb") as w:
        w.setnchannels(1); w.setsampwidth(2); w.setframerate(SR)
        w.writeframes(pcm.tobytes())
    print(f"  {name}.wav  {len(x) / SR:5.2f}s")


# ---------------------------------------------------------------- shared effects
def sfx():
    print("effects")
    # UI: quiet tick on hover, soft wooden press
    write("ui_hover", fade(bell(2400, 0.05, 2.0, 0.6, 60)), 0.35)
    write("ui_press", fade(mix(mallet(880, 0.12, 0.8), 0.4 * mallet(1320, 0.1, 0.5))), 0.6)
    # placement lock: low wooden thunk plus a latch click
    tt = t(0.35)
    thunk = np.sin(2 * np.pi * 140 * tt) * np.exp(-tt * 18) + 0.5 * np.sin(2 * np.pi * 210 * tt) * np.exp(-tt * 25)
    latch = np.zeros_like(tt); place(latch, highpass_fast(noise(0.02), 2000) * 0.5, 0.07)
    write("place_lock", fade(thunk + latch), 0.8)
    # cancel: two soft descending taps
    c = np.zeros(int(0.3 * SR)); place(c, mallet(660, 0.15, 0.3), 0); place(c, mallet(495, 0.15, 0.3), 0.09)
    write("cancel", fade(c), 0.5)
    # blocked target: a soft low two-tone "nope" (distinct at low volume, never harsh)
    b = np.zeros(int(0.4 * SR)); place(b, sine(220, 0.14) * env(int(0.14 * SR), 0.005, 0.12), 0); place(b, sine(185, 0.18) * env(int(0.18 * SR), 0.005, 0.16), 0.13)
    write("blocked", fade(lowpass_fast(b, 1800)), 0.55)
    # warning: gentle rising two-note alert with a bell tail
    wv = np.zeros(int(1.0 * SR)); place(wv, bell(784, 0.8, 2.0, 1.2, 3.5), 0); place(wv, bell(988, 0.8, 2.0, 1.2, 3.0), 0.22)
    write("warning", fade(wv), 0.75)
    # rescue acknowledgement: the gentle three-note chime (W1 bible), used in every world
    ch = np.zeros(int(1.4 * SR))
    for i, n in enumerate(["D5", "F#5", "A5"]): place(ch, mix(bell(note(n), 1.0, 2.0, 0.8, 2.6) * 0.8, mallet(note(n), 0.5, 0.2) * 0.4), i * 0.14)
    write("rescue_chime", fade(ch), 0.7)
    # a single resident safe: one soft tick of the chime
    write("resident_safe", fade(bell(note("A5"), 0.4, 2.0, 0.6, 5) * 0.7), 0.4)
    # boarding: soft footsteps on a gangplank
    bd = np.zeros(int(0.7 * SR))
    for i in range(4): place(bd, lowpass_fast(noise(0.06), 900) * np.exp(-t(0.06) * 40) * (0.8 + 0.2 * RNG.random()), 0.05 + i * 0.15)
    write("boarding", fade(bd), 0.6)
    # boat departs: small outboard engine (pulsed buzz, swelling then fading)
    dur = 1.8; tt = t(dur)
    f0 = 70 + 25 * np.minimum(tt / 0.6, 1)
    motor = np.sign(np.sin(2 * np.pi * np.cumsum(f0) / SR)) * 0.5 + lowpass_fast(noise(dur), 600) * 0.6
    motor = lowpass_fast(motor, 900) * env(len(tt), 0.25, 0, 1, 0.9)
    write("boat_depart", fade(motor), 0.55)
    # vehicle arrival: engine settles plus a wooden bump
    ar = np.zeros(int(1.0 * SR)); place(ar, lowpass_fast(noise(0.6), 500) * env(int(0.6 * SR), 0.01, 0.55), 0)
    place(ar, np.sin(2 * np.pi * 120 * t(0.25)) * np.exp(-t(0.25) * 20) * 0.8, 0.45)
    write("vehicle_arrive", fade(ar), 0.55)
    # water diversion: a soft whoosh
    dur = 1.2; w = noise(dur); w = lowpass_fast(w, 1400) - lowpass_fast(w, 200)
    write("water_divert", fade(w * np.sin(np.pi * t(dur) / dur) ** 2), 0.55)
    # pump start/stop: motor spins up / down
    def motor_sweep(up, dur=1.0):
        tt = t(dur); k = tt / dur if up else 1 - tt / dur
        f = 60 + 140 * k
        m = np.sin(2 * np.pi * np.cumsum(f) / SR) + 0.3 * np.sin(4 * np.pi * np.cumsum(f) / SR)
        return lowpass_fast(m + lowpass_fast(noise(dur), 400) * 0.3, 1500) * (np.minimum(tt / 0.1, 1) * np.minimum((dur - tt) / 0.15, 1))
    write("pump_start", fade(motor_sweep(True)), 0.5)
    write("pump_stop", fade(motor_sweep(False)), 0.45)
    # power restored: rising soft hum and a click
    dur = 0.9; tt = t(dur); hum = np.sin(2 * np.pi * (100 + 60 * tt / dur) * tt) * env(len(tt), 0.2, 0, 1, 0.3)
    place(hum, highpass_fast(noise(0.02), 3000) * 0.4, 0.02)
    write("power_on", fade(hum + 0.3 * np.sin(2 * np.pi * 300 * tt) * env(len(tt), 0.4, 0, 1, 0.3)), 0.5)
    dur = 0.9; tt = t(dur)
    write("power_off", fade(np.sin(2 * np.pi * (160 - 80 * tt / dur) * tt) * env(len(tt), 0.01, 0, 1, 0.5)), 0.45)
    # firebreak: a soft scrape of a rake through needles
    dur = 0.9; fb = highpass_fast(noise(dur), 1500) * (0.4 + 0.6 * np.abs(np.sin(2 * np.pi * 3 * t(dur))))
    write("firebreak", fade(lowpass_fast(fb, 5000) * env(int(dur * SR), 0.05, 0, 1, 0.3)), 0.45)
    # winch: geared motor with cable tension whine
    dur = 1.6; tt = t(dur)
    gear = np.sign(np.sin(2 * np.pi * 38 * tt)) * 0.3 + np.sin(2 * np.pi * 640 * tt + 3 * np.sin(2 * np.pi * 2 * tt)) * 0.15
    write("winch", fade(lowpass_fast(gear, 2200) * env(len(tt), 0.15, 0, 1, 0.4)), 0.45)
    # cooling: soft chime plus a steam hiss (W5: communicates a traversable crossing)
    co = np.zeros(int(1.6 * SR)); place(co, bell(note("E6"), 1.2, 1.5, 0.8, 2.2) * 0.8, 0)
    place(co, highpass_fast(noise(1.2), 2500) * env(int(1.2 * SR), 0.05, 0, 1, 0.8) * 0.25, 0.05)
    write("cooling", fade(co), 0.6)
    # structure warning: a low wooden creak (no sudden full-volume crack)
    dur = 1.0; tt = t(dur); creak = np.sin(2 * np.pi * (90 + 30 * np.sin(2 * np.pi * 1.5 * tt)) * tt) * (0.5 + 0.5 * np.abs(np.sin(2 * np.pi * 7 * tt)))
    write("creak", fade(lowpass_fast(creak, 900) * env(len(tt), 0.1, 0, 1, 0.4)), 0.45)
    # collapse: a muffled thud and rumble, kept soft
    dur = 1.6; tt = t(dur)
    write("collapse", fade(mix(lowpass_fast(noise(dur), 300) * np.exp(-tt * 2.5), np.sin(2 * np.pi * 55 * tt) * np.exp(-tt * 4) * 0.8)), 0.6)
    # outcome stings
    win = np.zeros(int(2.4 * SR))
    for i, n in enumerate(["D5", "F#5", "A5", "D6"]): place(win, mix(bell(note(n), 1.6, 2.0, 0.8, 1.8) * 0.7, pluck(note(n) / 2, 1.2, 0.6) * 0.5), i * 0.18)
    write("outcome_success", fade(win), 0.75)
    lose = np.zeros(int(2.0 * SR))
    for i, n in enumerate(["A4", "F4", "D4"]): place(lose, mallet(note(n), 1.2, 0.2) * 0.8, i * 0.28)
    write("outcome_backup", fade(lowpass_fast(lose, 2500)), 0.6)
    write("star", fade(bell(note("B6"), 0.6, 2.0, 0.6, 4) * 0.7), 0.45)


# ---------------------------------------------------------------- ambiences (seamless loops)
def ambiences():
    print("ambiences")
    dur = 20.0
    # W1 water over stone: band-limited noise with burbles
    base = lowpass_fast(noise(dur + 1.5), 1500) - lowpass_fast(noise(dur + 1.5), 150)
    burble = np.zeros(len(base))
    for _ in range(140):
        f = RNG.uniform(500, 1400); d = RNG.uniform(0.03, 0.09)
        place(burble, sine(f, d) * np.exp(-t(d) * 40) * RNG.uniform(0.1, 0.3), RNG.uniform(0, dur))
    write("amb_w1_water", loop_seamless(base * 0.6 + burble), 0.5)
    # W2 dry needles and restrained crackle
    air = lowpass_fast(noise(dur + 1.5), 4000) - lowpass_fast(noise(dur + 1.5), 1500)
    crack = np.zeros(len(air))
    for _ in range(260):
        place(crack, highpass_fast(noise(0.008), 2500) * RNG.uniform(0.1, 0.6), RNG.uniform(0, dur))
    write("amb_w2_forest", loop_seamless(air * 0.25 + crack * 0.6), 0.45)
    # W3 wind and soft snow
    n = noise(dur + 1.5); tt = t(dur + 1.5)
    gust = 0.5 + 0.5 * np.sin(2 * np.pi * 0.07 * tt + 1) * np.sin(2 * np.pi * 0.11 * tt)
    wind = (lowpass_fast(n, 900) - lowpass_fast(n, 200)) * (0.4 + 0.6 * gust)
    write("amb_w3_wind", loop_seamless(wind), 0.45)
    # W4 water under piers: slow wave swells with lapping
    n = noise(dur + 1.5)
    swell = 0.5 + 0.5 * np.sin(2 * np.pi * tt / 5.0)
    lap = np.zeros(len(n))
    for _ in range(60): place(lap, lowpass_fast(noise(0.25), 800) * np.exp(-t(0.25) * 12) * RNG.uniform(0.2, 0.5), RNG.uniform(0, dur))
    write("amb_w4_harbour", loop_seamless(lowpass_fast(n, 700) * swell * 0.7 + lap), 0.45)
    # W5 low geothermal rumble with occasional bubbles
    n = noise(dur + 1.5)
    rum = lowpass_fast(n, 120) * 3 + 0.3 * np.sin(2 * np.pi * 38 * tt) * (0.6 + 0.4 * np.sin(2 * np.pi * 0.05 * tt))
    for _ in range(40):
        f = RNG.uniform(120, 300); d = 0.12
        place(rum, sine(f, d) * np.exp(-t(d) * 25) * 0.3, RNG.uniform(0, dur))
    write("amb_w5_rumble", loop_seamless(rum), 0.5)


# ---------------------------------------------------------------- music (calm / problem / resolve per world)
SCALES = {
    "w1": (["D3", "A3"], ["D4", "E4", "F#4", "A4", "B4", "D5", "E5", "F#5"]),          # D major pentatonic
    "w2": (["A2", "E3"], ["A3", "C4", "D4", "E4", "G4", "A4", "C5", "D5"]),            # A minor pentatonic
    "w3": (["E3", "B3"], ["E4", "F#4", "G#4", "B4", "C#5", "E5", "F#5", "B5"]),        # E (lydian-ish colour)
    "w4": (["F3", "C4"], ["F4", "G4", "A4", "C5", "D5", "F5", "G5", "A5"]),            # F major pentatonic
    "w5": (["C3", "G3"], ["C4", "D#4", "F4", "G4", "A#4", "C5", "D#5", "G5"]),         # C minor pentatonic
}
CHORDS = {  # four two-bar chord roots per loop (scale degrees of the melody list)
    "w1": [0, 3, 4, 2], "w2": [0, 4, 3, 1], "w3": [0, 3, 2, 4], "w4": [0, 3, 4, 2], "w5": [0, 3, 2, 4],
}

def instrument(world, f, dur):
    if world == "w1": return pluck(f, dur, 0.6)
    if world == "w2": return mallet(f, dur, 0.25)
    if world == "w3": return bell(f, dur, 2.0, 0.9, 1.6)
    if world == "w4": return mallet(f, dur, 0.5)
    return bell(f, dur, 2.76, 1.4, 1.2) * 0.8   # inharmonic metallic tone

def music(world):
    roots, mel = SCALES[world]
    n = int(LOOP * SR) + int(1.5 * SR)
    calm = np.zeros(n); problem = np.zeros(n)
    rng = np.random.default_rng(hash(world) % 2**32)
    # calm: a pad per two bars plus a sparse melody on the beat grid
    for k, deg in enumerate(CHORDS[world]):
        start = k * 8 * BEAT
        chord = [note(roots[0]) * 2 ** (deg / 12.0 * 0)]  # root drone follows the bass note below
        f_root = note(mel[deg]) / 2
        p = pad([f_root, f_root * 1.5, f_root * 2.0], 8 * BEAT + 1.2, 900 if world != "w5" else 500) * 0.35
        place(calm, p, start)
        for b in range(8):
            if rng.random() < (0.55 if world != "w5" else 0.3):
                f = note(mel[(deg + rng.integers(0, 5)) % len(mel)])
                place(calm, instrument(world, f, 2.0) * rng.uniform(0.35, 0.6), start + b * BEAT + (0.5 * BEAT if rng.random() < 0.25 else 0))
    # problem: an urgent-but-restrained pulse layer (eighth notes and a soft drum on 1 and 3)
    for b in range(BARS * 4):
        at = b * BEAT
        deg = CHORDS[world][(b // 8) % 4]
        f = note(mel[deg]) / 2
        place(problem, instrument(world, f, 0.6) * 0.35, at)
        place(problem, instrument(world, f * 1.5, 0.4) * 0.2, at + BEAT / 2)
        if b % 2 == 0: place(problem, drum(0.5, 80 if world != "w3" else 120, 0.6) * 0.5, at)
    write(f"music_{world}_calm", loop_seamless(calm), 0.6)
    write(f"music_{world}_problem", loop_seamless(problem), 0.6)
    # resolve: a short cadence sting in the world's voice
    res = np.zeros(int(4.0 * SR))
    for i, deg in enumerate([0, 2, 4, 7]):
        place(res, instrument(world, note(mel[deg % len(mel)]), 2.5) * 0.6, i * BEAT / 2)
    place(res, pad([note(mel[0]) / 2, note(mel[2]) / 2, note(mel[4]) / 2], 3.5, 900) * 0.4, 0.2)
    write(f"music_{world}_resolve", fade(res), 0.7)


if __name__ == "__main__":
    what = sys.argv[1] if len(sys.argv) > 1 else "all"
    if what in ("all", "sfx"): sfx()
    if what in ("all", "amb"): ambiences()
    if what in ("all", "music"):
        print("music")
        for w in ("w1", "w2", "w3", "w4", "w5"): music(w)
