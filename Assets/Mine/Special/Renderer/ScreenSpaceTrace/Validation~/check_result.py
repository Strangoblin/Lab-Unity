import json
import sys
from pathlib import Path

response = json.loads(Path(sys.argv[1]).read_text())
control = sys.argv[2] == "NegativeControl"
if response.get("success") is not True:
    raise SystemExit("GPU command failed: " + json.dumps(response))
payload = response.get("result")
if isinstance(payload, str):
    payload = json.loads(payload)
if not isinstance(payload, dict) or payload.get("detected" if control else "passed") is not True:
    raise SystemExit("Unexpected GPU validation result")
report = json.loads(Path(payload["output"]).read_text())
if report.get("assertions", 0) <= 0 or report["assertions"] != payload.get("assertions"):
    raise SystemExit("Missing GPU assertion evidence")
if control:
    if report.get("passed") is not False or report.get("negativeControl") is not True or "distance backend 1" not in report.get("error", ""):
        raise SystemExit("GPU negative control did not detect the injected distance defect")
else:
    if report.get("passed") is not True or len(report.get("cases", [])) != payload.get("cases") or payload["cases"] < 35:
        raise SystemExit("Incomplete GPU validation matrix")
print(json.dumps(payload))
