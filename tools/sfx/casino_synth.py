"""Synthesised stand-in sounds of the casino (1.1.0, F30/F31).

Every clip listed in docs/v110/sfx_casino.json is built here from plain DSP (clicks, metal and clay pings, bells, filtered noise, formant
voices), so the casino is never silent while the generated audio (prompts in the same JSON) is still missing. A generated clip simply
replaces the file at the same path; nothing else changes. Deterministic: every file has its own seed.

    uv run --with numpy --with scipy python3 tools/sfx/casino_synth.py            # all clips
    uv run --with numpy --with scipy python3 tools/sfx/casino_synth.py Chip Groan # only files whose name contains one of the words

Output: mono 44.1 kHz Ogg Vorbis (ffmpeg libvorbis, q5) at Common/Sounds/HH/Casino/{UI,World}/<Name>.ogg. MIT, our own work.
"""
import json
import os
import subprocess
import sys
import tempfile
import zlib

import numpy as np
from scipy import signal
from scipy.io import wavfile

SR = 44100
ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))


# ---------------------------------------------------------------------------------------------------------------- building blocks
def n_of(sec):
    return max(1, int(round(sec * SR)))


def buf(sec):
    return np.zeros(n_of(sec))


def tt(sec):
    return np.arange(n_of(sec)) / SR


def put(dst, src, at, gain=1.0):
    i = int(round(at * SR))
    if i >= len(dst):
        return dst
    j = min(len(dst), i + len(src))
    dst[i:j] += src[: j - i] * gain
    return dst


def sos_bp(lo, hi, order=2):
    hi = min(hi, SR / 2 * 0.95)
    return signal.butter(order, [lo, hi], btype="band", fs=SR, output="sos")


def bp(x, lo, hi, order=2):
    return signal.sosfilt(sos_bp(lo, hi, order), x)


def lp(x, f, order=2):
    return signal.sosfilt(signal.butter(order, min(f, SR / 2 * 0.95), btype="low", fs=SR, output="sos"), x)


def hp(x, f, order=2):
    return signal.sosfilt(signal.butter(order, f, btype="high", fs=SR, output="sos"), x)


def env_exp(sec, tau, attack=0.002):
    t = tt(sec)
    e = np.exp(-t / max(tau, 1e-4))
    a = np.clip(t / max(attack, 1e-5), 0, 1)
    return e * a


def noise(rng, sec):
    return rng.standard_normal(n_of(sec))


def click(rng, lo=1500, hi=7000, tau=0.006, sec=0.04):
    return bp(noise(rng, sec), lo, hi) * env_exp(sec, tau, 0.0004)


def ping(freqs, amps, taus, sec, attack=0.0015, rng=None, detune=0.0):
    t = tt(sec)
    out = np.zeros_like(t)
    for f, a, tau in zip(freqs, amps, taus):
        if rng is not None and detune:
            f *= 1 + rng.uniform(-detune, detune)
        if f >= SR / 2 * 0.9:
            continue
        out += a * np.sin(2 * np.pi * f * t + (rng.uniform(0, 6.28) if rng is not None else 0)) * np.exp(-t / tau)
    return out * np.clip(t / attack, 0, 1)


def coin(rng, f=None, sec=0.35, level=1.0):
    """A silver coin hitting metal or another coin: bright inharmonic partials, short."""
    f = f or rng.uniform(3300, 5200)
    ratios = [1.0, 1.47, 2.09, 2.56, 3.18]
    amps = [1.0, 0.7, 0.55, 0.35, 0.2]
    taus = [0.11, 0.07, 0.05, 0.035, 0.025]
    s = ping([f * r for r in ratios], amps, [x * rng.uniform(0.7, 1.4) for x in taus], sec, rng=rng, detune=0.01)
    s += click(rng, 4000, 12000, 0.002, sec) * 0.6
    return s * level


