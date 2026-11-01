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
        python3 .mcp/bin/cli.py delete --reason "SSGI runner-owned disposable camera fixtures" "${targets[@]}" || return 1
        unityctl asset refresh
    fi
}
trap cleanup EXIT
for filename in Adapter.shader BaselineDiffuse.shader BaselineSpecular.shader SpatialChecks.shader SpecularBadPyramid.compute SpecularMissingKernel.compute; do
    mkdir -p "$(dirname "$fixture/$filename")"
    cp "$module/Validation~/RuntimeFixture/$filename" "$fixture/$filename"
done
# Inject a coarse-depth fault into a disposable copy of the live Diffuse shader.
python3 - "$module/DiffuseGI/DiffuseGI.shader" "$run/CorruptPyramid.shader" <<'PY'
from pathlib import Path
import sys
source = Path(sys.argv[1]).read_text()
name = 'Shader "PostProcess/DiffuseGI"'
depth = '#define SCREEN_TRACE_LOAD_DEPTH(pixel)'
assert source.count(name) == 1 and source.count(depth) == 1
source = source.replace(name, 'Shader "Hidden/SSGI/CorruptPyramid"')
source = source.replace(depth, '#define SCREEN_TRACE_LOAD_PYRAMID(pixel, mip) 0.0\n    ' + depth)
Path(sys.argv[2]).write_text(source)
PY
mkdir -p "$(dirname "$fixture/CorruptPyramid.shader")"
cp "$run/CorruptPyramid.shader" "$fixture/CorruptPyramid.shader"
if [[ ${1:-} == --compare ]]; then
    mkdir -p "$(dirname "$fixture/BaselineDiffuseGIFeature.cs")"
    cp "$module/Validation~/BaselineDiffuseGIFeature.cs" "$fixture/BaselineDiffuseGIFeature.cs"
fi
if [[ ${1:-} == --sequence ]]; then
    for filename in SequenceBaselineFunction.hlsl SequenceBaseline.shader TraceCopy.shader; do
        mkdir -p "$(dirname "$fixture/$filename")"
        cp "$module/Validation~/RuntimeFixture/$filename" "$fixture/$filename"
    done
fi
unityctl asset refresh
for method in Main NegativeControl HistoryNegativeControl SpatialNegativeControl MultiscaleNegativeControl EnvironmentNegativeControl; do
    unityctl --json script execute "$module/Validation~/CameraChecks.cs" --method "$method" -t 240 > "$run/output/$method.json"
    label="control"
    [[ "$method" != Main ]] || label="positive"
    python3 "$module/Validation~/check_result.py" "$run/output/$method.json" "$label"
done
cat "$module/Validation~/CameraChecks.cs" "$module/Validation~/HiZChecks.cs" > "$run/HiZRunner.cs"
unityctl --json script execute "$run/HiZRunner.cs" --method HizChecks -t 240 > "$run/output/HiZChecks.json"
python3 - "$run/output/HiZChecks.json" <<'PY'
import json, sys
response = json.load(open(sys.argv[1]))
assert response.get('success') is True, response
result = response['result']
if isinstance(result, str): result = json.loads(result)
required = {'three-quality-actual-hiz', 'missing-compute-dda-fallback', 'ray-isolation',
            'partial-viewport-dda-fallback', 'coarse-mip-corruption-detected', 'integrated-history-isolation'}
assert result.get('passed') is True and result.get('assertions', 0) > 0 and required <= set(result.get('checks', [])), result
print(json.dumps(result))
PY
for method in SpecularHiZChecks SharedTraceChecks; do
    cat "$module/Validation~/CameraChecks.cs" "$module/Validation~/$method.cs" > "$run/$method.cs"
    unityctl --json script execute "$run/$method.cs" --method "$method" -t 240 > "$run/output/$method.json"
    python3 - "$run/output/$method.json" "$method" <<'PY'
import json, sys
response = json.load(open(sys.argv[1]))
if response.get('success') is not True:
    raise SystemExit('Unity validation failed: ' + str(response))
result = response.get('result')
if isinstance(result, str): result = json.loads(result)
checks = {
    'SpecularHiZChecks': {'standalone-and-integrated-three-tiers', 'keyword-and-budget',
                          'dda-ray-and-resource-fallback', 'bad-pyramid-consumption-control',
                          'hiz-sky-reference', 'integrated-history-isolation'},
    'SharedTraceChecks': {'legacy-migration', 'migration-persistence', 'shared-pyramid-once',
                          'single-consumer', 'dda-ray-no-pyramid'},
}
if not isinstance(result, dict) or result.get('passed') is not True or result.get('assertions', 0) <= 0 \
        or not checks[sys.argv[2]].issubset(result.get('checks', [])):
    raise SystemExit('Incomplete validation: ' + str(result))
print(json.dumps(result))
PY
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
