#!/usr/bin/env python3
"""Representative float32 semantic probes; not a Unity/GPU visual test."""
import hashlib
import json
import sys

sys.dont_write_bytecode = True
import verify_hash as common

import numpy as np

HERE = common.HERE
BASELINE = common.FIXTURES / "final-stage-before.json"
source = json.loads(BASELINE.read_text())
F = np.float32


def frac(value):
    return value - np.floor(value)


def frac22(key):
    p = frac(key * np.array([123.34, 456.21], dtype=F))
    p = p + np.sum(p * (p + F(45.32)), axis=-1, keepdims=True, dtype=F)
    return frac(np.stack([p[..., 0] * p[..., 1], p[..., 0] + p[..., 1]], -1))


def frac_grid22(key, rain=False):
    # Transcription of the baseline HLSL p3 bodies. Each array operation is float32.
    multiplier = F(.1031) if rain else np.array([.1031, .1030, .0973], dtype=F)
    p = frac(key[..., [0, 1, 0]] * multiplier)
    p = p + np.sum(p * (p[..., [1, 2, 0]] + F(33.33)), axis=-1, keepdims=True, dtype=F)
    return frac((p[..., [0, 0]] + p[..., [1, 2]]) * p[..., [2, 1]])


def sine21(key, coefficients=(12.9898, 78.233), multiplier=43758.5453):
    dot = np.sum(key * np.array(coefficients, dtype=F), axis=-1, dtype=F)
    return frac(np.sin(dot) * F(multiplier))


def correlation(left, right):
    a, b = left.astype(np.float64).ravel(), right.astype(np.float64).ravel()
    if np.std(a) == 0 or np.std(b) == 0:
        return None
    return float(np.corrcoef(a, b)[0, 1])


def metrics(value, value_range=(0, 1), spatial=True):
    if value.ndim == 2:
        value = value[..., None]
    flat = value.reshape(-1, value.shape[-1]).astype(np.float64)
    histograms = [np.histogram(flat[:, channel], bins=16, range=value_range)[0]
                  for channel in range(flat.shape[-1])]
    result = {
        'samples': len(flat), 'channels': flat.shape[-1],
        'min': np.min(flat, axis=0).tolist(), 'max': np.max(flat, axis=0).tolist(),
        'mean': np.mean(flat, axis=0).tolist(), 'std': np.std(flat, axis=0).tolist(),
        'range_contract_holds': bool(np.all(np.isfinite(flat)) and
                                     np.all(flat >= value_range[0]) and np.all(flat < value_range[1])),
        'histogram_16_bins': [histogram.tolist() for histogram in histograms],
        'channel_correlation': correlation(flat[:, 0], flat[:, 1]) if flat.shape[-1] > 1 else None,
    }
    if spatial and value.ndim == 3:
        for lag in (1, 50, 100):
            for axis, name in ((1, 'x'), (0, 'y')):
                if value.shape[axis] > lag:
                    left, right = [slice(None)] * 3, [slice(None)] * 3
                    left[axis], right[axis] = slice(None, -lag), slice(lag, None)
                    a, b = value[tuple(left)], value[tuple(right)]
                    result[f'lag_{lag}_{name}'] = [correlation(a[..., c], b[..., c])
                                                  for c in range(value.shape[-1])]
    return result


def integer_grid(width, height, x_start=0, y_start=0):
    y, x = np.mgrid[y_start:y_start + height, x_start:x_start + width]
    return np.stack([x, y], -1).astype(F)


def uv_grid(width, height):
    return (integer_grid(width, height) + F(.5)) / np.array([width, height], dtype=F)


def vector_metrics(value, value_range=(0, 1)):
    # Shape (1, samples, channels) marks the sample sequence as the x axis.
    return metrics(value[None, ...], value_range)


def coverage_metrics(value):
    result = metrics(value)
    result['coverage'] = {str(t): float(np.mean(value[..., 0] < F(t)))
                          for t in (0., .1, .17, .425, .5, .85, .9, 1.)}
    # Existing weighted lifetime offset; unit-period fraction is applied later by caller.
    offset = value[..., 1] * F(.618) + value[..., 0] * F(.382)
    result['lifetime_offset_mean_std'] = [float(offset.astype(float).mean()),
                                          float(offset.astype(float).std())]
    return result