def chip(rng, f=None, sec=0.09, level=1.0):
    """A clay casino chip on felt or on another chip: a dry click with a short woody ping."""
    f = f or rng.uniform(1900, 2900)
    s = ping([f, f * 1.71, f * 2.6], [1.0, 0.45, 0.2], [0.014, 0.009, 0.006], sec, rng=rng, detune=0.02)
    s += click(rng, 1200, 6000, 0.004, sec) * 1.3
    return s * level


def thump(rng, f=95, sec=0.25, tau=0.06, drop=0.35):
    t = tt(sec)
    ff = f * (1 + drop * np.exp(-t / 0.02))
    ph = 2 * np.pi * np.cumsum(ff) / SR
    s = np.sin(ph) * np.exp(-t / tau)
    s += lp(noise(rng, sec), 900) * np.exp(-t / 0.012) * 0.5
    return s * np.clip(t / 0.001, 0, 1)


def bell(rng, f0, sec=1.2, tau=0.5, level=1.0):
    """A small brass bell: inharmonic partials, the high ones die first."""
    ratios = [1.0, 2.0, 2.76, 4.07, 5.4, 6.8]
    amps = [1.0, 0.55, 0.5, 0.25, 0.18, 0.08]
    taus = [tau, tau * 0.7, tau * 0.55, tau * 0.35, tau * 0.25, tau * 0.18]
    s = ping([f0 * r for r in ratios], amps, taus, sec, attack=0.001, rng=rng, detune=0.002)
    s += click(rng, 3000, 9000, 0.002, sec) * 0.25
    return s * level


def marimba(rng, f, sec=0.6, tau=0.18):
    s = ping([f, f * 3.99, f * 9.2], [1.0, 0.28, 0.06], [tau, tau * 0.25, tau * 0.1], sec, attack=0.002, rng=rng)
    s += lp(noise(rng, sec), 3000) * env_exp(sec, 0.004, 0.0005) * 0.25
    return s


def slap(rng, sec=0.08, lo=700, hi=6000, tau=0.012):
    """A card landing on felt."""
    s = bp(noise(rng, sec), lo, hi) * env_exp(sec, tau, 0.0008)
    s += lp(noise(rng, sec), 500) * env_exp(sec, 0.01, 0.001) * 0.7
    return s


def swish(rng, sec=0.16, f0=900, f1=4500):
    """A card sliding through the air: noise with a rising band."""
    n = n_of(sec)
    x = noise(rng, sec)
    out = np.zeros(n)
    seg = 256
    for i in range(0, n, seg):
        u = i / n
        fc = f0 + (f1 - f0) * u
        y = bp(x[max(0, i - 512): i + seg], fc * 0.6, fc * 1.6)
        out[i: i + seg] = y[-min(seg, n - i):]
    e = np.sin(np.pi * np.clip(np.arange(n) / n, 0, 1)) ** 1.5
    return out * e


def saw(f_contour):
    ph = np.cumsum(f_contour) / SR
    return 2 * (ph - np.floor(ph)) - 1


def formant(x, formants):
    out = np.zeros_like(x)
    for f, bw, g in formants:
        b, a = signal.iirpeak(f, f / bw, fs=SR)
        out += signal.lfilter(b, a, x) * g
    return out


VOWELS = {
    "a": [(800, 90, 1.0), (1200, 110, 0.6), (2500, 160, 0.25)],
    "o": [(500, 80, 1.0), (850, 100, 0.55), (2400, 160, 0.15)],
    "u": [(330, 70, 1.0), (800, 100, 0.35), (2300, 160, 0.1)],
    "e": [(530, 80, 1.0), (1850, 120, 0.5), (2500, 160, 0.25)],
    "i": [(300, 70, 1.0), (2200, 130, 0.45), (3000, 170, 0.25)],
    "ae": [(700, 90, 1.0), (1700, 120, 0.5), (2500, 160, 0.25)],
}


