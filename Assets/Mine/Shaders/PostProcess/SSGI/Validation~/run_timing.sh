#!/usr/bin/env bash
set -euo pipefail
project="$(git rev-parse --show-toplevel)"
cd "$project"
module="Assets/Mine/Shaders/PostProcess/SSGI"
validation="$module/Validation~"
method="Timing"
if [[ $# -gt 1 ]]; then
    printf '%s\n' "Usage: run_timing.sh [--isolated-ssgi]"
    exit 2
fi
case "${1:-}" in
    "") ;;
    --isolated-ssgi) method="TimingIsolatedSSGI" ;;
    *) printf '%s\n' "Usage: run_timing.sh [--isolated-ssgi]"; exit 2 ;;
esac
fixture="$module/TimingRuntimeFixture"
if [[ -e "$fixture" || -e "$fixture.meta" ]]; then
    printf '%s\n' "Refusing to overwrite existing timing fixtures: $fixture"
    exit 1
fi
run="$(mktemp -d "$project/tmp/sessions/$(date +%F)-ssgi-timing-run-XXXXXXXX")"
mkdir -p "$run/output"
python3 "$validation/prepare_timing.py" "$run/input" "$method"
unityctl asset refresh
unityctl --json script execute "$validation/CameraChecks.cs" --method "$method" -t 240 > "$run/output/timing-cli.json"
python3 "$validation/check_timing.py" "$run/output/timing-cli.json" "$method"
printf 'Timing CLI report: %s\n' "$run/output/timing-cli.json"
