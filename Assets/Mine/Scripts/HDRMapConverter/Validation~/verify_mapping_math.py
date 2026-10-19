#!/usr/bin/env python3
"""Independent double-precision reference for the HDRMap HLSL mapping contract.

This validates the formulas/topology, not GPU compilation or texture orientation.
The Unity axis-color/HDR/alpha render checks are still required after import.
"""
import math
import random


def sign(value):
    return -1.0 if value < 0.0 else 1.0


def normalize(value):
    scale = math.sqrt(sum(x * x for x in value))
    return tuple(x / scale for x in value)


def encode(direction, projection):
    x, y, z = normalize(direction)
    if projection == 0:
        longitude = math.atan2(x, z) if abs(x) + abs(z) > 1e-7 else 0.0
        return ((longitude / (2 * math.pi) + 0.5) % 1.0,
                math.asin(max(-1.0, min(1.0, y))) / math.pi + 0.5)
    if projection == 1:
        divisor = abs(x) + abs(y) + abs(z)
        x, y, z = x / divisor, y / divisor, z / divisor
        folded = max(-y, 0.0)
        return ((x + sign(x) * folded) * 0.5 + 0.5,
                (z + sign(z) * folded) * 0.5 + 0.5)
    if projection == 2:
        radius = math.sqrt(max(0.0, 1.0 - abs(y)))
        azimuth = math.atan2(abs(z), abs(x)) * 2 / math.pi if abs(x) + abs(z) > 1e-7 else 0.0
        a, b = radius * (1.0 - azimuth), radius * azimuth
        if y < 0:
            a, b = 1.0 - b, 1.0 - a
        return (a * sign(x) * 0.5 + 0.5, b * sign(z) * 0.5 + 0.5)
    if projection == 3:
        return (x * 0.5 + 0.5, y * 0.5 + 0.5)
    raise ValueError(projection)


def decode(uv, projection):
    u, v = uv
    if projection == 0:
        longitude, latitude = (u - 0.5) * 2 * math.pi, (v - 0.5) * math.pi
        return (math.sin(longitude) * math.cos(latitude), math.sin(latitude), math.cos(longitude) * math.cos(latitude))
    if projection == 1:
        x, z = u * 2 - 1, v * 2 - 1
        y = 1 - abs(x) - abs(z)
        folded = max(-y, 0.0)
        return normalize((x - sign(x) * folded, y, z - sign(z) * folded))
    if projection == 2:
        a, b = u * 2 - 1, v * 2 - 1
        diamond = 1 - abs(a) - abs(b)
        radius = 1 - abs(diamond)
        azimuth = math.pi * 0.25 * ((abs(b) - abs(a)) / radius + 1) if radius > 1e-7 else 0.0
        radial = radius * math.sqrt(max(2 - radius * radius, 0))
        return (radial * sign(a) * math.cos(azimuth), sign(diamond) * (1 - radius * radius), radial * sign(b) * math.sin(azimuth))
    if projection == 3:
        x, y = u * 2 - 1, v * 2 - 1
        return (x, y, math.sqrt(max(1 - x * x - y * y, 0)))
    raise ValueError(projection)


def error(a, b):
    return math.sqrt(sum((x - y) ** 2 for x, y in zip(a, b)))


def remap(texel, size, projection):
    x, y = texel
    width, height = size
    if projection == 0:
        return (x % width, max(0, min(height - 1, y)))
    if projection in (1, 2):
        if x < 0:
            x, y = -x - 1, height - y - 1
        elif x >= width:
            x, y = 2 * width - x - 1, height - y - 1
        if y < 0:
            x, y = width - x - 1, -y - 1
        elif y >= height:
            x, y = width - x - 1, 2 * height - y - 1
    return (max(0, min(width - 1, x)), max(0, min(height - 1, y)))


def verify():
    random.seed(382918)
    axes = [(1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0), (0, 0, 1), (0, 0, -1)]
    directions = axes + [normalize(tuple(random.uniform(-1, 1) for _ in range(3))) for _ in range(50000)]
    for projection in range(4):
        selected = [d for d in directions if projection != 3 or d[2] >= 0]
        maximum = max(error(d, decode(encode(d, projection), projection)) for d in selected)
        assert maximum < 1e-7, (projection, maximum)
        print(f"projection={projection}: {len(selected)} direction round trips; max vector error={maximum:.3g}")

    for projection in (1, 2):
        for y in range(129):
            for x in range(129):
                direction = decode((x / 128, y / 128), projection)
                assert all(math.isfinite(value) for value in direction)
                assert abs(sum(value * value for value in direction) - 1) < 1e-10
        for n in (1, 2, 16, 127, 256):
            for y in range(-1, n + 1):
                for x in range(-1, n + 1):
                    a, b = remap((x, y), (n, n), projection)
                    assert 0 <= a < n and 0 <= b < n
        # Matching boundary points represent exactly the same directions.
        for index in range(1001):
            t = index / 1000
            assert error(decode((0, t), projection), decode((0, 1 - t), projection)) < 1e-7
            assert error(decode((1, t), projection), decode((1, 1 - t), projection)) < 1e-7
            assert error(decode((t, 0), projection), decode((1 - t, 0), projection)) < 1e-7
            assert error(decode((t, 1), projection), decode((1 - t, 1), projection)) < 1e-7
    print("octahedral grids, centre/corners/axes, and bilinear seam topology: passed")

    # Equal-area Jacobian |dD/du cross dD/dv| must be 4*PI everywhere off folds.
    jacobians = []
    epsilon = 1e-6
    for _ in range(2000):
        u, v = random.uniform(0.01, 0.99), random.uniform(0.01, 0.99)
        a, b = 2 * u - 1, 2 * v - 1
        if min(abs(a), abs(b), abs(1 - abs(a) - abs(b))) < 0.002:
            continue
        du = tuple((x - y) / (2 * epsilon) for x, y in zip(decode((u + epsilon, v), 2), decode((u - epsilon, v), 2)))
        dv = tuple((x - y) / (2 * epsilon) for x, y in zip(decode((u, v + epsilon), 2), decode((u, v - epsilon), 2)))
        cross = (du[1] * dv[2] - du[2] * dv[1], du[2] * dv[0] - du[0] * dv[2], du[0] * dv[1] - du[1] * dv[0])
        jacobians.append(math.sqrt(sum(x * x for x in cross)))
    maximum_area_error = max(abs(value / (4 * math.pi) - 1) for value in jacobians)
    assert maximum_area_error < 1e-5, maximum_area_error
    print(f"equal-area finite-difference Jacobian: {len(jacobians)} samples; max relative error={maximum_area_error:.3g}")


if __name__ == "__main__":
    verify()
