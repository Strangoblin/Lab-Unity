#!/usr/bin/env python3
"""Final-stage verification for authorized Hash variant consolidation.

Checks preserved sequences separately from intentional sequence changes.
Outputs default to tmp/sessions/<run-id>/output; fixtures remain read-only.
"""
from __future__ import annotations

import itertools
import hashlib
import json
from pathlib import Path
import random
import re
import sys

sys.dont_write_bytecode = True
import verify_hash as common

HERE = common.HERE
ROOT = OUTPUT = None
RAIN = "Assets/Mine/Shaders/Render/RainDrops/RainDrop.hlsl"
SPEC = "Assets/Mine/Shaders/PostProcess/SSGI/SpecularGI/SpecularGISampling.hlsl"
GOGH = "Assets/Mine/Shaders/Render/VanGogh/TheStarryNightSDF.hlsl"
SSL = "Assets/Mine/Shaders/PostProcess/SSL/SSL.shader"
REPORT: dict = {"scope": "Current canonical Hash algorithms and caller contracts, with historical whole-file preservation checked only where its frozen snapshot still applies. Intentional sequence changes are not old-output equality claims."}


def include_order():
    event = re.compile(r'#\s*include\s+"([^"]+)"|\b(Hash_(?:Sine21|Frac22|FracGrid22|Sine14|UInt2SeedStream01))\s*\(')
    def expand(path, stack):
        if path in stack:
            raise AssertionError(f"Circular Assets include: {path}")
        source = common.no_comments((ROOT / path).read_text())
        found=[]
        for m in event.finditer(source):
            if m.group(1):
                target=m.group(1)
                if target.startswith("Assets/"):
                    candidate=ROOT/target
                    if not candidate.is_file():
                        raise AssertionError(f"Missing Assets include: {target} in {path}")
                else:
                    # HLSL accepts private file-relative includes such as
                    # RainDrop.hlsl and PCSSFunction.hlsl. Package/builtin
                    # includes have no local Assets candidate and are external.
                    candidate=(ROOT/path).parent/target
                if candidate.is_file() and (ROOT/"Assets") in candidate.resolve().parents:
                    relative=candidate.resolve().relative_to(ROOT).as_posix()
                    found.extend(expand(relative,stack+[path]))
            else:
                found.append((path,m.group(2)))
        return found
    checked,excluded=[],[]
    for path in sorted((ROOT/"Assets/Mine").rglob("*")):
        if path.suffix not in (".shader",".compute"):
            continue
        rel=path.relative_to(ROOT).as_posix()
        if any(p.startswith(".") or p.endswith("~") for p in path.relative_to(ROOT/"Assets/Mine").parts):
            excluded.append(rel)
            continue
        events=expand(rel,[])
        if events:
            if events[0][0]!=common.HASH:
                raise AssertionError(f"Hash use before library declaration: {rel} {events[0]}")
            checked.append(rel)
    REPORT["actual_shader_entry_include_order"]=checked
    REPORT["include_audit_hidden_entry_exclusions"]=excluded
    print(f"PASS final include order: {len(checked)} imported Shader/Compute entry files, including FracGrid22 calls")


