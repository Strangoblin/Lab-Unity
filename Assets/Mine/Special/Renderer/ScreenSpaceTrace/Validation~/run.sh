#!/usr/bin/env bash
set -euo pipefail
project="$(git rev-parse --show-toplevel)"
cd "$project"
module="Assets/Mine/Special/Renderer/ScreenSpaceTrace"
fixture="$module/RuntimeFixture"
run="$(mktemp -d "$project/tmp/sessions/$(date +%F)-screen-trace-run-XXXXXXXX")"
mkdir -p "$run/output"
if [[ -e "$fixture" || -e "$fixture.meta" ]]; then
    printf '%s\n' "Refusing to overwrite existing runtime fixtures: $fixture"
    exit 1
fi
cleanup() {
    local targets=()
    [[ ! -e "$fixture" ]] || targets+=("$fixture")
    [[ ! -e "$fixture.meta" ]] || targets+=("$fixture.meta")
    if [[ ${#targets[@]} -gt 0 ]]; then
        python3 .mcp/bin/cli.py delete --reason "ScreenSpaceTrace runner-owned disposable GPU fixtures" "${targets[@]}" || return 1
        unityctl asset refresh
    fi
}
trap cleanup EXIT
for filename in TraceChecks.compute TraceConsumers.shader; do
    mkdir -p "$(dirname "$fixture/$filename")"
    cp "$module/Validation~/RuntimeFixture/$filename" "$fixture/$filename"
done
unityctl asset refresh
for method in Main NegativeControl; do
    unityctl --json script execute "$module/Validation~/GpuChecks.cs" --method "$method" -t 120 > "$run/output/$method.json"
    python3 "$module/Validation~/check_result.py" "$run/output/$method.json" "$method"
done
printf 'GPU CLI reports: %s\n' "$run/output"
