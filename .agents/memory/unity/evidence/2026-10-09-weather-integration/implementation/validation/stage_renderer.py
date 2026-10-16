from pathlib import Path
import difflib
import re
import subprocess

path = "Assets/Settings/PC_Renderer.asset"
current = Path(path).read_text()
head = subprocess.check_output(["git", "show", "HEAD:" + path], text=True)

def block(text, name):
    return next(part for part in re.split(r"(?=^--- !u!)", text, flags=re.M)
                if "  m_Name: " + name + "\n" in part)

weather = block(current, "Weather")
weather = weather.replace("  m_Active: 0\n", "  m_Active: 1\n", 1)
weather = re.sub(r"    profile: .*", "    profile: {fileID: 11400000, guid: bd96443d72f254b91b8738a1adc656cc, type: 2}", weather)
weather_id = re.search(r"&(-?\d+)", weather).group(1)
debug = block(head, "DebugOutput")
retired = debug.replace("  m_Active: 1\n", "  m_Active: 0\n", 1)
retired = re.sub(r"    debugMaterial: .*", "    debugMaterial: {fileID: 0}", retired)
retired = re.sub(r"    debugShader: .*", "    debugShader: {fileID: 0}", retired)
retired = re.sub(r"    additionalPassIndex: .*", "    additionalPassIndex: -1", retired)
retired = re.sub(r"    requireDepth: .*", "    requireDepth: 0", retired)
retired = re.sub(r"    debug: .*", "    debug: 0", retired)
desired = head.replace(debug, weather + retired, 1)
start = desired.index("  m_RendererFeatures:")
end = desired.index("  m_RendererFeatureMap:", start)
desired = desired[:end] + "  - {fileID: " + weather_id + "}\n" + desired[end:]
mapping = next(line for line in current.splitlines() if line.startswith("  m_RendererFeatureMap:"))
desired = re.sub(r"^  m_RendererFeatureMap: .*", mapping, desired, flags=re.M)
patch = "".join(difflib.unified_diff(head.splitlines(True), desired.splitlines(True),
                                   fromfile="a/" + path, tofile="b/" + path))
Path(".codex/tmp/weather-implementation/validation/renderer-weather-only.patch").write_text(patch)
print(patch)
