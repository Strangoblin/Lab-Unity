#!/usr/bin/env bash
set -euo pipefail
project="$(git rev-parse --show-toplevel)"
cd "$project"
module="Assets/Mine/Shaders/PostProcess/SSGI"
fixture="$module/RuntimeFixture"
if [[ -e "$fixture" || -e "$fixture.meta" ]]; then
    printf '%s\n' "Refusing to overwrite existing runtime fixtures: $fixture"
    exit 1
fi
run="$(mktemp -d "$project/tmp/sessions/$(date +%F)-ssgi-run-XXXXXXXX")"
mkdir -p "$run/output"
cleanup() {
    local targets=()
    [[ ! -e "$fixture" ]] || targets+=("$fixture")
    [[ ! -e "$fixture.meta" ]] || targets+=("$fixture.meta")
    if [[ ${#targets[@]} -gt 0 ]]; then
        python3 .mcp/validation/gate_cli.py delete --recursive --reason "SSGI runner-owned disposable camera fixtures" "${targets[@]}" || return 1
        unityctl asset refresh
    fi
}
trap cleanup EXIT
for filename in Adapter.shader BaselineDiffuse.shader BaselineSpecular.shader SpatialChecks.shader; do
    python3 .mcp/validation/gate_cli.py write --path "$fixture/$filename" --from "$module/Validation~/RuntimeFixture/$filename"
done
if [[ ${1:-} == --compare ]]; then
    python3 .mcp/validation/gate_cli.py write --path "$fixture/BaselineDiffuseGIFeature.cs" --from "$module/Validation~/BaselineDiffuseGIFeature.cs"
fi
if [[ ${1:-} == --sequence ]]; then
    for filename in SequenceBaselineFunction.hlsl SequenceBaseline.shader TraceCopy.shader; do
        python3 .mcp/validation/gate_cli.py write --path "$fixture/$filename" --from "$module/Validation~/RuntimeFixture/$filename"
    done
fi
unityctl asset refresh
for method in Main NegativeControl HistoryNegativeControl SpatialNegativeControl MultiscaleNegativeControl; do
    unityctl --json script execute "$module/Validation~/CameraChecks.cs" --method "$method" -t 240 > "$run/output/$method.json"
    label="control"
    [[ "$method" != Main ]] || label="positive"
    python3 "$module/Validation~/check_result.py" "$run/output/$method.json" "$label"
done
if [[ ${1:-} == --compare ]]; then
    unityctl --json script execute "$module/Validation~/CameraChecks.cs" --method MultiscaleComparison -t 240 > "$run/output/MultiscaleComparison.json"
    python3 "$module/Validation~/check_comparison.py" "$run/output/MultiscaleComparison.json"
fi
if [[ ${1:-} == --sequence ]]; then
    unityctl --json script execute "$module/Validation~/CameraChecks.cs" --method SequenceComparison -t 240 > "$run/output/SequenceComparison.json"
    python3 "$module/Validation~/check_sequence_comparison.py" "$run/output/SequenceComparison.json"
fi
printf 'CLI reports: %s\n' "$run/output"