def voice(rng, sec, f_start, f_end, vowel, breath=0.15, vib=5.5, attack=0.06, release=0.25, shape=None):
    """One wordless human voice (a vowel), pitch gliding from f_start to f_end."""
    t = tt(sec)
    u = t / max(t[-1], 1e-6)
    f = f_start + (f_end - f_start) * (u if shape is None else shape(u))
    f = f * (1 + 0.012 * np.sin(2 * np.pi * vib * t + rng.uniform(0, 6.28))) * (1 + 0.004 * lp(rng.standard_normal(len(t)), 8) * 30)
    src = lp(saw(f), 4200) + bp(rng.standard_normal(len(t)), 500, 5000) * breath
    s = formant(src, VOWELS[vowel])
    env = np.clip(t / attack, 0, 1) * np.clip((t[-1] - t) / release, 0, 1)
    return s * env


def crowd(rng, sec, n, vowel, f_lo, f_hi, glide, spread=0.18, level=1.0):
    out = buf(sec)
    for _ in range(n):
        f0 = rng.uniform(f_lo, f_hi)
        d = sec * rng.uniform(0.7, 0.95)
        v = voice(rng, d, f0, f0 * glide * rng.uniform(0.95, 1.05), vowel, breath=0.25)
        put(out, v / (np.abs(v).max() + 1e-9), rng.uniform(0, spread), rng.uniform(0.5, 1.0))
    return out * level


def claps(rng, sec, n, start=0.05):
    out = buf(sec)
    for _ in range(n):
        c = bp(noise(rng, 0.05), 900, 5000) * env_exp(0.05, 0.008, 0.0005)
        put(out, c, rng.uniform(start, sec * 0.85), rng.uniform(0.3, 0.8))
    return out


def ratchet(rng, sec, n, accel=True, lo=1800, hi=8000, level=1.0):
    out = buf(sec)
    times = np.linspace(0, 1, n) ** (0.7 if accel else 1.4) * sec * 0.95
    for k, at in enumerate(times):
        put(out, click(rng, lo, hi, 0.003, 0.03), at, level * rng.uniform(0.7, 1.0))
    return out


def reel_whirr(rng, sec, rate0=28, rate1=28, level=1.0):
    """Spinning reels: a fast rattle of the reel teeth on the pawl plus a soft motor-like hum."""
    out = buf(sec)
    at = 0.0
    while at < sec:
        u = at / sec
        rate = rate0 + (rate1 - rate0) * u
        put(out, click(rng, 1200, 6000, 0.0025, 0.02), at, rng.uniform(0.35, 0.6))
        at += 1.0 / rate * rng.uniform(0.85, 1.15)
    t = tt(sec)
    hum = bp(noise(rng, sec), 150, 900) * (0.6 + 0.4 * np.sin(2 * np.pi * 9 * t))
    out += hum * 0.25
    return out * level


def coin_cascade(rng, sec, density0, density1, level=1.0, start=0.0):
    out = buf(sec)
    at = start
    while at < sec - 0.05:
        u = (at - start) / max(1e-6, sec - start)
        dens = density0 + (density1 - density0) * u
        put(out, coin(rng, sec=0.3), at, rng.uniform(0.25, 0.7) * (1 - 0.6 * u))
        at += rng.exponential(1.0 / max(dens, 1))
    # the pile in the tray: a soft noisy bed
    bed = bp(noise(rng, sec), 2500, 9000) * 0.06
    e = np.clip((tt(sec) - start) / 0.1, 0, 1) * np.clip((sec - tt(sec)) / 0.6, 0, 1)
    return (out + bed * e) * level


def fade_tail(x, sec=0.03):
    n = min(len(x), n_of(sec))
    x[-n:] *= np.linspace(1, 0, n)
    return x


# ---------------------------------------------------------------------------------------------------------------- the clips
def s_chip(rng):
    x = buf(0.3)
    put(x, chip(rng), 0.0, 1.0)
    put(x, chip(rng, level=0.5), 0.028, 1.0)
    return x


