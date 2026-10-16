import json
import math
from pathlib import Path
import statistics
import sys


def require(condition, message):
    if not condition:
        raise RuntimeError(message)


def finite_number(value, label, minimum=None, strict=False):
    require(type(value) in (int, float) and math.isfinite(value), f"{label}: finite number required")
    if minimum is not None:
        require(value > minimum if strict else value >= minimum, f"{label}: invalid bound")
    return value


def integer(value, label, minimum=0):
    require(type(value) is int and value >= minimum, f"{label}: integer >= {minimum} required")
    return value


def close(actual, expected, label):
    finite_number(actual, label)
    require(math.isclose(actual, expected, rel_tol=1e-9, abs_tol=1e-12),
            f"{label}: {actual} != {expected}")


def vector(value, label):
    require(isinstance(value, list) and len(value) == 4, f"{label}: four components required")
    for component in value:
        finite_number(component, label)
    return value


def extent(value, label):
    require(isinstance(value, dict), f"{label}: object required")
    return (integer(value.get("width"), label + " width", 1),
            integer(value.get("height"), label + " height", 1))


def trace_extent(result, width, height, divisor, label):
    require(integer(result.get("traceWidth"), label + " Trace width", 1) == (width + divisor - 1) // divisor
            and integer(result.get("traceHeight"), label + " Trace height", 1) == (height + divisor - 1) // divisor,
            label + ": ceil Trace extent")


def read_report(response_path):
    response = json.loads(Path(response_path).read_text())
    require(isinstance(response, dict) and response.get("success") is True, "CLI execution failed")
    payload = response.get("result")
    if isinstance(payload, str):
        payload = json.loads(payload)
    require(isinstance(payload, dict), "CLI result object required")
    require(isinstance(payload.get("output"), str) and payload["output"], "CLI result.output required")
    output = Path(payload["output"])
    report = json.loads((output / "high-budget.json").read_text())
    require(isinstance(report, dict), "High budget report object required")
    return payload, output, report