def source_assertions():
    checks = {
        'Assets/Mine/Shaders/Render/RainDrops/RainDrop.hlsl': [
            'float3 p3 = frac(float3(p.xyx) * 0.1031);', 'p3.yzx + 33.33',
            'Hash2D(float2(colIdx, 137.0)).x', 'float2 id = floor(gridUV);',
            'rand.y * 0.618 + rand.x * 0.382'],
        'Assets/Mine/Shaders/PostProcess/SSGI/SpecularGI/SpecularGISampling.hlsl': [
            'float3(0.1031, 0.1030, 0.0973)', 'return frac((p.xx + p.yz) * p.zy);',
            'uv * _ScreenParams.xy + _FrameIndex * float2(0.754877, 0.569840)'],
        'Assets/Mine/Shaders/PostProcess/SSL/SSL.shader': [
            'float2(41.0, 289.0)', '* 45758.5453', 'hash21(uv + frac(_Time.y)) * 2.0 - 1.0'],
        'Assets/Mine/Shaders/Render/VanGogh/TheStarryNightSDF.hlsl': [
            'sin(x * 12.9898)', 'sin(x * 127.1 + y * 311.7)', '* 43758.5453) - 0.5'],
    }
    for path, fragments in checks.items():
        for fragment in fragments:
            assert fragment in source[path], (path, fragment)
    return {path: hashlib.sha256(source[path].encode()).hexdigest() for path in checks}