def s_slot_lever(rng):
    x = buf(0.9)
    put(x, ratchet(rng, 0.38, 9, True), 0.02, 0.6)
    put(x, thump(rng, 85, 0.3, 0.07), 0.42, 1.0)
    put(x, click(rng, 1500, 6000, 0.01, 0.08), 0.42, 0.6)
    # the spring
    t = tt(0.45)
    spring = np.sin(2 * np.pi * (260 + 40 * np.sin(2 * np.pi * 11 * t)) * t) * np.exp(-t / 0.12) * 0.25
    put(x, spring, 0.44)
    return x


def s_slot_spin(rng):
    x = reel_whirr(rng, 1.8, 30, 34)
    t = tt(1.8)
    x *= np.clip(t / 0.08, 0, 1)
    return fade_tail(x, 0.02)


def s_reel_stop(rng):
    x = buf(0.35)
    put(x, thump(rng, 120, 0.25, 0.05), 0.0, 1.0)
    put(x, click(rng, 1500, 6000, 0.008, 0.06), 0.0, 0.8)
    put(x, ratchet(rng, 0.08, 3, False, level=0.4), 0.03)
    return x


def s_near_miss(rng):
    x = buf(0.9)
    at = 0.0
    gap = 0.07
    for k in range(7):
        put(x, click(rng, 1500, 6000, 0.004, 0.03), at, 0.9)
        put(x, ping([1800 - k * 60], [0.25], [0.03], 0.08), at)
        at += gap
        gap *= 1.33
    return x


def s_win_small(rng):
    x = buf(1.2)
    put(x, bell(rng, 1318, 1.1, 0.45), 0.0, 1.0)
    for k in range(5):
        put(x, coin(rng, sec=0.3), 0.32 + k * rng.uniform(0.07, 0.13), 0.45)
    return x


def s_win_big(rng):
    x = buf(2.6)
    for k, f in enumerate((1046, 1318, 1568, 2093)):
        put(x, bell(rng, f, 1.4, 0.5), k * 0.11, 0.7)
    put(x, bell(rng, 2093, 1.5, 0.6), 0.6, 0.6)
    put(x, coin_cascade(rng, 2.3, 40, 8), 0.3, 1.1)
    return x


def s_jackpot(rng):
    x = buf(3.5)
    for k in range(14):
        put(x, bell(rng, 1568 if k % 2 == 0 else 1975, 0.5, 0.2), k * 0.075, 0.55)
    # a deep friendly horn call, like a large gentle beast
    t = tt(1.1)
    f = 98 * (1 + 0.25 * np.clip(t / 0.25, 0, 1)) * (1 - 0.08 * np.clip((t - 0.7) / 0.4, 0, 1))
    f = f * (1 + 0.01 * np.sin(2 * np.pi * 5 * t))
    horn = formant(lp(saw(f), 2500), [(420, 120, 1.0), (900, 160, 0.5), (1800, 220, 0.15)])
    horn *= np.clip(t / 0.12, 0, 1) * np.clip((1.1 - t) / 0.3, 0, 1)
    put(x, horn / (np.abs(horn).max() + 1e-9), 0.95, 0.75)
    put(x, coin_cascade(rng, 2.1, 70, 12), 1.35, 1.2)
    return x


def roulette_roll(rng, sec, level=1.0):
    """The ball running around the rim: a rolling noise pulsed by each lap, slowing down; the wheel hums under it."""
    t = tt(sec)
    u = t / sec
    lap = 6.5 * (1 - 0.65 * u)  # laps per second
    ph = np.cumsum(lap) / SR
    roll = bp(noise(rng, sec), 1800, 7000) * (0.55 + 0.45 * np.sin(2 * np.pi * ph) ** 2)
    roll += bp(noise(rng, sec), 300, 1200) * 0.35
    # tiny bumps on the diamonds
    out = roll * (1 - 0.5 * u)
    at = 0.0
    while at < sec:
        if rng.random() < 0.6:
            put(out, click(rng, 2000, 7000, 0.002, 0.02), at, 0.5 * (1 - 0.4 * at / sec))
        at += 1.0 / (lap[min(len(lap) - 1, int(at * SR))] * 4)
    out *= np.clip(t / 0.15, 0, 1) * np.clip((sec - t) / 0.35, 0, 1)
    return out * level


