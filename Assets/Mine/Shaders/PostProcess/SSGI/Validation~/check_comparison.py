import json
import math
import pathlib
import sys


def main():
    response = json.loads(pathlib.Path(sys.argv[1]).read_text())
    if response.get("success") is not True:
        raise RuntimeError(f"Unity comparison failed: {response}")
    payload = response.get("result")
    if isinstance(payload, str):
        payload = json.loads(payload)
    if not isinstance(payload, dict) or payload.get("passed") is not True:
        raise RuntimeError(f"Unexpected comparison result: {payload}")
    output = pathlib.Path(payload["output"])
    report = json.loads((output / "comparison.json").read_text())
    if report.get("passed") is not True or report.get("restored") is not True:
        raise RuntimeError("Comparison failed or live settings were not preserved")
    if report.get("assertions", 0) <= 0 or report["assertions"] != payload.get("assertions"):
        raise RuntimeError("Missing or mismatched comparison assertions")
    if report.get("temporalBlend") != 0.5 or report.get("backend") not in {"DDA", "Ray"}:
        raise RuntimeError("Unexpected comparison settings")
    results = report.get("results", [])
    if [item.get("variant") for item in results] != ["BaselineDiffuseGIFeature", "DiffuseGIFeature"]:
        raise RuntimeError("Comparison needs a frozen single-scale and a production two-scale branch")
    for result in results:
        if result.get("warmup") != 32 or result.get("samples") != 16:
            raise RuntimeError("Unexpected comparison sampling window")
        if result.get("frames") != list(range(32, 48)) or result.get("historyValid") != [1.0] * 16:
            raise RuntimeError("Mismatched ray sequence or inactive history")
        if result.get("shaderTimes") != [report["shaderTime"]] * 16:
            raise RuntimeError("Shader time drifted during the comparison")
        if result.get("surfacePixels", 0) <= 0:
            raise RuntimeError("Comparison has no visible geometry")
        for name in ("frameRms", "meanLuminance"):
            if not math.isfinite(result.get(name, float("nan"))) or result[name] <= 0:
                raise RuntimeError(f"Invalid or empty comparison signal: {name}")
        if not (output / (result["variant"] + ".png")).is_file():
            raise RuntimeError("Missing full-frame GI comparison image")
    if report.get("maskMatches") is not True or results[0]["surfacePixels"] != results[1]["surfacePixels"]:
        raise RuntimeError("Comparison surface masks differ")
    expected_noise = 1 - results[1]["frameRms"] / results[0]["frameRms"]
    expected_mean = results[1]["meanLuminance"] / results[0]["meanLuminance"] - 1
    if not math.isclose(report.get("noiseReduction", float("nan")), expected_noise, abs_tol=1e-12):
        raise RuntimeError("Incorrect reported noise reduction")
    if not math.isclose(report.get("meanChange", float("nan")), expected_mean, abs_tol=1e-12):
        raise RuntimeError("Incorrect reported mean change")
    print(json.dumps(payload))


if __name__ == "__main__":
    main()
