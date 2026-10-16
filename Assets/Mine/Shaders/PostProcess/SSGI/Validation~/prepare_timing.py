import hashlib
import json
import sys
from pathlib import Path

destination = Path(sys.argv[1])
destination.mkdir(parents=True, exist_ok=False)
module = Path("Assets/Mine/Shaders/PostProcess/SSGI")
paths = [
    Path("Assets/Mine/Special/HLSL/RayStepFunction.hlsl"),
    Path("Assets/Mine/Special/HLSL/ScreenSpaceTraceFunction.hlsl"),
    module / "ScreenSpaceTrace.hlsl",
    module / "DiffuseGI/DiffuseGI.shader",
    module / "DiffuseGI/DiffuseGIFeature.cs",
    module / "SpecularGI/SpecularGI.shader",
    module / "SpecularGI/SpecularGIFeature.cs",
]
manifest = {
    "contract": "SSR-framework incremental DDA; no retired-grid control shaders",
    "quality": "Medium",
    "entryPoint": sys.argv[2],
    "expectedDiffuseSamples": 48,
    "expectedSpecularSamples": 64,
    "sourceSha256": {str(path): hashlib.sha256(path.read_bytes()).hexdigest() for path in paths},
}
(destination / "inputs.json").write_text(json.dumps(manifest, indent=2) + "\n")
