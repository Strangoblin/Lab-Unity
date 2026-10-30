import json
import pathlib
import sys


def main():
    response_path = pathlib.Path(sys.argv[1])
    control = sys.argv[2] != "positive"
    response = json.loads(response_path.read_text())
    if response.get("success") is not True:
        raise RuntimeError(f"Unity execution failed: {response}")
    payload = response.get("result")
    if isinstance(payload, str):
        payload = json.loads(payload)
    if not isinstance(payload, dict) or payload.get("detected" if control else "passed") is not True:
        raise RuntimeError(f"Unexpected validation result: {payload}")
    report = json.loads((pathlib.Path(payload["output"]) / "results.json").read_text())
    if report.get("assertions", 0) <= 0 or report.get("assertions") != payload.get("assertions"):
        raise RuntimeError("Missing or mismatched assertion count")
    if control:
        if report.get("passed") is not False or report.get("negativeControlDetected") is not True:
            raise RuntimeError("Negative control did not detect the injected defect")
    else:
        cases = report.get("cases", [])
        required = {"history-multi-camera-isolation", "exact-history-key-equality",
                    "actual-camera-dense-versus-sparse-dda-ray-sampling", "ao-backend-isolation",
                    "consumers-DDA-Low", "consumers-DDA-Medium", "consumers-DDA-High",
                    "consumers-Ray-Low", "consumers-Ray-Medium", "consumers-Ray-High",
                    "spatial-constant", "spatial-impulse", "spatial-step-depth", "spatial-normal-discontinuity",
                    "spatial-invalid-normal", "spatial-sky", "spatial-boundary", "spatial-radius", "spatial-fallback",
                    "spatial-fractional-parity", "multiscale-checkerboard", "multiscale-checkerboard-parity",
                    "multiscale-constant-hdr", "multiscale-impulse-spreading", "multiscale-depth-edge-leakage",
                    "multiscale-normal-edge-leakage", "multiscale-invalid-guides-zero",
                    "multiscale-boundary-clamping", "multiscale-confidence-not-denominator",
                    "diffuse-single-scale-history-rejected", "diffuse-previous-sequence-history-rejected",
                    "diffuse-half-high-history-rejected", "specular-environment-hdr-history-rejected",
                    "specular-environment-resource-history-rejected", "specular-environment-update-history-rejected",
                    "specular-default-environment-hdr", "intensity-range-integrated-and-standalone"}
        required.update({"ao-mode-history-rejected", "ao-radius-history-rejected",
                         "ao-bias-history-rejected", "ao-angle-history-rejected",
                         "ao-depth-sigma-history-rejected", "ao-tier-history-rejected",
                         "ao-intensity-history-preserved", "ao-blend-history-preserved",
                         "standalone-ao-falloff-history-rejected",
                         "zero-temporal-no-history-sampling-advances",
                         "zero-to-nonzero-first-reject-next-recover",
                         "zero-temporal-mixed-retains-other-histories",
                         "zero-temporal-two-camera-isolation", "zero-temporal-direct-output-precision",
                         "all-disabled-skip-queue-other-feature-inputs",
                         "enabled-uses-actual-pass-instance", "missing-pass-not-enabled-by-shader-setting"})
        required.update(f"history-{kind}-channel-{channel}"
                        for kind in ("depthLayouts", "depthScales") for channel in range(3))
        if report.get("passed") is not True or not required.issubset(cases) or len(cases) != payload.get("cases"):
            raise RuntimeError("Incomplete or failed positive test matrix")
    print(json.dumps(payload))


if __name__ == "__main__":
    main()
