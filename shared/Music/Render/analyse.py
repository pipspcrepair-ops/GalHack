"""Checks rendered drones by measurement, since nobody here can hear them.

  python analyse.py <dir of .wav> [<dir for .png>]

Prints peak, DC, silence, clipping, integrated loudness (BS.1770, ungated plus gate), crest,
spectral centroid and rolloff, band balance, spectral flatness, movement over time, stereo width
and the biggest short-term jump, then how far apart the moods are. Draws one waveform and
spectrogram image per file.
"""
import glob
import os
import sys
import wave

import numpy as np
from scipy import signal


def load(path):
    with wave.open(path, "rb") as w:
        rate, frames, channels = w.getframerate(), w.getnframes(), w.getnchannels()
        data = np.frombuffer(w.readframes(frames), dtype="<i2").astype(np.float64) / 32768.0
    return rate, data.reshape(-1, channels)


def lufs(rate, x):
    # K-weighting, then the two-stage gate of BS.1770-4 over 400 ms blocks.
    f0, g, q = 1681.97, 3.99984, 0.7071752
    k = np.tan(np.pi * f0 / rate)
    vh, vb = 10 ** (g / 20), 10 ** (g / 20 * 0.4996667)
    a0 = 1 + k / q + k * k
    shelf_b = [(vh + vb * k / q + k * k) / a0, 2 * (k * k - vh) / a0, (vh - vb * k / q + k * k) / a0]
    shelf_a = [1, 2 * (k * k - 1) / a0, (1 - k / q + k * k) / a0]
    f0, q = 38.13547, 0.5003270
    k = np.tan(np.pi * f0 / rate)
    a0 = 1 + k / q + k * k
    hp_b, hp_a = [1, -2, 1], [1, 2 * (k * k - 1) / a0, (1 - k / q + k * k) / a0]
    y = signal.lfilter(hp_b, hp_a, signal.lfilter(shelf_b, shelf_a, x, axis=0), axis=0)
    block, hop = int(0.4 * rate), int(0.1 * rate)
    e = np.array([np.sum(np.mean(y[i : i + block] ** 2, axis=0)) for i in range(0, len(y) - block, hop)])
    e = np.maximum(e, 1e-12)
    loud = -0.691 + 10 * np.log10(e)
    kept = e[loud > -70]
    gate = -0.691 + 10 * np.log10(np.mean(kept)) - 10
    return -0.691 + 10 * np.log10(np.mean(e[loud > gate]))


def measure(rate, x):
    m = x.mean(axis=1)
    body = x[int(10 * rate) : int(-5 * rate)]  # past the fade in, before the end fade
    bm = body.mean(axis=1)
    out = {}
    out["peak_db"] = 20 * np.log10(np.abs(x).max() + 1e-12)
    out["clipped"] = int((np.abs(x) >= 0.999).sum())
    out["dc"] = float(np.abs(x.mean(axis=0)).max())
    rms = np.sqrt(np.mean(body**2))
    out["rms_db"] = 20 * np.log10(rms + 1e-12)
    out["crest_db"] = out["peak_db"] - out["rms_db"]
    out["lufs"] = lufs(rate, x)

    f, t, s = signal.spectrogram(bm, rate, nperseg=8192, noverlap=4096, scaling="spectrum")
    p = s + 1e-16
    mean = p.mean(axis=1)
    cum = np.cumsum(mean) / mean.sum()
    out["centroid"] = float((f * mean).sum() / mean.sum())
    out["rolloff85"] = float(f[np.searchsorted(cum, 0.85)])
    bands = [(20, 60), (60, 200), (200, 800), (800, 3000), (3000, 12000)]
    total = mean.sum()
    out["bands_db"] = [10 * np.log10(mean[(f >= a) & (f < b)].sum() / total + 1e-12) for a, b in bands]
    keep = (f >= 40) & (f <= 8000)
    out["flatness"] = float(np.exp(np.mean(np.log(mean[keep]))) / mean[keep].mean())
    # Movement: how much the log spectrum changes across 10 s, against a static tone's zero.
    logp = 10 * np.log10(p[keep])
    frames = logp.shape[1]
    step = max(1, int(10 / (t[1] - t[0])))
    out["move_db"] = float(np.mean(np.abs(logp[:, step:] - logp[:, :-step])))
    # Peaks: how many separate lines stand above the spectrum's shape (a test tone has one or two).
    smooth = signal.medfilt(10 * np.log10(mean[f < 4000]), 101)
    peaks, _ = signal.find_peaks(10 * np.log10(mean[f < 4000]) - smooth, height=8)
    out["lines"] = len(peaks)

    # Short-term level: gentle dynamics, and no sudden change.
    win = int(0.2 * rate)
    env = np.sqrt(np.convolve(bm**2, np.ones(win) / win, mode="valid"))[::win]
    db = 20 * np.log10(env + 1e-9)
    out["jump_db"] = float(np.abs(np.diff(db)).max())
    out["swing_db"] = float(np.percentile(db, 95) - np.percentile(db, 5))
    # A click shows as one sample far outside its neighbours.
    d2 = np.abs(np.diff(x, 2, axis=0)).max(axis=1)
    out["click"] = float(d2[int(rate) :].max() / (np.percentile(d2, 99.9) + 1e-9))
    # Roughness: how much the mid band's envelope flutters at 2 to 15 Hz, the rate heard as unease
    # rather than as swell. Scale shape: energy folded to 12 pitch classes, read relative to its peak.
    sos = signal.butter(4, [200, 2000], "bandpass", fs=rate, output="sos")
    mid = signal.sosfiltfilt(sos, bm)[:: 4]
    e = np.abs(signal.hilbert(mid[: 2 ** 20]))
    fe, pe = signal.welch(e - e.mean(), rate / 4, nperseg=65536)
    out["rough_db"] = float(10 * np.log10(pe[(fe >= 2) & (fe <= 15)].sum() / (e.mean() ** 2) + 1e-12) + 30)
    fold = np.zeros(12)
    for fk, m in zip(f[(f > 55) & (f < 2000)], mean[(f > 55) & (f < 2000)]):
        fold[int(round(12 * np.log2(fk / 55.0))) % 12] += m ** 0.5
    fold = fold / np.linalg.norm(fold)
    out["chroma"] = fold
    corr = np.corrcoef(body[:, 0], body[:, 1])[0, 1]
    out["stereo_corr"] = float(corr)
    out["silent_s"] = float((env < 10 ** (-60 / 20)).sum() * 0.2)
    return out