def validate_report(payload, output, report):
    for flag in ("passed", "restored", "maskMatches", "repeatStable"):
        require(report.get(flag) is True, f"Missing {flag}")
    if "passed" in payload:
        require(payload["passed"] is True, "CLI result failed")
    assertions = integer(report.get("assertions"), "Report assertion count", 1)
    require(integer(payload.get("assertions"), "CLI assertion count", 1) == assertions, "Assertion counts differ")
    require(report.get("isolatedSSGI") is True, "Explicit isolated Diffuse configuration required")
    require(report.get("temporalBlend") == 0.5 and report.get("quality") == "High", "High/blend contract")
    require(report.get("backend") in ("Ray", "DDA"), "Trace backend contract")
    require(report.get("warmup") == 8 and report.get("samples") == 12 and report.get("rounds") == 3,
            "Timing sampling contract")
    shader_time = vector(report.get("shaderTime"), "Report Shader time")
    timing_sizes = report.get("timingSizes")
    require(isinstance(timing_sizes, list) and 1 <= len(timing_sizes) <= 2, "Timing sizes required")
    sizes = [extent(size, "Timing size") for size in timing_sizes]
    require(len(set(sizes)) == len(sizes), "Duplicate timing sizes")
    expected_sizes = list(dict.fromkeys((sizes[0], (1920, 1080))))
    require(sizes == expected_sizes, "Main Camera / 1080p ordered deduplicated sizes")
    if "mainCameraSize" in report:
        require(extent(report["mainCameraSize"], "Main Camera size") == sizes[0], "Main Camera extent mismatch")
    blocks = report.get("blocks")
    require(isinstance(blocks, list) and len(blocks) == len(sizes) * 6, "Complete timing matrix required")
    if "blocks" in payload:
        require(integer(payload["blocks"], "CLI block count", 1) == len(blocks), "CLI block count differs")
    timing = {}
    for size in sizes:
        width, height = size
        size_blocks = [block for block in blocks if extent(block, "Timing block") == size]
        require(len(size_blocks) == 6, f"Six timing blocks required for {width}x{height}")
        expected_order = [(round_index, variant) for round_index in range(3)
                          for variant in (("ThirdHigh", "HalfHigh") if round_index == 1 else ("HalfHigh", "ThirdHigh"))]
        require([(block.get("round"), block.get("variant")) for block in size_blocks] == expected_order,
                "Timing round/variant order")
        for block_index, block in enumerate(size_blocks):
            label = f"{width}x{height} timing block {block_index}"
            variant = block["variant"]
            round_index = integer(block["round"], label + " round")
            if "order" in block:
                require(block["order"] == block_index % 2, label + ": order metadata")
            divisor = 2 if variant == "HalfHigh" else 3
            require(block.get("divisor") == divisor, label + ": divisor")
            trace_extent(block, width, height, divisor, label)
            require(block.get("rays") == 8 and block.get("steps") == 64, label + ": 8 rays / 64 steps")
            require(block.get("shader") == "Assets/Mine/Shaders/PostProcess/SSGI/DiffuseGI/DiffuseGI.shader",
                    label + ": production Diffuse Shader")
            require(block.get("frames") == list(range(8, 20)), label + ": ray frames")
            require(vector(block.get("shaderTime"), label + " Shader time") == shader_time,
                    label + ": fixed Shader time")
            if "shaderTimes" in block:
                require(block["shaderTimes"] == [shader_time] * 12, label + ": per-sample Shader times")
            submitted = block.get("submitMilliseconds")
            synchronized = block.get("synchronizedMilliseconds")
            require(isinstance(submitted, list) and isinstance(synchronized, list)
                    and len(submitted) == len(synchronized) == 12, label + ": complete samples")
            for submit, total in zip(submitted, synchronized):
                finite_number(submit, label + " submit", 0, strict=True)
                finite_number(total, label + " synchronized", submit)
            timing[(size, round_index, variant)] = (statistics.median(submitted), statistics.median(synchronized))
    require(len(timing) == len(blocks), "Unknown or duplicate timing blocks")
    quality_results = report.get("results")
    variants = ("HalfHigh", "ThirdHigh", "HalfHighRepeat")
    require(isinstance(quality_results, list) and len(quality_results) == 3
            and [result.get("variant") for result in quality_results] == list(variants), "Quality A/B/A branches")
    for result, variant in zip(quality_results, variants):
        label = "Quality " + variant
        divisor = 3 if variant == "ThirdHigh" else 2
        trace_extent(result, 640, 360, divisor, label)
        require(result.get("warmup") == 32 and result.get("samples") == 64, label + ": sampling window")
        require(result.get("frames") == list(range(32, 96)), label + ": ray frames")
        require(result.get("historyValid") == [1] * 64, label + ": active history")
        require(result.get("shaderTimes") == [shader_time] * 64, label + ": fixed Shader times")
        integer(result.get("surfacePixels"), label + " surface pixels", 1)
        require(result["surfacePixels"] <= 640 * 360, label + ": surface pixel bound")
        for metric in ("frameRms", "temporalVariance", "meanLuminance"):
            finite_number(result.get(metric), label + " " + metric, 0, strict=True)
        for suffix in (".png", "-mean.png"):
            image_path = output / (variant + suffix)
            require(image_path.is_file() and image_path.stat().st_size > 0, f"Missing quality PNG: {image_path}")
    baseline, candidate, repeat = quality_results
    require(len({result["surfacePixels"] for result in quality_results}) == 1, "Quality surface counts differ")
    close(report.get("noiseReduction"), 1 - candidate["frameRms"] / baseline["frameRms"], "Noise reduction")
    close(report.get("varianceReduction"), 1 - candidate["temporalVariance"] / baseline["temporalVariance"], "Variance reduction")
    close(report.get("meanChange"), candidate["meanLuminance"] / baseline["meanLuminance"] - 1, "Mean change")
    finite_number(report.get("meanImageRms"), "Mean image RMS", 0)
    close(report.get("repeatRelativeTolerance"), 0.002, "Repeat relative tolerance")
    for metric, field in (("frameRms", "repeatFrameRmsChange"), ("temporalVariance", "repeatVarianceChange"),
                          ("meanLuminance", "repeatMeanChange")):
        change = repeat[metric] / baseline[metric] - 1
        close(report.get(field), change, field)
        require(abs(change) <= 0.002, field + ": repeat drift exceeds tolerance")
    repeat_image_rms = finite_number(report.get("repeatMeanImageRms"), "Repeat mean image RMS", 0)
    require(repeat_image_rms / baseline["meanLuminance"] <= 0.002, "Repeat mean image drift exceeds tolerance")
    return sizes, timing


def main():
    require(len(sys.argv) == 2, "Usage: check_high_budget.py CLI_RESPONSE.json")
    payload, output, report = read_report(sys.argv[1])
    sizes, timing = validate_report(payload, output, report)
    print("Timing metric: GPU-synchronized wall time including submission and readback")
    for size in sizes:
        for round_index in range(3):
            half_submit, half_total = timing[(size, round_index, "HalfHigh")]
            third_submit, third_total = timing[(size, round_index, "ThirdHigh")]
            print(f"{size[0]}x{size[1]} round={round_index} "
                  f"synchronized median HalfHigh={half_total:.6f}ms ThirdHigh={third_total:.6f}ms "
                  f"change={(third_total / half_total - 1):+.3%}; "
                  f"submit median HalfHigh={half_submit:.6f}ms ThirdHigh={third_submit:.6f}ms "
                  f"change={(third_submit / half_submit - 1):+.3%}")
    print(json.dumps({"passed": True, "output": str(output),
                      **{field: report[field] for field in ("noiseReduction", "varianceReduction", "meanChange", "meanImageRms")}}))


if __name__ == "__main__":
    main()
