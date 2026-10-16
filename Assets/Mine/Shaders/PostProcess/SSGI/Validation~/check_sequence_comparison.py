import json
import math
from pathlib import Path
import sys


def require(condition, message):
    if not condition:
        raise RuntimeError(message)


def close(actual, expected):
    require(math.isfinite(actual) and math.isclose(actual, expected, rel_tol=1e-9, abs_tol=1e-12),
            f"Derived metric mismatch: {actual} != {expected}")


def main():
    response = json.loads(Path(sys.argv[1]).read_text())
    require(response.get("success") is True, f"Unity execution failed: {response}")
    payload = response["result"]
    if isinstance(payload, str):
        payload = json.loads(payload)
    report = json.loads((Path(payload["output"]) / "sequence-comparison.json").read_text())
    for flag in ("passed", "restored", "maskMatches", "repeatStable"):
        require(report.get(flag) is True, f"Missing {flag}")
    require(report["assertions"] == payload["assertions"] > 0, "Assertion count")
    require(report["temporalBlend"] == 0.5 and report["format"] == "R16G16B16A16_SFloat", "Signal conditions")
    require(report["width"] == 640 and report["height"] == 360, "Comparison resolution")
    tier = {"Low": (8, 4, 24), "Medium": (4, 6, 48), "High": (3, 8, 64)}[report["quality"]]
    require(report["traceWidth"] == (640 + tier[0] - 1) // tier[0]
            and report["traceHeight"] == (360 + tier[0] - 1) // tier[0], "Native Trace resolution")
    results = report["results"]
    require([result["variant"] for result in results] == ["baseline", "candidate", "baseline_repeat"], "A/B/A branches")
    for result in results:
        require(result["warmup"] == 32 and result["samples"] == 64, "Sampling window")
        require(result["frames"] == list(range(32, 96)), "Frame coverage")
        require(result["historyValid"] == [1] * 64 and result["blends"] == [0.5] * 64, "Active weak history")
        require(result["rays"] == [tier[1]] * 64 and result["steps"] == [tier[2]] * 64, "Unchanged budgets")
        require(result["times"] == [report["shaderTime"]] * 64, "Fixed time")
        for signal, width, height in (("rawTrace", report["traceWidth"], report["traceHeight"]), ("history", 640, 360)):
            metrics = result[signal]
            require(metrics["width"] == width and metrics["height"] == height and metrics["samples"] == 64, "Signal extent")
            require(0 < metrics["surfacePixels"] <= width * height, "Nonempty surface")
            for name in ("meanLuminance", "frameRms", "temporalVariance"):
                require(math.isfinite(metrics[name]) and metrics[name] > 0, f"Nonempty stochastic {signal}:{name}")
    for signal, metric in (("rawTrace", "raw"), ("history", "history")):
        baseline, candidate, repeat = [result[signal] for result in results]
        tolerance = report["repeatRelativeTolerance"]
        require(tolerance == 0.002, "Repeat-control bound")
        for name in ("frameRms", "temporalVariance", "meanLuminance"):
            require(abs(repeat[name] / baseline[name] - 1) <= tolerance, "Repeated baseline metrics drift")
        repeat_metrics = report["repeatRaw" if signal == "rawTrace" else "repeatHistory"]["metrics"]
        require(repeat_metrics["meanImageRms"] / baseline["meanLuminance"] <= tolerance, "Repeat mean image drift")
        close(repeat_metrics["frameRmsReduction"], 1 - repeat["frameRms"] / baseline["frameRms"])
        close(repeat_metrics["varianceReduction"], 1 - repeat["temporalVariance"] / baseline["temporalVariance"])
        close(repeat_metrics["meanChange"], repeat["meanLuminance"] / baseline["meanLuminance"] - 1)
        require(baseline["surfacePixels"] == candidate["surfacePixels"], "Surface counts differ")
        derived = report[metric]
        close(derived["frameRmsReduction"], 1 - candidate["frameRms"] / baseline["frameRms"])
        close(derived["varianceReduction"], 1 - candidate["temporalVariance"] / baseline["temporalVariance"])
        close(derived["meanChange"], candidate["meanLuminance"] / baseline["meanLuminance"] - 1)
        require(math.isfinite(derived["meanImageRms"]) and derived["meanImageRms"] >= 0, "Mean image RMS")
    print(json.dumps({"passed": True, "raw": report["raw"], "history": report["history"], "output": payload["output"]}))


if __name__ == "__main__":
    main()
