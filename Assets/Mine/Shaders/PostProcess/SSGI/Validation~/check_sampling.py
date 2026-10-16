import json
from pathlib import Path
from datetime import date
import sys
import uuid

import numpy as np


def frac(value):
    return value - np.floor(value)


def hash_grid(pixel):
    value = frac(pixel[..., [0, 1, 0]] * np.array([0.1031, 0.1030, 0.0973], dtype=np.float32))
    value += np.sum(value * (value[..., [1, 2, 0]] + np.float32(33.33)), axis=-1, keepdims=True)
    return frac((value[..., [0, 0]] + value[..., [1, 2]]) * value[..., [2, 1]])


def digital_net(pixel):
    cell = pixel.astype(np.uint32) & 7
    horizontal, vertical = cell[..., 0], cell[..., 1]
    first = ((horizontal & 1) << 5) | ((vertical & 1) << 4) | ((horizontal & 2) << 2)
    first |= ((vertical & 2) << 1) | ((horizontal & 4) >> 1) | ((vertical & 4) >> 2)
    second = (horizontal & 1) * 32 ^ (vertical & 1) * 48
    second ^= ((horizontal >> 1) & 1) * 40 ^ ((vertical >> 1) & 1) * 60
    second ^= ((horizontal >> 2) & 1) * 34 ^ ((vertical >> 2) & 1) * 51
    return np.stack([first, second], axis=-1).astype(np.float32) / 64


def rotations(pixel, candidate):
    spatial = digital_net(pixel) + hash_grid(np.floor(pixel / 8)) if candidate else hash_grid(pixel)
    frames = np.arange(128, dtype=np.float32)[:, None, None, None]
    return frac(spatial[None, ...] + frames * np.array([0.75487766, 0.5698403], dtype=np.float32))


def samples(rotation, count, candidate=False):
    ray = np.arange(count, dtype=np.float32)
    lattice = np.stack([(ray + 0.5) / count, ray * np.float32(0.61803398875)], axis=-1)
    points = frac(rotation[..., None, :] + lattice)
    if candidate:
        points[..., 0] = (ray + rotation[..., None, 0]) / count
    return points


def spatial_filter(signal, stride):
    result = np.zeros_like(signal)
    total = 0.0
    for vertical in range(-2, 3):
        for horizontal in range(-2, 3):
            weight = 1 / (1 + horizontal * horizontal + vertical * vertical)
            result += weight * np.roll(signal, (vertical * stride, horizontal * stride), axis=(1, 2))
            total += weight
    return result / total


def rms(signal):
    return float(np.sqrt(np.mean(np.diff(signal[:, 8:-8, 8:-8], axis=0) ** 2)))


def evaluate(rotation, count, candidate=False):
    points = samples(rotation, count, candidate)
    radial, azimuth = points[..., 0], points[..., 1]
    assert np.isfinite(points).all() and points.min() >= 0 and points.max() < 1
    if candidate:
        strata = np.floor(points[..., 0] * count).astype(int)
        assert np.all(strata == np.arange(count))
    bins = [np.histogram(points[..., axis], bins=32, range=(0, 1))[0] for axis in (0, 1)]
    assert all(np.max(np.abs(hist / hist.sum() * 32 - 1)) < 0.04 for hist in bins)
    cosine_mean = float(np.mean(np.sqrt(1 - radial)))
    assert abs(cosine_mean - 2 / 3) < 0.002
    cases = {
        "broad_light": np.maximum(np.cos(2 * np.pi * azimuth), 0) * np.sqrt(radial),
        "angular_occlusion": ((azimuth > 0.1) & (azimuth < 0.4) & (radial > 0.3)).astype(np.float32),
        "small_light": ((azimuth > 0.15) & (azimuth < 0.2) & (radial > 0.65) & (radial < 0.8)).astype(np.float32),
        "horizon": (radial > 0.8).astype(np.float32),
    }
    metrics = {}
    for name, integrand in cases.items():
        trace = np.mean(integrand, axis=-1)
        filtered = spatial_filter(spatial_filter(trace, 1), 2)
        history = np.empty_like(filtered)
        history[0] = filtered[0]
        for frame in range(1, len(history)):
            history[frame] = 0.5 * filtered[frame] + 0.5 * history[frame - 1]
        metrics[name] = {"mean": float(trace.mean()), "raw_rms": rms(trace), "history_rms": rms(history[32:])}
    return {"cosine_mean": cosine_mean, "cases": metrics}


def main():
    vertical, horizontal = np.indices((128, 128), dtype=np.float32)
    pixel = np.stack([horizontal, vertical], axis=-1)
    tile = digital_net(pixel[:8, :8])
    for axis in (0, 1):
        assert len(np.unique(tile[..., axis])) == 64
    for width in (2, 4, 8):
        cells = np.floor(tile[:width, :width] * width).astype(int).reshape(-1, 2)
        assert len(np.unique(cells, axis=0)) == width * width
    original = rotations(pixel, False)
    candidate = rotations(pixel, True)
    assert np.all(np.any(np.diff(candidate, axis=0) != 0, axis=-1))
    assert len(np.unique(candidate[:, 0, 0], axis=0)) == len(candidate)
    report = {"passed": True, "frames": 128, "pixels": [128, 128], "scope": "CPU float32 sampling and flat-guide synthetic integrands; not scene/GPU evidence", "tiers": {}}
    for count in (4, 6, 8):
        report["tiers"][str(count)] = {"baseline": evaluate(original, count), "candidate": evaluate(candidate, count, True)}
    output = Path(sys.argv[1]) if len(sys.argv) > 1 else Path("tmp/sessions") / (str(date.today()) + "-diffuse-sampling-" + uuid.uuid4().hex[:8]) / "output"
    output.mkdir(parents=True, exist_ok=True)
    (output / "sampling.json").write_text(json.dumps(report, indent=2))
    print(json.dumps(report, indent=2))


if __name__ == "__main__":
    main()
