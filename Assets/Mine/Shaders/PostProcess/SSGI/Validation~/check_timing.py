import json
import math
import statistics
import sys
from collections import defaultdict
from pathlib import Path

response = json.loads(Path(sys.argv[1]).read_text())
if response.get("success") is not True:
    raise SystemExit("Unity timing command failed: " + json.dumps(response))
payload = response.get("result")
if isinstance(payload, str):
    payload = json.loads(payload)
if not isinstance(payload, dict) or payload.get("passed") is not True:
    raise SystemExit("Unity timing result failed")
report_path = Path(payload["reportPath"])
report = json.loads(report_path.read_text())
if report.get("passed") is not True or report.get("sceneAndSettingsRestored") is not True:
    raise SystemExit("Timing report failed or original state changed")
if not isinstance(report.get("isolatedSSGI"), bool):
    raise SystemExit("Missing timing configuration mode")
if len(sys.argv) > 2 and report["isolatedSSGI"] != (sys.argv[2] == "TimingIsolatedSSGI"):
    raise SystemExit("Timing configuration mode differs from the requested entry point")
if not report.get("blocks") or payload.get("blocks") != len(report["blocks"]):
    raise SystemExit("Empty or mismatched timing blocks")
if report["samplesPerBlock"] < 12 or report["warmupPerBlock"] < 8 or report["rounds"] < 3:
    raise SystemExit("Insufficient timing samples or warmup")
groups = defaultdict(list)
variants = {"Ray-Ray", "DDA48-DDA64", "Ray-DDA64", "DDA48-Ray"}
for block in report["blocks"]:
    if block["variant"] not in variants or block["diffuseBudget"] != 48 or block["specularBudget"] != 64:
        raise SystemExit("Unexpected backend or production tier budget")
    times = block["synchronizedMilliseconds"]
    submits = block["submitMilliseconds"]
    if len(times) != report["samplesPerBlock"] or len(submits) != len(times):
        raise SystemExit("Incomplete timing block")
    if any(not math.isfinite(total) or not math.isfinite(submit) or total <= 0 or submit < 0 or total < submit
           for total, submit in zip(times, submits)):
        raise SystemExit("Invalid timer samples")
    if block["width"] <= 0 or block["height"] <= 0 or block["firstRayFrame"] < 0:
        raise SystemExit("Invalid resolution or starting ray frame")
    groups[(block["width"], block["height"], block["variant"])].append(block)
sizes = {(key[0], key[1]) for key in groups}
if len(sizes) != 2 or (1920, 1080) not in sizes:
    raise SystemExit("Missing Game View or 1080p timing resolution")
if len(report["blocks"]) != len(sizes) * len(variants) * report["rounds"]:
    raise SystemExit("Unexpected timing matrix size")
for width, height in sizes:
    if {key[2] for key in groups if key[:2] == (width, height)} != variants:
        raise SystemExit("Incomplete variant matrix")
    for variant in variants:
        blocks = groups[(width, height, variant)]
        if len(blocks) != report["rounds"] or {block["round"] for block in blocks} != set(range(report["rounds"])):
            raise SystemExit("Incomplete repeat rounds")
    seeds = {block["firstRayFrame"] for block in report["blocks"] if (block["width"], block["height"]) == (width, height)}
    if len(seeds) != 1:
        raise SystemExit("Unmatched starting ray-frame seeds")
print("report:", report_path)
print("metric:", report["metric"])
for key, blocks in sorted(groups.items()):
    samples = [value for block in blocks for value in block["synchronizedMilliseconds"]]
    ordered = sorted(samples)
    percentile = ordered[math.ceil(0.95 * len(ordered)) - 1]
    medians = [round(statistics.median(block["synchronizedMilliseconds"]), 3) for block in blocks]
    print(f"{key[0]}x{key[1]} {key[2]:17} median={statistics.median(samples):.3f}ms P95={percentile:.3f}ms blocks={medians}")
