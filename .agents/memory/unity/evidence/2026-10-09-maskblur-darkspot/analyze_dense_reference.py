#!/usr/bin/env python3
"""Dense expectation of the current independently uniform stratified blur.

This integrates the bilinear tent exactly for each constant-mass stratum. It
does not replace the piecewise distribution with a Gaussian, use Monte Carlo,
or assert statistical independence of the deterministic GPU hash streams.

Manifest example (paths relative to input_dir or the manifest directory):
{
  "width": 640, "height": 360, "input_dir": "inputs",
  "cases": [
    {"name": "R16-one", "source": "R16-source.rgba",
     "output": "R16-one.rgba", "rounds": 1, "radius_pixels": 18,
     "taps": 9, "strength": 1, "crop_size": 128}
  ]
}

rounds sets a common per-pass radius of radius_pixels/sqrt(rounds), matching
the Feature. per_pass_radius_pixels can instead supply the actually executed
radius. Files are headerless little-endian float32 RGBA in H x W x 4 order.
Only an explicitly supplied flip_output_y changes readback orientation.
"""
from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFont


FIXED_SUM = 1.0006476
TARGET_M2 = 1.43852761
WEIGHTS = {
    5: np.array([0.1216216, 0.2332432, 0.290918, 0.2332432, 0.1216216]) / FIXED_SUM,
    9: np.array([0.0544094825, 0.0879394219, 0.1239134970, 0.1522227692,
                 0.1630296588, 0.1522227692, 0.1239134970, 0.0879394219, 0.0544094825]),
    13: np.array([0.0346004322, 0.0496734398, 0.0667749739, 0.0840523767,
                  0.0990679210, 0.1093359240, 0.1129898648, 0.1093359240,
                  0.0990679210, 0.0840523767, 0.0667749739, 0.0496734398, 0.0346004322]),
}
SHADER_M2 = {5: 1.4385276095200747, 9: 1.4258538730, 13: 1.4254231168}


def tent_cdf(value: np.ndarray) -> np.ndarray:
    value = np.asarray(value, dtype=np.float64)
    return np.where(value <= -1, 0.0,
                    np.where(value < 0, 0.5 * (value + 1) ** 2,
                             np.where(value < 1, 1 - 0.5 * (1 - value) ** 2, 1.0)))


def dense_kernel(taps: int, radius: float, strength: float) -> tuple[np.ndarray, np.ndarray]:
    strength = float(np.clip(strength, 0, 1))
    if strength == 0:
        # All jittered shader entries fall back to the old fixed five taps.
        weights = WEIGHTS[5]
        midpoints = np.arange(-2, 3, dtype=float) * radius
        span = 0.0
    else:
        weights = WEIGHTS[taps]
        width = 5.0 / taps
        sample_scale = np.sqrt(TARGET_M2 / (SHADER_M2[taps] + (strength * width) ** 2 / 12))
        midpoints = ((np.arange(taps) + 0.5) * width - 2.5) * sample_scale * radius
        span = width * strength * sample_scale * radius
    support = int(np.ceil(np.max(np.abs(midpoints)) + span / 2 + 1))
    indices = np.arange(-support, support + 1, dtype=int)
    coefficients = np.zeros(indices.size, dtype=np.float64)
    for mass, midpoint in zip(weights, midpoints):
        if span <= 1e-12:
            coefficients += mass * np.maximum(1 - np.abs(midpoint - indices), 0)
        else:
            low = midpoint - span / 2
            high = midpoint + span / 2
            coefficients += mass / span * (tent_cdf(high - indices) - tent_cdf(low - indices))
    # Do not renormalize: preserve the actual rounded shader weight table sum.
    return indices, coefficients