def hlsl_compile():
    include=f'#include "{common.HASH}"\n#include "{common.HASH}"\n'
    frag,comp=OUTPUT/"hash_final_float.frag.hlsl",OUTPUT/"hash_final_uint.comp.hlsl"
    frag.write_text(include+"float4 main(float2 uv : TEXCOORD0) : SV_Target { float2 pair = Hash_Frac22(uv); float2 grid = Hash_FracGrid22(uv); return float4(Hash_Sine21(uv),pair.x+grid.x,pair.y+grid.y,Hash_Sine14(uv.x).w); }\n")
    comp.write_text(include+"RWStructuredBuffer<float> Results : register(u0);\n[numthreads(1,1,1)] void main(uint3 id : SV_DispatchThreadID) { Results[id.x] = Hash_UInt2SeedStream01(id.xy,id.z,id.x); }\n")
    results=[]
    for target in (20,35,50):
        args=[common.GLSLANG,"-D",f"-DSHADER_TARGET={target}",f"-I{ROOT}","-e","main"]
        pre=common.run(args+["-E","-S","frag",frag])
        exposed=bool(re.search(r"\bfloat\s+Hash_UInt2SeedStream01\s*\(",pre.stdout))
        if exposed!=(target>=35):
            raise AssertionError(f"Wrong uint API exposure at macro {target}")
        common.run(args+["-V","--spirv-val","-S","frag","-o",OUTPUT/f"final_float_{target}.spv",frag])
        uint_result=common.run(args+["-V","--spirv-val","-S","comp","-o",OUTPUT/f"final_uint_{target}.spv",comp],expect_ok=target>=35)
        if target<35 and "Hash_UInt2SeedStream01" not in uint_result.stdout+uint_result.stderr:
            raise AssertionError("Low-macro uint negative test failed for unrelated reason")
        results.append({"SHADER_TARGET":target,"float_fragment":"compiled + SPIR-V validated, all four float APIs called","uint_compute":"compiled + SPIR-V validated" if target>=35 else "expected compile rejection; uint Hash API hidden"})
    REPORT["native_hlsl_spirv"]=results
    print("PASS final native HLSL -> SPIR-V syntax/macro branches: four float APIs at SHADER_TARGET 20/35/50, uint at 35/50; macro20 uint API hidden/call rejected")


def hash_statements(source_tokens):
    """Exact tokens of declarations/calls and their containing statements."""
    names = {"Hash_Sine21", "Hash_Frac22", "Hash_FracGrid22", "Hash_Sine14",
             "Hash_UInt2SeedStream01", "Blur_Hash01", "Weather_Random",
             "Random1D", "Hash21", "Hash1", "Hash2", "Hash2D",
             "SpecularGI_Hash22", "Hash", "hash21"}
    result, segment = [], []
    for token in source_tokens + [";"]:
        if token in (";", "{", "}"):
            if any(item in names and segment[i + 1:i + 2] == ["("]
                   for i, item in enumerate(segment)):
                result.append(segment)
            segment = []
        else:
            segment.append(token)
    return result


