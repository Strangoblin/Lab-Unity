#!/usr/bin/env bash
set -euo pipefail
if [[ $# -ne 1 || ${1:-} != --isolated-diffuse ]]; then
    printf '%s\n' 'Usage: run_high_budget.sh --isolated-diffuse'
    exit 2
fi
script_directory="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
project="$(git -C "$script_directory" rev-parse --show-toplevel)"
cd "$project"
module="Assets/Mine/Shaders/PostProcess/SSGI"
fixture="$module/HighBudgetFixture"
for target in "$fixture" "$fixture.meta"; do
    if [[ -e "$target" || -L "$target" ]]; then
        printf 'Refusing existing fixture: %s\n' "$target"
        exit 1
    fi
done
run="$(mktemp -d "$project/tmp/sessions/$(date +%F)-diffuse-high-budget-XXXXXXXX")"
mkdir -p "$run/output"
python3 "$script_directory/prepare_high_budget.py" "$run/input" --project "$project"
fixture_owned=0
cleanup() {
    local result=$?
    trap - EXIT INT TERM
    if [[ "$fixture_owned" == 1 ]]; then
        if ! python3 - "$fixture" "$run/input/HalfHighDiffuseGIFeature.cs" <<'PY'
from pathlib import Path
import sys

fixture = Path(sys.argv[1])
baseline = Path(sys.argv[2])
if fixture.is_symlink() or Path(str(fixture) + ".meta").is_symlink():
    raise SystemExit("Refusing cleanup of a replaced fixture symlink")
if fixture.exists():
    allowed = {"HalfHighDiffuseGIFeature.cs", "HalfHighDiffuseGIFeature.cs.meta"}
    for child in fixture.iterdir():
        if child.name not in allowed or child.is_symlink() or not child.is_file():
            raise SystemExit("Refusing cleanup of concurrently changed fixture: " + str(child))
    source = fixture / "HalfHighDiffuseGIFeature.cs"
    if source.exists() and source.read_bytes() != baseline.read_bytes():
        raise SystemExit("Refusing cleanup of concurrently edited baseline")
PY
        then
            result=1
        else
            local targets=()
            [[ ! -e "$fixture" ]] || targets+=("$fixture")
            [[ ! -e "$fixture.meta" ]] || targets+=("$fixture.meta")
            if [[ ${#targets[@]} -gt 0 ]]; then
                python3 .mcp/validation/gate_cli.py delete --recursive \
                    --reason 'SSGI High budget runner-owned disposable baseline fixture' "${targets[@]}" || result=1
                unityctl asset refresh || result=1
            fi
        fi
    fi
    printf 'High budget CLI report: %s\n' "$run/output/high-budget-cli.json"
    exit "$result"
}
trap cleanup EXIT
trap 'exit 130' INT
trap 'exit 143' TERM
for target in "$fixture" "$fixture.meta"; do
    if [[ -e "$target" || -L "$target" ]]; then
        printf 'Refusing fixture created during preparation: %s\n' "$target"
        exit 1
    fi
done
fixture_owned=1
python3 .mcp/validation/gate_cli.py write --path "$fixture/HalfHighDiffuseGIFeature.cs" \
    --from "$run/input/HalfHighDiffuseGIFeature.cs"
if ! cmp -s "$run/input/HalfHighDiffuseGIFeature.cs" "$fixture/HalfHighDiffuseGIFeature.cs"; then
    printf '%s\n' 'Gate did not install the prepared baseline exactly'
    exit 1
fi
unityctl asset refresh
unityctl --json script execute "$run/input/HighBudgetComparison.cs" \
    --method HighBudgetComparison -t 240 > "$run/output/high-budget-cli.json"
python3 "$script_directory/check_high_budget.py" "$run/output/high-budget-cli.json"
