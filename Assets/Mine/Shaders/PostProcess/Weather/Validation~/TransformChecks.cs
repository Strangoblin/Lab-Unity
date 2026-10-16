// Parameter validation only: profiles and material binding, with no draws or asset writes.
public class Script
{
    [System.Serializable]
    public sealed class Result
    {
        public string status;
        public int passed;
        public string[] cases;
    }

    public static object Main()
    {
        var cases = new System.Collections.Generic.List<string>();
        var profile = UnityEngine.ScriptableObject.CreateInstance<Mine.Weather.WeatherProfile>();
        profile.hideFlags = UnityEngine.HideFlags.HideAndDontSave;
        UnityEngine.Material material = null;
        try
        {
            var kinds = new[] { Mine.Weather.WeatherKind.Rain, Mine.Weather.WeatherKind.Sand, Mine.Weather.WeatherKind.Snow };
            float[] tileDefaults = { 4.5f, 3f, 1.5f };
            float[] scaleDefaults = { 0.35f, 0.2f, 0.1f };
            for (int i = 0; i < kinds.Length; i++)
            {
                string path = "Assets/Mine/Shaders/PostProcess/Weather/Presets/" + kinds[i] + ".asset";
                var asset = UnityEditor.AssetDatabase.LoadAssetAtPath<Mine.Weather.WeatherProfile>(path);
                Require(asset != null, "Missing preset: " + path);
                bool dirty = UnityEditor.EditorUtility.IsDirty(asset);
                CheckGeometry(asset.Resolve(), new UnityEngine.Vector2(tileDefaults[i], tileDefaults[i]),
                    scaleDefaults[i], kinds[i] + " asset defaults");
                Require(dirty == UnityEditor.EditorUtility.IsDirty(asset), "Resolve dirtied the preset asset: " + path);
                cases.Add(kinds[i] + " preset asset transform defaults (read only)");

                profile.ConfigurePreset(kinds[i]);
                CheckGeometry(profile.Resolve(), new UnityEngine.Vector2(tileDefaults[i], tileDefaults[i]),
                    scaleDefaults[i], kinds[i] + " ConfigurePreset defaults");
                cases.Add(kinds[i] + " ConfigurePreset transform defaults");

                profile.Particles.TileCount = new UnityEngine.Vector2(7.25f + i, 2.5f + i);
                profile.Particles.Scale = 0.77f + i * 0.1f;
                var particles = profile.Particles;
                var snapshot = profile.Resolve();
                CheckGeometry(snapshot, particles.TileCount, particles.Scale, kinds[i] + " custom snapshot");
                Require(System.Object.ReferenceEquals(particles, profile.Particles), "Resolve replaced the particle settings object.");
                cases.Add(kinds[i] + " non-square TileCount and Scale snapshot");

                UnityEngine.Vector2 capturedTile = snapshot.ParticleTileCount;
                float capturedScale = snapshot.ParticleScale;
                particles.TileCount = new UnityEngine.Vector2(3.125f, 5.75f);
                particles.Scale = 0.23f;
                CheckGeometry(snapshot, capturedTile, capturedScale, kinds[i] + " independent snapshot");
                var changedSnapshot = snapshot;
                changedSnapshot.ParticleTileCount = new UnityEngine.Vector2(100f, 200f);
                changedSnapshot.ParticleScale = 3.5f;
                CheckGeometry(snapshot, capturedTile, capturedScale, kinds[i] + " copied snapshot");
                Near(particles.TileCount.x, 3.125f, "Snapshot copy did not mutate Profile TileCount.x");
                Near(particles.TileCount.y, 5.75f, "Snapshot copy did not mutate Profile TileCount.y");
                Near(particles.Scale, 0.23f, "Snapshot copy did not mutate Profile Scale");
                cases.Add(kinds[i] + " resolved values and copied snapshots do not alias Profile");
            }

            profile.ConfigurePreset(Mine.Weather.WeatherKind.Rain);
            profile.Particles.TileCount = new UnityEngine.Vector2(-10f, 5000f);
            profile.Particles.Scale = -1f;
            var originalParticles = profile.Particles;
            var clamped = profile.Resolve();
            CheckGeometry(clamped, new UnityEngine.Vector2(0.01f, 256f), 0.001f, "Finite range clamps");
            Require(profile.Particles.TileCount.x == -10f && profile.Particles.TileCount.y == 5000f
                && profile.Particles.Scale == -1f, "Resolve wrote range clamps back into Profile.");
            Require(System.Object.ReferenceEquals(originalParticles, profile.Particles), "Resolve replaced invalid source settings.");
            cases.Add("Finite TileCount and Scale clamps without source mutation");

            profile.Particles.TileCount = new UnityEngine.Vector2(0.01f, 256f);
            profile.Particles.Scale = 4f;
            CheckGeometry(profile.Resolve(), new UnityEngine.Vector2(0.01f, 256f), 4f, "Inclusive limits");
            cases.Add("TileCount and Scale inclusive legal limits");

            foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
            {
                profile.Particles.TileCount = new UnityEngine.Vector2(invalid, 6.25f);
                profile.Particles.Scale = invalid;
                var resolved = profile.Resolve();
                LegalGeometry(resolved, "Non-finite transform fallback");
                Near(resolved.ParticleTileCount.y, 6.25f, "A bad x component must not reset legal y");
                Require(SameFloat(profile.Particles.TileCount.x, invalid) && SameFloat(profile.Particles.Scale, invalid),
                    "Resolve wrote non-finite fallback values to Profile.");
                cases.Add("Non-finite " + invalid + " transform fallback without writeback");
            }

            profile.Particles.TileCount = new UnityEngine.Vector2(8.5f, float.NaN);
            profile.Particles.Scale = 0.08f;
            var invalidY = profile.Resolve();
            LegalGeometry(invalidY, "Independent y fallback");
            Near(invalidY.ParticleTileCount.x, 8.5f, "A bad y component must not reset legal x");
            Near(invalidY.ParticleScale, 0.08f, "TileCount fallback must not reset legal Scale");
            Require(float.IsNaN(profile.Particles.TileCount.y), "Resolve wrote the y fallback into Profile.");
            cases.Add("Non-finite y fallback preserves legal x and Scale");

            // OnValidate is invoked solely on this unsaved temporary Profile.
            profile.Particles.TileCount = new UnityEngine.Vector2(-1f, float.PositiveInfinity);
            profile.Particles.Scale = 99f;
            var expectedValidation = profile.Resolve();
            var onValidate = typeof(Mine.Weather.WeatherProfile).GetMethod("OnValidate",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Require(onValidate != null, "WeatherProfile.OnValidate is missing.");
            onValidate.Invoke(profile, null);
            Near(profile.Particles.TileCount.x, expectedValidation.ParticleTileCount.x, "OnValidate TileCount.x");
            Near(profile.Particles.TileCount.y, expectedValidation.ParticleTileCount.y, "OnValidate TileCount.y");
            Near(profile.Particles.Scale, expectedValidation.ParticleScale, "OnValidate Scale");
            CheckGeometry(profile.Resolve(), expectedValidation.ParticleTileCount, expectedValidation.ParticleScale,
                "OnValidate and Resolve use the same range rules");
            cases.Add("Temporary Profile OnValidate shares Resolve range and fallback rules");

            var shader = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Shader>(
                "Assets/Mine/Shaders/PostProcess/Weather/Weather.shader");
            Require(shader != null && shader.isSupported, "Weather Shader is missing or unsupported.");
            material = new UnityEngine.Material(shader);
            material.hideFlags = UnityEngine.HideFlags.HideAndDontSave;
            Require(material.HasProperty("_ParticleTileCount") && material.HasProperty("_ParticleScale"),
                "Weather Shader is missing the transform properties.");
            var noise = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Texture2D>("Assets/Textures/Noise2DRG.asset");
            Require(noise != null, "Weather noise resource is missing.");
            var passType = typeof(Mine.Weather.WeatherFeature).GetNestedType("WeatherPass", System.Reflection.BindingFlags.NonPublic);
            Require(passType != null, "WeatherPass type is missing.");
            var bind = passType.GetMethod("Bind", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            Require(bind != null, "WeatherPass.Bind is missing.");
            int[] sequence = { 0, 1, 2, 0 };
            for (int step = 0; step < sequence.Length; step++)
            {
                int index = sequence[step];
                profile.ConfigurePreset(kinds[index]);
                if (step < 3)
                {
                    profile.Particles.TileCount = new UnityEngine.Vector2(9.125f + step, 2.25f + step * 3f);
                    profile.Particles.Scale = 0.125f + step * 0.31f;
                }
                var resolved = profile.Resolve();
                bind.Invoke(null, new object[] { material, noise, resolved, Mine.Weather.WeatherDebugMode.Off,
                    new UnityEngine.Vector4(1280f, 720f, 1f / 1280f, 1f / 720f), 12.5f });
                var tile = material.GetVector("_ParticleTileCount");
                Near(tile.x, resolved.ParticleTileCount.x, "Bind TileCount.x at sequence step " + step);
                Near(tile.y, resolved.ParticleTileCount.y, "Bind TileCount.y at sequence step " + step);
                Near(material.GetFloat("_ParticleScale"), resolved.ParticleScale, "Bind Scale at sequence step " + step);
                Require(material.GetTexture("_WeatherNoiseTex") == noise, "Bind lost the existing shared noise reference.");
                string[] keywords = { "WEATHER_RAIN", "WEATHER_SAND", "WEATHER_SNOW" };
                for (int mode = 0; mode < keywords.Length; mode++)
                    Require(material.IsKeywordEnabled(keywords[mode]) == (mode == index), "Bind left stale mode keyword " + keywords[mode]);
                cases.Add("Sequential Bind " + step + " " + kinds[index] + " refreshes TileCount, Scale and exclusive mode");
            }
            return UnityEngine.JsonUtility.ToJson(new Result { status = "PASS", passed = cases.Count, cases = cases.ToArray() }, true);
        }
        finally
        {
            if (material != null) UnityEngine.Object.DestroyImmediate(material);
            UnityEngine.Object.DestroyImmediate(profile);
        }
    }

    private static void CheckGeometry(Mine.Weather.ResolvedWeatherSettings settings, UnityEngine.Vector2 tile, float scale, string label)
    {
        Near(settings.ParticleTileCount.x, tile.x, label + " TileCount.x");
        Near(settings.ParticleTileCount.y, tile.y, label + " TileCount.y");
        Near(settings.ParticleScale, scale, label + " Scale");
        LegalGeometry(settings, label);
    }

    private static void LegalGeometry(Mine.Weather.ResolvedWeatherSettings settings, string label)
    {
        Require(Finite(settings.ParticleTileCount.x) && Finite(settings.ParticleTileCount.y) && Finite(settings.ParticleScale),
            label + " contains non-finite transform values.");
        Require(settings.ParticleTileCount.x >= 0.01f && settings.ParticleTileCount.x <= 256f
            && settings.ParticleTileCount.y >= 0.01f && settings.ParticleTileCount.y <= 256f
            && settings.ParticleScale >= 0.001f && settings.ParticleScale <= 4f, label + " exceeds transform ranges.");
    }

    private static bool SameFloat(float left, float right) { return left.Equals(right); }
    private static bool Finite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }
    private static void Near(float value, float expected, string label)
    {
        Require(Finite(value) && UnityEngine.Mathf.Abs(value - expected) <= 0.00001f,
            label + ": actual=" + value + ", expected=" + expected);
    }
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new System.InvalidOperationException(message);
    }
}