def source_preservation(before, current, *, strict=False):
    known_later_drift = {SSL, common.SSO,
                        "Assets/Mine/Shaders/PostProcess/Kuwahara/Kuwahara.shader",
                        "Assets/Mine/Shaders/PostProcess/DDOF/DDOF.shader"}
    allowed = {
        common.HASH: [],
        RAIN: [("Hash2D", "float2 Hash2D(float2 p) { return Hash_FracGrid22(p); }")],
        SPEC: [("SpecularGI_Hash22", "float2 SpecularGI_Hash22(float2 value) { return Hash_FracGrid22(value); }")],
        SSL: [("hash21", "float hash21(float2 p) { return Hash_Sine21(p); }")],
        GOGH: [("Hash", "float Hash(float x) { return Hash_Sine21(float2(x, 0.0)) - 0.5; }"), ("Hash2", "float Hash2(float x, float y) { return Hash_Sine21(float2(x, y)) - 0.5; }")],
    }
    checked, drift, contracts, retired, consumer_functions = [], [], [], [], []
    for path, old in before.items():
        # These existing consumers changed around the Hash code after the
        # migration. Preserve their complete active Hash consumer functions.
        names = {common.SSO: ("Hash21", "OffsetUV", "ShadowLine"), SSL: ("SSL",)}.get(path, ())
        for name in names:
            expected_function = common.tokens(common.function(old, name))
            actual_function = common.tokens(common.function(current[path], name))
            if expected_function != actual_function:
                raise AssertionError(f"Current complete Hash consumer function differs: {path} {name}")
            consumer_functions.append({"file": path, "function": name})
        direct_ssl = path == SSL and not re.search(r"\bfloat\s+hash21\s*\(", common.no_comments(current[path]))
        if direct_ssl:
            # SSL later removed the wrapper and changed non-Hash shader layout.
            # Keep the immutable migration snapshot; audit the complete radial
            # consumer function instead of asserting whole-file preservation.
            expected_entry = common.function(old, "Frag_RBR2D")
            expected_entry = common.renamed(expected_entry, "hash21", "Hash_Sine21")
            actual_entry = common.function(current[path], "Frag_RBR2D")
            if common.tokens(expected_entry) != common.tokens(actual_entry):
                raise AssertionError("SSL radial caller math changed beyond direct Hash_Sine21 mapping")
        expected = common.tokens(old)
        for name, target in allowed.get(path, []):
            old_function = common.function(old, name)
            if direct_ssl and name == "hash21":
                expected = common.replace_tokens(expected, old_function, "")
                expected = ["Hash_Sine21" if token == "hash21" else token for token in expected]
                continue
            if target is None:
                # Only this named function body may change. Its implementation
                # is compiled and compared with the saved SpecGI standard below.
                target = common.function(current[path], name)
                if common.tokens(old_function.split("{", 1)[0]) != common.tokens(target.split("{", 1)[0]):
                    raise AssertionError(f"Unexpected public API signature change: {path} {name}")
            expected = common.replace_tokens(expected, old_function, target)
        actual = common.tokens(current[path])
        if path == common.HASH:
            grid = common.function(current[path], "Hash_FracGrid22")
            if common.tokens(grid.split("{",1)[0]) != common.tokens("float2 Hash_FracGrid22(float2 key)"):
                raise AssertionError("Unexpected Grid22 signature")
            actual = common.replace_tokens(actual, grid, "")
        historical_expected = expected
        if path == common.SSO:
            unused = "float jitter = Hash21(uv).x;"
            old_entry = common.tokens(common.function(old, "Frag_SSO"))
            new_entry = common.tokens(common.function(current[path], "Frag_SSO"))
            if old_entry.count("jitter") == 1 and new_entry.count("jitter") == 0:
                expected = common.replace_tokens(expected, unused, "")
                retired.append({"file": path, "function": "Frag_SSO", "statement": unused,
                                "reason": "Only occurrence of local jitter in the historical entry function was its declaration; current entry contains none. Remaining active Hash callers are checked exactly."})
        expected_contract, actual_contract = hash_statements(expected), hash_statements(actual)
        if path == common.HASH and expected != actual:
            raise AssertionError("Current canonical Hash library differs from the frozen algorithms beyond the listed Grid22 addition")
        if expected_contract != actual_contract:
            raise AssertionError(f"Hash-bearing statements/caller math changed beyond listed mappings: {path}")
        if expected_contract:
            contracts.append({"file": path, "hash_statement_count": len(expected_contract),
                              "sha256": hashlib.sha256(json.dumps(expected_contract).encode()).hexdigest()})
        if historical_expected != actual:
            if path not in known_later_drift:
                raise AssertionError(f"Unreviewed historical whole-file drift; review current Hash scope before accepting: {path}")
            item = {"file": path,
                    "historical_source_sha256": hashlib.sha256(old.encode()).hexdigest(),
                    "current_source_sha256": hashlib.sha256(current[path].encode()).hexdigest(),
                    "reason": "Reviewed later source edits; historical whole-file preservation no longer applies. Current Hash-bearing statements, when present, remain exact after listed mappings."}
            if direct_ssl:
                item["current_contract"] = "Complete Frag_RBR2D tokens match the frozen baseline after hash21 -> Hash_Sine21: UV + frac(time), signed remap, dTuv amplitude, 24 samples and all radial math remain unchanged."
            drift.append(item)
        else:
            checked.append(path)
    REPORT["source_preservation"] = {"files": checked, "allowed_function_replacements": {p: [name for name, _ in targets] for p, targets in allowed.items()}}
    REPORT["source_preservation"]["allowed_added_function"] = "Hash_FracGrid22"
    REPORT["source_preservation"]["historical_baseline_drift"] = drift
    REPORT["current_hash_statement_contracts"] = contracts
    REPORT["historical_unused_hash_statement_removals"] = retired
    REPORT["current_complete_hash_consumer_functions"] = consumer_functions
    print(f"PASS final source preservation: {len(checked)} complete files; only listed function replacements/new Hash includes, caller inputs and other code tokens intact")
    for item in drift:
        print(f"HISTORICAL snapshot drift excluded from whole-file preservation: {item['file']}")
    print(f"PASS current Hash-bearing statement contracts: {len(contracts)} files")
    if strict and drift:
        raise AssertionError("Historical whole-file snapshot drift: " + ", ".join(item["file"] for item in drift))