def analyze():
    report = {
        'method': f'NumPy {np.__version__} float32 transcription of source-checked HLSL; no FMA contraction; statistics accumulated in float64.',
        'baseline': str(BASELINE.relative_to(HERE)),
        'baseline_path_base': 'validation script directory',
        'source_sha256': source_assertions(),
        'limitations': [
            'Representative domains, not captured mesh/camera/scene data.',
            'No Unity, Metal, actual GPU compiler, spatial spectrum, rendered image, instruction-cost, or perceptual equivalence test.',
            'CPU sin/frac and dot accumulation may differ from Unity/GPU implementations.',
            'A finite set of random column keys can noticeably change its mean, order, and neighboring speeds when a sequence changes.',
            'Global marginal histograms cannot establish spatial/temporal independence. Correlations here probe selected lags only.',
        ],
    }
    rain = {}
    for name, key in [
        ('default_12x2', integer_grid(12, 2)),
        ('grid_40x36', integer_grid(40, 36)),
        ('shifted_grid_negative_cells', integer_grid(48, 36, -4, -12)),
        ('expanded_grid_512x256', integer_grid(512, 256)),
    ]:
        rain[name] = {label: coverage_metrics(frac_grid22(key, is_old))
                      for label, is_old in (('old_rain', True), ('new_grid', False))}
    for count in (4, 12, 40, 640):
        key = np.stack([np.arange(count), np.full(count, 137)], -1).astype(F)
        rain[f'column_key_137_{count}_columns'] = {
            label: vector_metrics(frac_grid22(key, is_old)[:, :1])
            for label, is_old in (('old_rain', True), ('new_grid', False))}
    report['rain'] = rain

    pixel = integer_grid(512, 256) + F(.5)
    step = np.array([.754877, .569840], dtype=F)
    spec = {}
    for frame in (0, 1, 127, 1023):
        key = pixel + F(frame) * step
        value = frac_grid22(key)
        spec[f'pixel_centers_frame_{frame}'] = metrics(value)
        # New Hash_FracGrid22 body is chosen directly from this unchanged source body.
        spec[f'pixel_centers_frame_{frame}']['old_new_max_abs_diff_transcription'] = 0.0
    keys = integer_grid(64, 64) + F(.5)
    sequence = np.stack([frac_grid22(keys + F(frame) * step) for frame in range(128)])
    temporal = {}
    for lag in (1, 2, 16, 50, 100):
        temporal[f'frame_lag_{lag}'] = [correlation(sequence[:-lag, ..., channel], sequence[lag:, ..., channel])
                                        for channel in range(2)]
    spec['temporal_128_frames_64x64_pixels'] = temporal
    report['specular_gi'] = spec

    uv = uv_grid(1920, 1080)
    ssl = {}
    for time_fraction in (0., .25, .75):
        key = uv + F(time_fraction)
        ssl[f'1920x1080_time_fraction_{time_fraction}'] = {
            'old': metrics(sine21(key, (41., 289.), 45758.5453)),
            'new': metrics(sine21(key)),
        }
    keys = uv_grid(64, 64)
    for label, coefficients, multiplier in (
        ('old', (41., 289.), 45758.5453), ('new', (12.9898, 78.233), 43758.5453)):
        sequence = np.stack([sine21(keys + F((frame % 60) / 60), coefficients, multiplier)
                             for frame in range(121)])
        ssl[f'{label}_temporal_60hz'] = {
            'frame_lag_1': correlation(sequence[:-1], sequence[1:]),
            'frame_lag_60_one_second_reset': correlation(sequence[:-60], sequence[60:]),
        }
    report['ssl_radial'] = ssl

    vangogh = {}
    for name, key in [
        ('row_col_600x600', integer_grid(600, 600)),
        ('signed_row_col_600x600', integer_grid(600, 600, -300, -300)),
    ]:
        vangogh[name] = {
            'old_hash2': metrics(sine21(key, (127.1, 311.7)) - F(.5), (-.5, .5)),
            'new_hash2': metrics(sine21(key) - F(.5), (-.5, .5)),
        }
    ring = np.arange(1000, dtype=F)
    old = frac(np.sin(ring * F(12.9898)) * F(43758.5453)) - F(.5)
    new = sine21(np.stack([ring, np.zeros_like(ring)], -1)) - F(.5)
    vangogh['scalar_ring_1000'] = {
        'old_new_max_abs_diff_transcription': float(np.max(np.abs(old - new))),
        'old': vector_metrics(old[:, None], (-.5, .5)),
        'new': vector_metrics(new[:, None], (-.5, .5)),
    }
    report['vangogh'] = vangogh

    # Two counterexamples explain why distinct generic algorithms remain by input domain.
    key = integer_grid(512, 256)
    report['rejected_direct_merge_integer_domain'] = {
        'old_frac22': metrics(frac22(key)), 'grid': metrics(frac_grid22(key)),
    }
    report['rejected_direct_merge_normalized_uv'] = {
        'old_frac22': metrics(frac22(uv)), 'grid': metrics(frac_grid22(uv)),
    }
    # Fin is intentionally unchanged. Probe representative UV/independent seed inputs only.
    rng = np.random.default_rng(42)
    hair_uv, hair_seed = rng.random((131072, 2)).astype(F), rng.random((131072, 1)).astype(F)
    fin = {}
    for label, fn in (('unchanged_frac22', frac22), ('rejected_grid_candidate', frac_grid22)):
        rotation = fn(hair_uv + hair_seed)[:, :1]
        dimensions = fn(hair_uv * F(31.) + hair_seed + F(17.))
        fin[label] = {
            'rotation': vector_metrics(rotation), 'dimensions': vector_metrics(dimensions),
            'rotation_length_correlation': correlation(rotation[:, 0], dimensions[:, 0]),
            'rotation_width_correlation': correlation(rotation[:, 0], dimensions[:, 1]),
        }
    report['fin_representative_unchanged_contract'] = fin
    return report


if __name__ == '__main__':
    common.configure(need_compilers=False, label='hash-statistics')
    report = analyze()
    path = common.OUTPUT / 'variant-statistics-report.json'
    path.write_text(json.dumps(report, ensure_ascii=False, indent=2) + '\n')
    print(json.dumps({'report': str(path.relative_to(common.ROOT)) if common.ROOT in path.parents else str(path), 'all_range_contracts': 'see per-domain report fields',
                      'domains': [key for key in report if key not in ('method', 'baseline', 'baseline_path_base', 'source_sha256', 'limitations')]}, ensure_ascii=False))
