import argparse
import hashlib
import json
from pathlib import Path
import re


MODULE = Path("Assets/Mine/Shaders/PostProcess/SSGI")
TOKEN = re.compile(r'//[^\n]*|/\*[\s\S]*?\*/|@"(?:""|[^"])*"|"(?:\\.|[^"\\])*"|\'(?:\\.|[^\'\\])*\'|[A-Za-z_][A-Za-z_0-9]*|[0-9]+|[^\s]')


def require(condition, message):
    if not condition:
        raise RuntimeError(message)


def code_tokens(source):
    return [match for match in TOKEN.finditer(source)
            if not match.group().startswith(('//', '/*', '"', "'", '@"'))]


def occurrences(tokens, expected):
    values = [token.group() for token in tokens]
    return [index for index in range(len(values) - len(expected) + 1)
            if values[index:index + len(expected)] == expected]


def half_high_source(source):
    tokens = code_tokens(source)
    names = [token for token in tokens if token.group() == "DiffuseGIFeature"]
    require(len(names) == 1, f"Expected exactly one DiffuseGIFeature identifier, found {len(names)}")
    require(len(occurrences(tokens, ["public", "class", "DiffuseGIFeature", ":", "ScriptableRendererFeature"])) == 1,
            "Expected the current production Feature declaration")
    branch = ["case", "SSGIQuality", ".", "High", ":", "tier", "=", "new", "Vector3Int", "(",
              "3", ",", "8", ",", "64", ")", ";", "break", ";"]
    matches = occurrences(tokens, branch)
    require(len(matches) == 1, f"Expected exactly one High (3,8,64) branch, found {len(matches)}")
    divisor = tokens[matches[0] + 10]
    replacements = [(names[0].start(), names[0].end(), "HalfHighDiffuseGIFeature"),
                    (divisor.start(), divisor.end(), "2")]
    candidate = source
    for start, end, replacement in sorted(replacements, reverse=True):
        candidate = candidate[:start] + replacement + candidate[end:]
    expected = ["HalfHighDiffuseGIFeature" if token.group() == "DiffuseGIFeature"
                else "2" if token.start() == divisor.start() else token.group() for token in tokens]
    require([token.group() for token in code_tokens(candidate)] == expected, "Unexpected baseline token change")
    return candidate, {"renamedIdentifiers": len(names), "highDivisorReplacements": len(matches)}


def combine_checks(camera, extension):
    for label, source in (("CameraChecks.cs", camera), ("HighBudgetChecks.cs", extension)):
        tokens = code_tokens(source)
        for declaration in (["public", "partial", "class", "Script"],
                            ["public", "static", "partial", "class", "SSGICameraChecks"]):
            require(len(occurrences(tokens, declaration)) == 1, f"{label}: partial declaration missing or duplicated")
    directives = re.compile(r"(?m)^[ \t]*(?:global[ \t]+)?using[ \t]+"
                            r"(?:(?:static[ \t]+)?(?:global::)?[A-Za-z_]\w*(?:\.[A-Za-z_]\w*)*[ \t]*"
                            r"|[A-Za-z_]\w*[ \t]*=[^;\n]+);")
    require(directives.search(extension) is None, "HighBudgetChecks.cs must contain no using directives")
    require("HighBudgetComparison" in [token.group() for token in code_tokens(extension)],
            "HighBudgetChecks.cs entry point missing")
    return camera.rstrip() + "\n\n" + extension.rstrip() + "\n"


def collect_sources(project):
    paths = {
        MODULE / "DiffuseGI/DiffuseGIFeature.cs", MODULE / "SSGIFeature.cs", MODULE / "SSGIControls.cs",
        MODULE / "SSGITemporalFilter.cs", MODULE / "DiffuseGI/DiffuseGI.shader", MODULE / "SSGITemporal.shader",
        MODULE / "Validation~/CameraChecks.cs", MODULE / "Validation~/HighBudgetChecks.cs",
        Path("Packages/manifest.json"), Path("Packages/packages-lock.json"),
    }
    pending = list(paths)
    contents = {}
    while pending:
        relative = pending.pop()
        data = (project / relative).read_bytes()
        contents[relative] = data
        if relative.suffix not in (".shader", ".hlsl", ".compute"):
            continue
        for include in re.findall(r'^\s*#include(?:_with_pragmas)?\s+"([^"]+)"', data.decode("utf-8-sig"), re.MULTILINE):
            if include.startswith("Packages/"):
                continue
            dependency = Path(include) if include.startswith("Assets/") else relative.parent / include
            dependency = (project / dependency).resolve().relative_to(project)
            require(dependency.parts[0] == "Assets", f"Unexpected local include: {include}")
            if dependency not in paths:
                paths.add(dependency)
                pending.append(dependency)
    return contents


def main():
    parser = argparse.ArgumentParser(description="Prepare Diffuse High comparison without importing assets or running Unity")
    parser.add_argument("destination", type=Path)
    parser.add_argument("--project", type=Path, default=Path.cwd())
    arguments = parser.parse_args()
    project = arguments.project.resolve()
    destination = arguments.destination.resolve()
    require(destination.is_relative_to(project / "tmp/sessions"), "Preparation output must be under project tmp/sessions")
    require(not destination.exists() and not destination.is_symlink(), f"Refusing existing preparation output: {destination}")
    contents = collect_sources(project)
    feature, counts = half_high_source(contents[MODULE / "DiffuseGI/DiffuseGIFeature.cs"].decode("utf-8"))
    combined = combine_checks(contents[MODULE / "Validation~/CameraChecks.cs"].decode("utf-8-sig"),
                              contents[MODULE / "Validation~/HighBudgetChecks.cs"].decode("utf-8-sig"))
    generated = {"HalfHighDiffuseGIFeature.cs": feature.encode("utf-8"),
                 "HighBudgetComparison.cs": combined.encode("utf-8")}
    for relative, data in contents.items():
        require((project / relative).read_bytes() == data, f"Concurrent source change during preparation: {relative}")
    manifest = {
        "contract": "isolated-diffuse-high-half-versus-third-v1",
        "entryPoint": "HighBudgetComparison", "quality": "High", "temporalBlend": 0.5,
        "variants": {"HalfHigh": [2, 8, 64], "ThirdHigh": [3, 8, 64]},
        "tokenChanges": counts,
        "sourceSha256": {str(relative): hashlib.sha256(data).hexdigest() for relative, data in sorted(contents.items())},
        "generatedSha256": {name: hashlib.sha256(data).hexdigest() for name, data in generated.items()},
    }
    destination.mkdir(parents=True, exist_ok=False)
    for filename, data in generated.items():
        with (destination / filename).open("xb") as output:
            output.write(data)
    with (destination / "inputs.json").open("x") as output:
        output.write(json.dumps(manifest, indent=2) + "\n")
    print(json.dumps({"input": str(destination), "manifest": str(destination / "inputs.json"), **counts}))


if __name__ == "__main__":
    main()