def bridge():
    code = common.BRIDGE
    code = code.replace("typedef float float4", "typedef float float3 __attribute__((ext_vector_type(3)));\ntypedef float float4")
    code = code.replace("#define float4", "static float3 hlsl_make_float3(float3 x) { return x; }\nstatic float3 hlsl_make_float3(float x, float y, float z) { return (float3){x, y, z}; }\n#define float3(...) hlsl_make_float3(__VA_ARGS__)\n#define float4")
    code += r'''
static float3 frac(float3 x) { return float3(frac(x.x), frac(x.y), frac(x.z)); }
static float dot(float3 a, float3 b) { return a.x * b.x + a.y * b.y + a.z * b.z; }
static float hlsl_sin(float x) { return sinf(x); }
static float4 hlsl_sin(float4 x) { return sin(x); }
static int range_checks = 0, changed_components = 0, intentional_components = 0;
static void range(float x, float lo, float hi, const char* label) {
    ++range_checks;
    if (!(x >= lo && x < hi)) {
        if (mismatches < 5) std::printf("RANGE MISMATCH %s %.9g [%.9g,%.9g)\n",label,x,lo,hi);
        ++mismatches;
    }
}
static void range(float2 x, float lo, float hi, const char* label) { range(x.x,lo,hi,label); range(x.y,lo,hi,label); }
static void range(float4 x, float lo, float hi, const char* label) { range(x.x,lo,hi,label); range(x.y,lo,hi,label); range(x.z,lo,hi,label); range(x.w,lo,hi,label); }
static void changed(float a,float b) { ++intentional_components; changed_components += bits(a)!=bits(b); }
static void changed(float2 a,float2 b) { changed(a.x,b.x); changed(a.y,b.y); }
'''
    return code


