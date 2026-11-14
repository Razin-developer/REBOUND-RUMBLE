#!/usr/bin/env python3
"""REBOUND RUMBLE - naval sound effects + battle music, synthesised with numpy (no samples needed).
usage: python3 gen_naval_audio.py <Assets/Resources/Audio dir>"""
import os, sys, wave, math
import numpy as np

OUT = sys.argv[1] if len(sys.argv) > 1 else "."
os.makedirs(OUT, exist_ok=True)
SR = 22050
rng = np.random.RandomState(7)


def T(sec):
    return np.arange(int(sec * SR)) / float(SR)


def lowpass(x, cutoff):
    a = 1 - math.exp(-2 * math.pi * cutoff / SR)
    y = np.zeros_like(x)
    acc = 0.0
    for i in range(len(x)):
        acc += a * (x[i] - acc)
        y[i] = acc
    return y


def highpass(x, cutoff):
    return x - lowpass(x, cutoff)


def save(name, x, gain=0.9):
    x = np.asarray(x, dtype=np.float64)
    peak = max(1e-6, float(np.max(np.abs(x))))
    x = x / peak * gain
    pcm = (x * 32767).astype(np.int16)
    with wave.open(os.path.join(OUT, name + ".wav"), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes(pcm.tobytes())


def noise(n):
    return rng.uniform(-1, 1, n)


# ------------------------------------------------------------------ effects
t = T(0.75)
sweep = 38 + 110 * np.exp(-t * 12)
phase = 2 * np.pi * np.cumsum(sweep) / SR
cannon = np.sin(phase) * np.exp(-t * 5.5) * 1.0
cannon += lowpass(noise(len(t)), 1800) * np.exp(-t * 9) * 0.9
cannon += lowpass(noise(len(t)), 260) * np.exp(-t * 3.2) * 0.7
cannon[:60] *= np.linspace(0, 1, 60)
save("naval_cannon", cannon)

t = T(1.5)
sweep = 30 + 70 * np.exp(-t * 6)
phase = 2 * np.pi * np.cumsum(sweep) / SR
boom = np.sin(phase) * np.exp(-t * 2.4)
boom += lowpass(noise(len(t)), 1400) * np.exp(-t * 3.6) * 1.1
boom += lowpass(noise(len(t)), 200) * np.exp(-t * 1.8) * 0.9
boom += highpass(noise(len(t)), 3500) * np.exp(-t * 14) * 0.5
boom[:50] *= np.linspace(0, 1, 50)
save("naval_boom", boom)

t = T(0.7)
sp = highpass(noise(len(t)), 900) * np.exp(-t * 5.5)
for k in range(9):
    c = int(rng.uniform(0.04, 0.5) * SR)
    L = int(0.06 * SR)
    if c + L < len(t):
        pop = np.sin(2 * np.pi * rng.uniform(500, 1400) * np.arange(L) / SR * (1 + np.linspace(0, 1.4, L))) * np.exp(-np.arange(L) / (0.012 * SR))
        sp[c:c + L] += pop * 0.5
sp = lowpass(sp, 7000)
sp[:40] *= np.linspace(0, 1, 40)
save("naval_splash", sp)

t = T(0.28)
crack = highpass(noise(len(t)), 600) * np.exp(-t * 28)
crack += np.sin(2 * np.pi * 330 * t * (1 - 0.4 * t)) * np.exp(-t * 22) * 0.6
crack += lowpass(noise(len(t)), 500) * np.exp(-t * 14) * 0.8
save("naval_crack", crack)

t = T(1.3)
f = 98.0
horn = np.zeros_like(t)
for mult, a in ((1, 1.0), (1.5, 0.7), (2, 0.4), (3, 0.2)):
    horn += a * (2 * ((t * f * mult) % 1.0) - 1)
horn = lowpass(horn, 900)
env = np.minimum(1, t / 0.12) * np.minimum(1, (t[-1] - t) / 0.45)
save("naval_horn", horn * env)

t = T(1.8)
bell = np.zeros_like(t)
for fr, a, d in ((660, 1.0, 3.2), (990, 0.6, 3.6), (1320.0 * 1.01, 0.45, 4.2), (1760 * 1.02, 0.25, 5.0), (2640, 0.15, 6.0)):
    bell += a * np.sin(2 * np.pi * fr * t) * np.exp(-t * d)
bell[:30] *= np.linspace(0, 1, 30)
save("naval_bell", bell)

t = T(0.5)
whoosh = lowpass(noise(len(t)), 2600) * np.sin(np.pi * t / t[-1]) ** 1.5
whoosh = whoosh * (0.5 + 0.5 * np.sin(2 * np.pi * 9 * t))
save("naval_whoosh", whoosh, 0.7)

t = T(1.0)
sweep = 110 + 640 * (t / t[-1]) ** 2
phase = 2 * np.pi * np.cumsum(sweep) / SR
charge = np.sin(phase) * (t / t[-1]) ** 1.5 + 0.3 * np.sin(phase * 2.01) * (t / t[-1])
charge += lowpass(noise(len(t)), 900) * 0.25 * (t / t[-1])
save("naval_charge", charge, 0.75)


# ------------------------------------------------------------------ music: a stormy 6/8 shanty-ish loop in A minor
BPM = 116
E8 = 60.0 / BPM / 2.0           # one eighth note
BAR = E8 * 6
BARS = 16
total = BAR * BARS
n = int(total * SR)
mix = np.zeros(n)


def mtof(m):
    return 440.0 * 2 ** ((m - 69) / 12.0)


def tone(freq, dur, kind="tri", vol=0.3, duty=0.3, attack=0.01, release=0.08):
    L = int((dur + release) * SR)
    tt = np.arange(L) / SR
    ph = (tt * freq) % 1.0
    if kind == "tri":
        w = 2 * np.abs(2 * ph - 1) - 1
    elif kind == "pulse":
        w = np.where(ph < duty, 1.0, -1.0)
        w = lowpass(w, 2600)
    elif kind == "saw":
        w = lowpass(2 * ph - 1, 1800)
    else:
        w = np.sin(2 * np.pi * freq * tt)
    env = np.minimum(1, tt / attack) * np.where(tt < dur, 1.0, np.exp(-(tt - dur) / (release / 4.0)))
    return w * env * vol


def place(x0, snd):
    i = int(x0 * SR)
    j = min(n, i + len(snd))
    if i < n:
        mix[i:j] += snd[:j - i]


roots = [45, 45, 43, 43, 41, 41, 40, 40]          # Am Am G G F F E E
chords = [[57, 60, 64], [57, 60, 64], [55, 59, 62], [55, 59, 62], [53, 57, 60], [53, 57, 60], [52, 56, 59], [52, 56, 59]]
lead = [
    [(69, 2), (72, 1), (76, 3)], [(74, 2), (72, 1), (69, 3)], [(67, 2), (71, 1), (74, 3)], [(72, 2), (71, 1), (67, 3)],
    [(65, 2), (69, 1), (72, 3)], [(77, 2), (76, 1), (72, 3)], [(71, 2), (68, 1), (71, 1), (74, 1), (76, 1)], [(69, 6)],
]
lead2 = [
    [(81, 2), (79, 1), (76, 3)], [(77, 2), (76, 1), (74, 3)], [(74, 2), (76, 1), (79, 3)], [(76, 2), (74, 1), (71, 3)],
    [(72, 2), (74, 1), (77, 3)], [(81, 2), (79, 1), (77, 1), (76, 1), (74, 1)], [(76, 2), (74, 1), (71, 1), (68, 1), (71, 1)], [(69, 5), (0, 1)],
]
for b in range(BARS):
    x0 = b * BAR
    r = roots[b % 8]
    ch = chords[b % 8]
    for e, rr in ((0, r), (3, r + 7)):
        place(x0 + e * E8, tone(mtof(rr), E8 * 2.6, "tri", 0.34))
    for e in (1, 2, 4, 5):                                     # off-beat stabs
        for note in ch:
            place(x0 + e * E8, tone(mtof(note), E8 * 0.55, "pulse", 0.05, 0.4, 0.004, 0.05))
    # drums
    k = np.sin(2 * np.pi * (46 + 90 * np.exp(-np.arange(int(0.25 * SR)) / (0.03 * SR))) * np.arange(int(0.25 * SR)) / SR) * np.exp(-np.arange(int(0.25 * SR)) / (0.07 * SR))
    place(x0, k * 0.8)
    place(x0 + 3 * E8, k * 0.55)
    for e in (3,):
        sn = highpass(noise(int(0.2 * SR)), 1200) * np.exp(-np.arange(int(0.2 * SR)) / (0.04 * SR)) * 0.55
        place(x0 + e * E8, sn)
    for e in range(6):
        h = highpass(noise(int(0.05 * SR)), 5000) * np.exp(-np.arange(int(0.05 * SR)) / (0.012 * SR)) * 0.14
        place(x0 + e * E8, h)
    seq = (lead if b < 8 else lead2)[b % 8]
    e = 0
    for (note, d) in seq:
        if note > 0:
            place(x0 + e * E8, tone(mtof(note), d * E8 * 0.92, "pulse", 0.17, 0.3, 0.012, 0.09))
        e += d

mix = lowpass(mix, 7000)
# a short fade so the loop point is clean
fade = int(0.01 * SR)
mix[:fade] *= np.linspace(0, 1, fade)
mix[-fade:] *= np.linspace(1, 0, fade)
save("naval_music", mix, 0.8)
print("naval audio ->", OUT)