def convolve_axis(image: np.ndarray, coefficients: np.ndarray, axis: int) -> np.ndarray:
    padding = [(0, 0)] * image.ndim
    padding[axis] = (len(coefficients) // 2, len(coefficients) // 2)
    source = np.pad(image, padding, mode="edge")
    # The current tables and strata are symmetric, so convolution/reversal
    # give the same operator as source samples at output_position + offset.
    return np.apply_along_axis(lambda a: np.convolve(a, coefficients, mode="valid"), axis, source)


def read_rgba(path: Path, width: int, height: int) -> np.ndarray:
    data = np.fromfile(path, dtype="<f4")
    if data.size != width * height * 4:
        raise ValueError(f"Incorrect float RGBA size: {path} ({data.size} floats)")
    data = data.reshape(height, width, 4).astype(np.float64)
    if not np.all(np.isfinite(data)):
        raise ValueError(f"Nonfinite GPU/source values: {path}")
    return data


def distribution(values: np.ndarray) -> dict:
    return {"min": float(values.min()), "max": float(values.max()),
            "mean": float(values.mean()), "std": float(values.std()),
            "p01_p50_p99": np.quantile(values, [0.01, 0.5, 0.99]).tolist(),
            "zero_count": int(np.count_nonzero(values == 0)),
            "le_1e_minus6_count": int(np.count_nonzero(values <= 1e-6))}


def display_rgb(rgba: np.ndarray) -> np.ndarray:
    # One common physical linear -> display mapping, with no per-image exposure.
    linear = np.maximum(rgba[..., :3], 0)
    encoded = np.where(linear <= 0.0031308, 12.92 * linear,
                       1.055 * linear ** (1 / 2.4) - 0.055)
    return np.clip(np.rint(encoded * 255), 0, 255).astype(np.uint8)


def self_check() -> dict:
    # Tests integration and normalization, rather than reproducing GPU output.
    table = {}
    for taps in (5, 9, 13):
        indices, coefficients = dense_kernel(taps, 18, 1)
        assert coefficients.min() >= -1e-12
        assert abs(coefficients.sum() - WEIGHTS[taps].sum()) < 1e-12
        assert np.max(np.abs(coefficients - coefficients[::-1])) < 1e-12
        assert abs(float(np.dot(indices, coefficients))) < 1e-12
        table[taps] = {"sum": float(coefficients.sum()),
                       "discrete_m2_pixels": float(np.dot(indices * indices, coefficients))}
    # A narrow symmetric uniform stratum has its exact bilinear variance.
    span = 0.4
    values = (tent_cdf(span / 2 - np.array([-1, 0, 1])) -
              tent_cdf(-span / 2 - np.array([-1, 0, 1]))) / span
    assert np.max(np.abs(values - np.array([0.05, 0.9, 0.05]))) < 1e-12
    return table


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--manifest", type=Path)
    parser.add_argument("--out-dir", type=Path)
    parser.add_argument("--self-check", action="store_true")
    args = parser.parse_args()
    if args.self_check:
        print(json.dumps({"self_check": "passed", "kernels": self_check()}, indent=2))
        if args.manifest is None:
            return
    if args.manifest is None:
        parser.error("--manifest is required unless only --self-check is used")
    if args.out_dir is None:
        parser.error("--out-dir is required with --manifest; use a task output directory")
    manifest = json.loads(args.manifest.read_text())
    width = int(manifest.get("width", 640))
    height = int(manifest.get("height", 360))
    inputs = Path(manifest.get("input_dir", "."))
    if not inputs.is_absolute():
        inputs = args.manifest.parent / inputs
    args.out_dir.mkdir(parents=True, exist_ok=True)
    results = []
    panels = []
    expected_path = Path(__file__).with_name("expected-reference.json")
    expected = json.loads(expected_path.read_text()) if args.self_check else None
    array_checks = []
    for case in manifest["cases"]:
        name = case["name"]
        source = read_rgba(inputs / case["source"], width, height)
        gpu = read_rgba(inputs / case["output"], width, height)
        if case.get("flip_output_y", False):
            gpu = gpu[::-1]
        rounds = int(case.get("rounds", 1))
        if rounds < 1:
            raise ValueError("rounds must be positive")
        radius = float(case.get("per_pass_radius_pixels", float(case.get("radius_pixels", 18)) / np.sqrt(rounds)))
        taps = int(case.get("taps", 9))
        strength = float(case.get("strength", 1))
        indices, coefficients = dense_kernel(taps, radius, strength)
        axes = case.get("axes", ["H", "V"] * rounds)
        reference = source.copy()
        for direction in axes:
            if direction not in ("H", "V"):
                raise ValueError(f"Unknown axis {direction}")
            reference = convolve_axis(reference, coefficients, 1 if direction == "H" else 0)
        occupied = source[..., 3] > 0.5
        ys, xs = np.nonzero(occupied)
        if xs.size == 0:
            # A constant RGBA control may intentionally use alpha < 0.5.
            occupied = np.max(np.abs(source), axis=2) > 0
            ys, xs = np.nonzero(occupied)
        if xs.size == 0:
            raise ValueError(f"Empty source field {name}")
        center_x, center_y = case.get("center_xy", [float((xs.min() + xs.max()) / 2),
                                                  float((ys.min() + ys.max()) / 2)])
        yy, xx = np.indices((height, width))
        core_radius = float(case.get("core_radius", 4))
        core = (xx - center_x) ** 2 + (yy - center_y) ** 2 <= core_radius ** 2
        residual = gpu[..., 0] - reference[..., 0]
        values = {"name": name, "taps": taps, "axes": axes, "rounds": rounds,
                  "executed_radius_pixels": radius, "jitter_strength": strength,
                  "kernel_mass": float(coefficients.sum()),
                  "kernel_discrete_m2_pixels": float(np.dot(indices * indices, coefficients)),
                  "center_xy": [center_x, center_y], "core_radius": core_radius,
                  "core_pixel_count": int(core.sum()),
                  "gpu_core_R": distribution(gpu[..., 0][core]),
                  "reference_core_R": distribution(reference[..., 0][core]),
                  "residual_core_R": distribution(residual[core]),
                  "core_residual_rmse": float(np.sqrt(np.mean(residual[core] ** 2))),
                  "core_residual_nrmse": float(np.sqrt(np.mean(residual[core] ** 2)) / max(reference[..., 0][core].mean(), 1e-12)),
                  "gpu_R_alpha_max_abs_difference": float(np.max(np.abs(gpu[..., 0] - gpu[..., 3]))),
                  "gpu_GB_max_abs": float(np.max(np.abs(gpu[..., 1:3]))),
                  "source_R_alpha_max_abs_difference": float(np.max(np.abs(source[..., 0] - source[..., 3]))),
                  "gpu_reference_total_linear_R_ratio": float(gpu[..., 0].sum() / max(reference[..., 0].sum(), 1e-12))}
        results.append(values)
        reference_file = args.out_dir / f"{name}-dense-reference.npy"
        saved = reference.astype(np.float32)
        np.save(reference_file, saved)
        if expected is not None:
            baseline = expected["arrays"][reference_file.name]
            digest = hashlib.sha256(reference_file.read_bytes()).hexdigest()
            if (digest != baseline["sha256"] or list(saved.shape) != baseline["shape"]
                    or str(saved.dtype) != baseline["dtype"]):
                raise ValueError(f"Historical dense-reference reproduction mismatch: {name}")
            array_checks.append({"name": reference_file.name, "sha256": digest,
                                 "shape": list(saved.shape), "dtype": str(saved.dtype),
                                 "matches_immutable_expected": True})
        crop_size = int(case.get("crop_size", 128))
        x0 = int(round(center_x + 0.5)) - crop_size // 2
        y0 = int(round(center_y + 0.5)) - crop_size // 2
        if x0 < 0 or y0 < 0 or x0 + crop_size > width or y0 + crop_size > height:
            raise ValueError(f"Crop outside full-frame image: {name}")
        crops = []
        for image in (source, gpu, reference):
            crop = Image.fromarray(display_rgb(image[y0:y0 + crop_size, x0:x0 + crop_size]))
            if crop_size != 256:
                crop = crop.resize((256, 256), Image.Resampling.NEAREST)
            crops.append(crop)
        panels.append((name, crop_size, crops))
    report = {"reference_model": "Exact bilinear-integrated expectation under ideal uniform independent strata; deterministic hash is compared, not assumed independent.",
              "value_space": "linear float32 GPU readback; references computed float64",
              "cases": results}
    if expected is not None:
        original_report = expected_path.parent / expected["report_path"]
        original_bytes = original_report.read_bytes()
        if hashlib.sha256(original_bytes).hexdigest() != expected["report_sha256"]:
            raise ValueError("Historical numeric report does not match immutable expected SHA256")
        if json.loads(original_bytes) != report:
            raise ValueError("Regenerated numeric report differs from the historical report")
        if {x["name"] for x in array_checks} != set(expected["arrays"]):
            raise ValueError("Regenerated case set differs from the historical expected arrays")
        proof = {"status": "passed", "reference_files": array_checks,
                 "report_values_exact_equal": True,
                 "scope": "Frozen CPU reference from saved RGBA; no Editor/GPU replay."}
        (args.out_dir / "reference-self-check.json").write_text(json.dumps(proof, indent=2) + "\n")
    (args.out_dir / "dense-reference-report.json").write_text(json.dumps(report, indent=2) + "\n")
    font = ImageFont.load_default()
    panel = Image.new("RGB", (836, 70 + 287 * len(panels)), "#161a20")
    draw = ImageDraw.Draw(panel)
    for col, title in enumerate(("Source", "GPU", "Dense expected reference")):
        draw.text((26 + 273 * col, 15), title, font=font, fill="white")
    draw.text((26, 39), "Common linear -> sRGB transfer; nearest crops; no smoothing or exposure normalization", font=font, fill="#ccd5e0")
    for row, (name, crop_size, crops) in enumerate(panels):
        top = 93 + row * 287
        draw.text((26, top - 20), f"{name} / {crop_size}px crop", font=font, fill="white")
        for col, crop in enumerate(crops):
            panel.paste(crop, (26 + 273 * col, top))
    panel.save(args.out_dir / "dense-reference-comparison.png")
    print(json.dumps(report, indent=2))


if __name__ == "__main__":
    main()