def source(original, before, current, library):
    bodies = [common.function(library, name) for name in ("Hash_Sine21", "Hash_Frac22", "Hash_FracGrid22", "Hash_Sine14", "Hash_UInt2SeedStream01")]
    def pair(mapping, path, name, label, include_old=True):
        if include_old:
            bodies.append(common.renamed(common.function(mapping[path], name), name, "old_" + label))
        bodies.append(common.renamed(common.function(current[path], name), name, "new_" + label))
    for path, name, label in [(common.PCSS, "Random1D", "pcss"), (common.WEATHER, "Weather_Random", "weather"), (common.BLUR, "Blur_Hash01", "blur")]:
        pair(original, path, name, label)
    # Existing Frac22/Fin/SSO keep their old sequences. Original Rain/SSL/Gogh
    # bodies are used for observed-change counts, not correctness expectations.
    for path, name, label in [(common.SSO, "Hash21", "sso"), (common.FUR, "Hash1", "fur1"), (common.FUR, "Hash2", "fur2")]:
        pair(original, path, name, label)
    for path, name, label in [(RAIN, "Hash2D", "rain"), (SPEC, "SpecularGI_Hash22", "spec"), (GOGH, "Hash", "gogh1"), (GOGH, "Hash2", "gogh2")]:
        pair(before, path, name, label)
    bodies.append(common.renamed(common.function(before[SSL], "hash21"), "hash21", "old_sslradial"))
    if re.search(r"\bfloat\s+hash21\s*\(", common.no_comments(current[SSL])):
        bodies.append(common.renamed(common.function(current[SSL], "hash21"), "hash21", "new_sslradial"))
    else:
        # source_preservation has independently checked the complete actual
        # radial entry function against this direct-call mapping before use.
        bodies.append("float new_sslradial(float2 key) { return Hash_Sine21(key); }")
    # Frozen standard is taken from the pre-consolidation real SpecGI body.
    bodies.append(common.renamed(common.function(before[SPEC], "SpecularGI_Hash22"), "SpecularGI_Hash22", "reference_spec"))
    for i, path in enumerate(common.INLINE):
        for prefix, mapping in [("old", original), ("new", current)]:
            bodies.append(f"float {prefix}_inline{i}(float2 uv) {{ return {common.inline_jitter(mapping[path])}; }}")
    code = bridge() + "\n".join(common.cpp(body) for body in bodies)
    rng = random.Random(10102026)
    keys = [(0, 0), (-0.0, 0), (1, 1), (-1, -1), (0.5, -0.75), (-123.125, 456.5), (100000, -200000), (1e7, -1e7), (1e20, -1e20)]
    keys += [(rng.uniform(-8192, 8192), rng.uniform(-8192, 8192)) for _ in range(512)]
    # Fixed examples of the actual key categories: UV, UV+neighbour offsets,
    # uv*31+seed+17, raindrop floor cell and (column,137), pixel+frame phase,
    # VanGogh signed row/column and ring/sector IDs, SSL UV+frac(time).
    domain_keys = {
        "normalized_uv": [(x/16,y/16) for x in range(17) for y in range(17)],
        "sso_uv_neighbor": [(x/16+dx,y/16+dy) for x in range(17) for y in range(17) for dx,dy in ((0,0),(1/1920,0),(-1/1920,1/1080))],
        "fur_uv_seed": [(x/8*31+seed+17,y/8*31+seed+17) for x in range(9) for y in range(9) for seed in (0,0.25,1000)],
        "raindrop_cells_columns": [(x,y) for x in range(-64,65) for y in (-137,-1,0,1,137)] + [(x,137) for x in range(-64,65)],
        "specgi_pixel_frame": [(x+frame*0.754877,y+frame*0.569840) for x,y in ((0,0),(1920,1080),(4096,2160),(8192,4320)) for frame in (0,1,7,255,65535)],
        "vangogh_row_column_ring_sector": [(x,y) for x in (-4096,-300,-40,-1,0,1,40,250,300,4096) for y in (-40,-1,0,1,40,250)],
        "ssl_uv_time_phase": [(x/8+phase,y/8+phase) for x in range(9) for y in range(9) for phase in (0,0.25,0.999)],
    }
    for values in domain_keys.values():
        keys.extend(values)
    indices = [0, -1, 1, -2, 0.25, -0.75, 100000, -100000, 1e7, 1e20]
    indices += [rng.uniform(-10000, 10000) for _ in range(128)]
    edges = [0, 1, 0x7fffffff, 0x80000000, 0xfffffffe, 0xffffffff]
    integer_inputs = list(itertools.product(edges, repeat=4))
    integer_inputs += [tuple(rng.getrandbits(32) for _ in range(4)) for _ in range(1024)]
    code += "\nint main() {\nfloat2 keys[] = {" + ",".join(f"float2({common.literal(x)},{common.literal(y)})" for x,y in keys) + "};\n"
    code += r'''
for (float2 key : keys) {
    // These algorithms retain the old formula/sequence.
    equal(old_pcss(key),new_pcss(key),"preserved A PCSS");
    equal(old_inline0(key),new_inline0(key),"preserved A SSSM");
    equal(old_inline1(key),new_inline1(key),"preserved A SSC");
    equal(old_inline2(key),new_inline2(key),"preserved A SSL");
    equal(old_fur1(key),new_fur1(key),"preserved Frac22 Fur scalar");
    equal(old_fur2(key),new_fur2(key),"preserved Frac22 Fur vector");
    equal(old_sso(key),new_sso(key),"preserved Frac22 SSO signed remap");
    equal(old_spec(key),new_spec(key),"preserved SpecGI standard");
    equal(old_gogh1(key.x),new_gogh1(key.x),"preserved VanGogh scalar formula");
    // Canonical comparisons use the frozen actual SpecGI implementation,
    // independent of the function being tested.
    float2 expected = reference_spec(key);
    equal(expected,Hash_FracGrid22(key),"canonical FracGrid22 vs original SpecGI standard");
    equal(expected,new_spec(key),"canonical SpecGI wrapper");
    equal(expected,new_rain(key),"canonical Rain wrapper");
    equal(Hash_Sine21(key),new_sslradial(key),"canonical SSL radial Sine21");
    equal(Hash_Sine21(key)-0.5f,new_gogh2(key.x,key.y),"canonical VanGogh 2D centered Sine21");
    range(Hash_Sine21(key),0.0f,1.0f,"Sine21");
    range(Hash_Frac22(key),0.0f,1.0f,"Frac22");
    range(Hash_FracGrid22(key),0.0f,1.0f,"FracGrid22");
    range(new_rain(key),0.0f,1.0f,"Rain");
    range(new_spec(key),0.0f,1.0f,"SpecGI");
    range(new_fur1(key),0.0f,1.0f,"Fur scalar");
    range(new_fur2(key),0.0f,1.0f,"Fur vector");
    range(new_sso(key),-1.0f,1.0f,"SSO signed remap");
    range(new_sslradial(key),0.0f,1.0f,"SSL radial");
    range(new_gogh1(key.x),-0.5f,0.5f,"VanGogh scalar");
    range(new_gogh2(key.x,key.y),-0.5f,0.5f,"VanGogh 2D");
    changed(old_rain(key),new_rain(key));
    changed(old_sslradial(key),new_sslradial(key));
    changed(old_gogh2(key.x,key.y),new_gogh2(key.x,key.y));
}
'''
    code += "float indices[] = {" + ",".join(common.literal(x) for x in indices) + "};\nfor(float i:indices) { equal(old_weather(i),new_weather(i),\"preserved Weather index + 1\"); range(new_weather(i),0.0f,1.0f,\"Weather\"); }\n"
    code += "uint data[][4] = {" + ",".join("{" + ",".join(str(n) + "u" for n in row) + "}" for row in integer_inputs) + "};\n"
    code += r'''
for (auto& row:data) {
    float old = old_blur(uint2(row[0],row[1]),row[2],row[3]);
    float now = new_blur(uint2(row[0],row[1]),row[2],row[3]);
    equal(old,now,"preserved Blur uint32 high24");
    range(now,0.0f,1.0f,"Blur high24");
    if (floorf(now*16777216.0f)!=now*16777216.0f) ++mismatches;
}
std::printf("CPU comparisons=%d bit_mismatches=%d range_checks=%d observed_intentional_changes=%d/%d\n",checks,mismatches,range_checks,changed_components,intentional_components);
return mismatches ? 1 : 0;
}
'''
    return code, {"float2_keys": len(keys), "representative_input_categories": {name:len(values) for name,values in domain_keys.items()}, "weather_indices":len(indices), "uint32_tuples":len(integer_inputs)}