def s_roulette_spin(rng):
    return roulette_roll(rng, 4.0)


def s_roulette_drop(rng):
    x = buf(1.0)
    at, gap, a = 0.0, 0.11, 1.0
    for k in range(8):
        put(x, click(rng, 1500, 8000, 0.004, 0.04), at, a)
        put(x, ping([rng.uniform(2600, 4200)], [0.35 * a], [0.025], 0.08, rng=rng), at)
        at += gap
        gap *= 0.78
        a *= 0.8
    put(x, bp(noise(rng, 0.25), 1500, 5000) * env_exp(0.25, 0.06), at, 0.25)
    return x


def s_card_deal(rng):
    x = buf(0.3)
    put(x, swish(rng, 0.14), 0.0, 0.5)
    put(x, slap(rng), 0.13, 1.0)
    return x


def s_card_flip(rng):
    x = buf(0.3)
    put(x, hp(noise(rng, 0.05), 2500) * env_exp(0.05, 0.006, 0.0004), 0.0, 0.8)
    put(x, slap(rng, tau=0.008), 0.07, 0.8)
    return x


def riffle(rng, sec, rate=55, level=1.0):
    out = buf(sec)
    at = 0.0
    while at < sec * 0.75:
        put(out, click(rng, 2000, 8000, 0.0015, 0.015), at, rng.uniform(0.3, 0.7))
        at += 1.0 / rate * rng.uniform(0.6, 1.4)
    bridge = bp(noise(rng, sec * 0.3), 600, 4000) * np.hanning(n_of(sec * 0.3))
    put(out, bridge, sec * 0.7, 0.35)
    return out * level


def s_card_shuffle(rng):
    x = buf(1.2)
    put(x, riffle(rng, 1.1), 0.02)
    return x


def s_chips_place(rng):
    x = buf(0.5)
    at = 0.0
    for k in range(4):
        put(x, chip(rng), at, 1.0 - k * 0.12)
        at += rng.uniform(0.04, 0.08)
    return x


def s_chips_push(rng):
    x = buf(0.9)
    slide = bp(noise(rng, 0.6), 400, 2500) * np.hanning(n_of(0.6)) * 0.35
    put(x, slide, 0.0)
    for k in range(7):
        put(x, chip(rng), 0.08 + k * rng.uniform(0.05, 0.1), rng.uniform(0.4, 0.8))
    return x


def s_win(rng):
    x = buf(1.0)
    for k, f in enumerate((784, 988, 1175, 1568)):
        put(x, marimba(rng, f, 0.6), k * 0.09, 0.8)
    put(x, coin(rng), 0.4, 0.4)
    return x


def s_lose(rng):
    x = buf(0.8)
    put(x, marimba(rng, 392, 0.6, 0.2), 0.0, 0.8)
    put(x, marimba(rng, 311, 0.7, 0.25), 0.16, 0.8)
    return x


def s_bust(rng):
    x = buf(0.7)
    put(x, thump(rng, 70, 0.5, 0.1, drop=0.6), 0.0, 1.0)
    put(x, marimba(rng, 233, 0.5, 0.18), 0.05, 0.5)
    put(x, marimba(rng, 196, 0.5, 0.2), 0.2, 0.45)
    return x


# map sounds (3D, softer, shorter)
def w_slot_play(rng):
    x = buf(1.4)
    put(x, ratchet(rng, 0.25, 6, True), 0.0, 0.5)
    put(x, thump(rng, 90, 0.2, 0.05), 0.27, 0.7)
    w = reel_whirr(rng, 1.0, 30, 22, 0.7)
    w *= np.clip(tt(1.0) / 0.05, 0, 1)
    put(x, fade_tail(w, 0.05), 0.32)
    return x