def draw(name, rate, x, path):
    import matplotlib

    matplotlib.use("Agg")
    import matplotlib.pyplot as plt

    fig, (a, b) = plt.subplots(2, 1, figsize=(11, 6), gridspec_kw={"height_ratios": [1, 3]}, sharex=True)
    t = np.arange(len(x)) / rate
    stride = max(1, len(x) // 6000)
    a.plot(t[::stride], x[::stride, 0], lw=0.3, color="#335")
    a.plot(t[::stride], x[::stride, 1], lw=0.3, color="#a55")
    a.set_ylim(-0.5, 0.5)
    a.set_ylabel("level")
    a.set_title(name)
    f, tt, s = signal.spectrogram(x.mean(axis=1), rate, nperseg=8192, noverlap=6144, scaling="spectrum")
    b.pcolormesh(tt, f, 10 * np.log10(s + 1e-14), vmin=-100, vmax=-35, shading="auto", cmap="magma", rasterized=True)
    b.set_yscale("log")
    b.set_ylim(25, 16000)
    b.set_ylabel("Hz")
    b.set_xlabel("seconds")
    fig.tight_layout()
    fig.savefig(path, dpi=85)
    plt.close(fig)


def main():
    folder = sys.argv[1]
    images = sys.argv[2] if len(sys.argv) > 2 else None
    results = {}
    print(f"{'name':13}{'peak':>7}{'rms':>7}{'LUFS':>7}{'crest':>6}{'centr':>7}{'roll85':>7}{'flat':>6}{'move':>6}{'lines':>6}{'rough':>6}{'swing':>6}{'jump':>6}{'click':>6}{'corr':>6}{'dc':>9}  bands 20/60/200/800/3k")
    for path in sorted(glob.glob(os.path.join(folder, "*.wav"))):
        name = os.path.splitext(os.path.basename(path))[0]
        rate, x = load(path)
        r = measure(rate, x)
        results[name] = r
        bands = " ".join(f"{b:5.1f}" for b in r["bands_db"])
        flags = (" CLIPPED" if r["clipped"] else "") + (" SILENT" if r["silent_s"] > 0.5 else "")
        print(f"{name:13}{r['peak_db']:7.1f}{r['rms_db']:7.1f}{r['lufs']:7.1f}{r['crest_db']:6.1f}{r['centroid']:7.0f}{r['rolloff85']:7.0f}{r['flatness']:6.2f}{r['move_db']:6.1f}{r['lines']:6d}{r['rough_db']:6.1f}{r['swing_db']:6.1f}{r['jump_db']:6.1f}{r['click']:6.1f}{r['stereo_corr']:6.2f}{r['dc']:9.5f}  {bands}{flags}")
        if images:
            os.makedirs(images, exist_ok=True)
            draw(name, rate, x, os.path.join(images, name + ".png"))

    names = list(results)
    vec = np.array([results[n]["bands_db"] + [np.log2(results[n]["centroid"]) * 6, results[n]["move_db"], results[n]["flatness"] * 10, results[n]["rough_db"]] for n in names])
    d = np.sqrt(((vec[:, None] - vec[None]) ** 2).sum(axis=2))
    # Scale shape, whatever the key: the best match over all twelve rotations.
    chroma = [results[n]["chroma"] for n in names]
    scale = np.array([[1 - max(np.dot(a, np.roll(b, k)) for k in range(12)) for b in chroma] for a in chroma])
    d = d + 60 * scale
    np.fill_diagonal(d, np.inf)
    nearest = d.argmin(axis=1)
    print("\nnearest mood by measured character (bands, centroid, movement, flatness, roughness, scale shape):")
    for i, n in enumerate(names):
        print(f"  {n:13} {names[nearest[i]]:13} distance {d[i, nearest[i]]:.1f}")


if __name__ == "__main__":
    main()