def cpu_compare(original, before, current, library):
    code, counts = source(original,before,current,library)
    cpp, exe = OUTPUT / "hash_final_cpu.cpp", OUTPUT / "hash_final_cpu"
    cpp.write_text(code)
    compiler, stdlib = common.cpp_compiler()
    common.run(compiler+[cpp,"-o",exe,stdlib])
    result = common.run([exe])
    REPORT["cpu"] = {**counts,"result":result.stdout.strip(),"preserved_sequences":["A/PCSS/SSSM/SSC/SSL primary", "Frac22/Fur scalar/vector/SSO signed", "Weather index + 1", "Blur uint32 high24", "SpecGI", "VanGogh scalar"],"intentional_sequences":["RainDrop", "SSL radial", "VanGogh 2D"],"canonical_reference":"actual pre-consolidation SpecularGI_Hash22 body from final-stage-before.json"}
    print("PASS "+result.stdout.strip())
    # Independent negative controls for both float algorithm families.
    controls=[]
    for original_constant,changed_constant,label in [("12.9898","13.9898","Sine21"),("0.1030","0.1130","FracGrid22")]:
        if library.count(original_constant)!=1:
            raise AssertionError(f"Fault-control constant not unique: {original_constant}")
        mutant=library.replace(original_constant,changed_constant)
        mutated,_=source(original,before,current,mutant)
        mutated_cpp,mutated_exe=OUTPUT/f"hash_final_fault_{label}.cpp",OUTPUT/f"hash_final_fault_{label}"
        mutated_cpp.write_text(mutated)
        common.run(compiler+[mutated_cpp,"-o",mutated_exe,stdlib])
        failed=common.run([mutated_exe],expect_ok=False)
        match=re.search(r"bit_mismatches=(\d+)",failed.stdout)
        if not match or int(match.group(1))==0:
            raise AssertionError(f"Fault control didn't detect changes: {label}")
        controls.append({"algorithm":label,"generated_copy_change":f"{original_constant} -> {changed_constant}","bit_mismatches":int(match.group(1))})
    REPORT["fault_controls"]=controls
    print("PASS final fault controls: "+", ".join(f"{x['algorithm']} {x['bit_mismatches']} mismatches" for x in controls)+"; Assets untouched")