def w_slot_stop(rng):
    x = buf(0.6)
    for k in range(3):
        put(x, thump(rng, 110 + k * 12, 0.15, 0.04), k * 0.16, 0.9)
        put(x, click(rng, 1500, 6000, 0.006, 0.05), k * 0.16, 0.6)
    return x


def w_roulette_roll(rng):
    x = roulette_roll(rng, 2.5)
    out = buf(3.0)
    put(out, x, 0.0)
    put(out, s_roulette_drop(rng)[: n_of(0.55)], 2.4, 0.7)
    return out


def w_card_snap(rng):
    x = buf(0.6)
    put(x, slap(rng), 0.0, 1.0)
    put(x, slap(rng), 0.18, 0.8)
    put(x, chip(rng), 0.38, 0.5)
    return x


def w_shuffle(rng):
    return riffle(rng, 1.0)


def w_win_bell(rng):
    x = buf(1.0)
    put(x, bell(rng, 1568, 0.95, 0.35), 0.0)
    put(x, bell(rng, 1568, 0.6, 0.25), 0.18, 0.6)
    return x


def w_win_big(rng):
    x = buf(2.5)
    for k in range(6):
        put(x, bell(rng, 1568 if k % 2 == 0 else 1975, 0.6, 0.25), k * 0.09, 0.6)
    put(x, coin_cascade(rng, 2.0, 35, 6), 0.4, 1.0)
    return x


def w_cheer(rng):
    sec = 1.2
    x = crowd(rng, sec, 7, "a", 140, 300, 1.25, 0.12)
    x += crowd(rng, sec, 3, "ae", 200, 320, 1.15, 0.2) * 0.6
    x = x / (np.abs(x).max() + 1e-9)
    x += claps(rng, sec, 22, 0.15) * 0.8
    return x


def w_groan(rng):
    sec = 0.9
    x = crowd(rng, sec, 4, "o", 110, 210, 0.72, 0.08)
    x += crowd(rng, sec, 2, "u", 120, 180, 0.75, 0.1) * 0.6
    return x


def w_cash_out(rng):
    x = buf(1.0)
    for k in range(9):
        put(x, coin(rng, sec=0.3), k * rng.uniform(0.04, 0.07), rng.uniform(0.4, 0.8))
    # into a leather pouch: a soft muffled bump
    put(x, lp(noise(rng, 0.12), 700) * env_exp(0.12, 0.03), 0.55, 0.9)
    put(x, bell(rng, 2349, 0.4, 0.15), 0.62, 0.35)
    return x


def w_ambience(rng):
    """A seamless 20 s loop: a wordless murmur of a room of players, chips, a distant slot bell now and then."""
    sec, xf = 20.0, 2.0
    total = sec + xf
    x = buf(total)
    vowels = list(VOWELS.keys())
    # babble: many voices, each a chain of short syllables
    for v in range(14):
        at = rng.uniform(0, 1.5)
        f0 = rng.uniform(95, 230)
        while at < total:
            d = rng.uniform(0.12, 0.32)
            syl = voice(rng, d, f0 * rng.uniform(0.92, 1.1), f0 * rng.uniform(0.85, 1.08), vowels[rng.integers(len(vowels))], breath=0.3, attack=0.03, release=0.06)
            put(x, syl / (np.abs(syl).max() + 1e-9), at, rng.uniform(0.25, 0.6))
            at += d + (rng.uniform(0.6, 2.4) if rng.random() < 0.18 else rng.uniform(0.0, 0.08))
    x = lp(x, 2200, 4)              # far enough away that no word is clear
    x = x / (np.abs(x).max() + 1e-9) * 0.55
    # chips here and there
    at = 0.3
    while at < total:
        put(x, chip(rng, level=0.25), at)
        if rng.random() < 0.4:
            put(x, chip(rng, level=0.18), at + rng.uniform(0.03, 0.07))
        at += rng.exponential(0.9)
    # a distant slot bell and a short reel rattle, rare
    for _ in range(3):
        at = rng.uniform(1, total - 2)
        put(x, lp(bell(rng, 1568, 0.9, 0.3), 3500), at, 0.12)
        put(x, lp(reel_whirr(rng, 0.8, 30, 24), 3000), at - 0.85, 0.08)
    # the loop: the last xf seconds fade into the first ones
    n, m = n_of(sec), n_of(xf)
    out = x[:n].copy()
    fade = np.linspace(0, 1, m)
    out[:m] = x[:m] * fade + x[n:n + m] * (1 - fade)
    return out