def main(argv=None):
    global ROOT, OUTPUT
    args = common.configure(argv)
    ROOT, OUTPUT = common.ROOT, common.OUTPUT
    original=json.loads((common.FIXTURES/"before.json").read_text())
    before=json.loads((common.FIXTURES/"final-stage-before.json").read_text())
    REPORT["baselines"] = {"path_base": "validation script directory", "files": [
        {"path": "fixtures/" + name, "sha256": hashlib.sha256((common.FIXTURES/name).read_bytes()).hexdigest()}
        for name in ("before.json", "final-stage-before.json")
    ]}
    REPORT["compiler_versions"] = {
        "clang": common.run([common.CLANG, "--version"]).stdout.splitlines()[0],
        "glslang": common.run([common.GLSLANG, "--version"]).stdout.splitlines()[0],
    }
    current={path:(ROOT/path).read_text() for path in set(before)|set(original)}
    library=(ROOT/common.HASH).read_text()
    source_preservation(before,current,strict=args.strict_migration_snapshot)
    include_order()
    hlsl_compile()
    cpu_compare(original,before,current,library)
    REPORT["status"]="PASS"
    REPORT["evidence_limits"]=["No Unity Editor, Metal compiler, GPU execution or image comparison.","SHADER_TARGET 20/35/50 checks macro exposure and native HLSL syntax, not Unity shader-model hardware capability.","CPU same-host float equality cannot establish GPU cross-platform determinism. Range checks cover sampled fixed/domain keys, not a proof of distribution quality.","Sequence-change counts document sampled differences; no claim of old random output preservation for authorized changed functions.","Representatives cover input categories with fixed plausible values; actual material ranges and runtime frame/camera values can vary."]
    (OUTPUT/"final-validation-report.json").write_text(json.dumps(REPORT,indent=2,ensure_ascii=False)+"\n")
    print("PASS final Hash consolidation. Original equivalent interfaces and authorized new canonical/range contracts checked separately; no GPU execution.")


def cli(argv=None):
    try:
        main(argv)
    except (AssertionError, OSError, ValueError) as e:
        REPORT.update(status="FAIL", error=str(e))
        if common.OUTPUT is not None and common.OUTPUT.is_dir():
            (common.OUTPUT/"final-validation-report.json").write_text(json.dumps(REPORT,indent=2,ensure_ascii=False)+"\n")
        print(f"FAIL {e}",file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(cli())