CLIPS = {
    "UI/Chip": s_chip, "UI/SlotLever": s_slot_lever, "UI/SlotSpin": s_slot_spin, "UI/SlotReelStop": s_reel_stop,
    "UI/SlotNearMiss": s_near_miss, "UI/SlotWinSmall": s_win_small, "UI/SlotWinBig": s_win_big, "UI/SlotJackpot": s_jackpot,
    "UI/RouletteSpin": s_roulette_spin, "UI/RouletteDrop": s_roulette_drop, "UI/CardDeal": s_card_deal, "UI/CardFlip": s_card_flip,
    "UI/CardShuffle": s_card_shuffle, "UI/ChipsPlace": s_chips_place, "UI/ChipsPush": s_chips_push, "UI/Win": s_win,
    "UI/Lose": s_lose, "UI/Bust": s_bust,
    "World/SlotPlay": w_slot_play, "World/SlotStop": w_slot_stop, "World/RouletteRoll": w_roulette_roll, "World/CardSnap": w_card_snap,
    "World/Shuffle": w_shuffle, "World/WinBell": w_win_bell, "World/WinBig": w_win_big, "World/Cheer": w_cheer, "World/Groan": w_groan,
    "World/CashOut": w_cash_out, "World/Ambience": w_ambience,
}


def finish(x, loop, peak_db=-1.0):
    x = np.asarray(x, dtype=np.float64)
    x = hp(x, 35)                  # no rumble below hearing
    if not loop:
        x = fade_tail(x, 0.01)
    p = np.abs(x).max()
    if p > 0:
        x = x / p * (10 ** (peak_db / 20))
    return x


def main(words):
    spec = json.load(open(os.path.join(ROOT, "docs", "v110", "sfx_casino.json")))
    by_file = {e["file"]: e for e in spec}
    done = 0
    for key, fn in CLIPS.items():
        rel = "Common/Sounds/HH/Casino/%s.ogg" % key
        if rel not in by_file:
            raise SystemExit("not in sfx_casino.json: " + rel)
        if words and not any(w.lower() in key.lower() for w in words):
            continue
        e = by_file[rel]
        rng = np.random.default_rng(zlib.crc32(key.encode()))
        x = finish(fn(rng), e.get("loop", False), -3.0 if e.get("loop") else -1.0)
        path = os.path.join(ROOT, rel)
        os.makedirs(os.path.dirname(path), exist_ok=True)
        with tempfile.TemporaryDirectory() as td:
            wav = os.path.join(td, "x.wav")
            wavfile.write(wav, SR, (np.clip(x, -1, 1) * 32767).astype(np.int16))
            subprocess.run(["ffmpeg", "-y", "-loglevel", "error", "-i", wav, "-ac", "1", "-c:a", "libvorbis", "-q:a", "5", path], check=True)
        print("%-24s %5.2f s  %s" % (key, len(x) / SR, rel))
        done += 1
    missing = [e["file"] for e in spec if e["file"].replace("Common/Sounds/HH/Casino/", "").replace(".ogg", "") not in CLIPS]
    if missing:
        raise SystemExit("no synth for: " + ", ".join(missing))
    print("%d clips written" % done)


if __name__ == "__main__":
    main(sys.argv[1:])
