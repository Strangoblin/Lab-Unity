public class Script
{
    public static object Main()
    {
        var profile = UnityEngine.ScriptableObject.CreateInstance<Mine.Weather.WeatherProfile>();
        var cases = new System.Collections.Generic.List<string>();

        try
        {
            CheckMigrationPresets(profile, cases);
            CheckModuleShutdown(profile, cases);
            CheckFrozenCycle(profile, cases);
            CheckWhiteTint(profile, cases);
            CheckInvalidInputDoesNotMutate(profile, cases);
            CheckSnowEndpoints(profile, cases);
            return new { Passed = cases.Count, Cases = cases.ToArray() };
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(profile);
        }
    }

    private static void CheckMigrationPresets(Mine.Weather.WeatherProfile profile,
        System.Collections.Generic.List<string> cases)
    {
        profile.ConfigurePreset(Mine.Weather.WeatherKind.Rain, 1f, 0.5f, 0.5f, 1f);
        Mine.Weather.ResolvedWeatherSettings rain = profile.Resolve();
        Require(rain.Kind == Mine.Weather.WeatherKind.Rain && profile.Kind == rain.Kind, "Rain mode");
        RequireNear(rain.Randomness, 1f, "Rain randomness");
        RequireNear(rain.CycleRate, 1f, "Rain cycle migration");
        RequireNear(rain.ParticleShape, 0.5f, "Rain particle shape migration");
        RequireNear(rain.ParticleOpacity, 0.5f, "Rain particle opacity migration");
        RequireNear(rain.RefractionScale, 1f, "Rain refraction migration");
        RequireNear(rain.LensStrength, 0.5f, "Rain lens migration");
        RequireNear(rain.FogStrength, 0.5f, "Rain fog migration");
        RequireNear(rain.ToneStrength, 1f, "Rain tone migration");
        RequireTint(rain.ToneTint, 0.88f, 0.96f, 1.05f, "Rain cold tint");
        Require(!rain.LegacySnowAlpha, "Rain preserves input Alpha");
        Require(rain.ParticlesActive && rain.AtmosphereActive && rain.FogActive, "Rain active modules");
        cases.Add("Rain actual-material migration");

        profile.ConfigurePreset(Mine.Weather.WeatherKind.Sand, 1f, 0.5f, 1f, 1f);
        Mine.Weather.ResolvedWeatherSettings sand = profile.Resolve();
        Require(sand.Kind == Mine.Weather.WeatherKind.Sand && profile.Kind == sand.Kind, "Sand mode");
        RequireNear(sand.Randomness, 1f, "Sand randomness");
        RequireNear(sand.CycleRate, 1f, "Sand cycle migration");
        RequireNear(sand.ParticleOpacity, 1f, "Sand particle opacity migration");
        RequireNear(sand.LensStrength, 1f, "Sand lens migration");
        RequireNear(sand.FogStrength, 1f, "Sand fog migration");
        RequireNear(sand.ToneStrength, 1f, "Sand tone migration");
        RequireTint(sand.ToneTint, 1.08f, 0.96f, 0.82f, "Sand warm tint");
        Require(!sand.LegacySnowAlpha, "Sand preserves input Alpha");
        Require(sand.ParticlesActive && sand.AtmosphereActive && sand.FogActive, "Sand active modules");
        cases.Add("Sand actual-material migration");

        profile.ConfigurePreset(Mine.Weather.WeatherKind.Snow, 1f, 0.5f, 0.3f, 1f);
        Mine.Weather.ResolvedWeatherSettings snow = profile.Resolve();
        Require(snow.Kind == Mine.Weather.WeatherKind.Snow && profile.Kind == snow.Kind, "Snow mode");
        RequireNear(snow.Randomness, 1f, "Snow randomness");
        RequireNear(snow.CycleRate, 1f, "Snow cycle migration");
        RequireNear(snow.ParticleShape, 0.3f, "Snow particle shape migration");
        RequireNear(snow.ParticleOpacity, 0.3f, "Snow particle opacity migration");
        RequireNear(snow.LensStrength, 0.3f, "Snow lens opacity migration");
        RequireNear(snow.EdgeBaseWidth, 0.03f, "Snow base width migration");
        RequireNear(snow.EdgeNoiseWidth, 0.3f, "Snow noise width migration");
        RequireNear(snow.FogStrength, 0.3f, "Snow fog opacity migration");
        RequireNear(snow.FogBaseDistance, 50f, "Snow base distance migration");
        RequireNear(snow.FogNoiseDistance, 100f, "Snow noise distance migration");
        RequireNear(snow.FogNoiseScrollX, 0.5f, "Snow fog scroll migration");
        RequireNear(snow.ToneStrength, 1f, "Snow tone migration");
        RequireTint(snow.ToneTint, 0.88f, 0.96f, 1.05f, "Snow cold tint");
        Require(snow.LegacySnowAlpha, "Snow retains legacy Alpha policy");
        Require(snow.ParticlesActive && snow.AtmosphereActive && snow.FogActive, "Snow active modules");
        cases.Add("Snow actual-material migration");
    }

    private static void CheckModuleShutdown(Mine.Weather.WeatherProfile profile,
        System.Collections.Generic.List<string> cases)
    {
        foreach (Mine.Weather.WeatherKind kind in System.Enum.GetValues(typeof(Mine.Weather.WeatherKind)))
        {
            profile.ConfigurePreset(kind, 1f, 0.5f, 0.5f, 1f);
            profile.Particles.Shape = 0f;
            profile.Particles.Opacity = 0f;
            profile.Particles.RefractionScale = 0f;
            profile.Lens.Strength = 0f;
            profile.Fog.Strength = 0f;
            profile.Tone.Strength = 0f;

            Mine.Weather.ResolvedWeatherSettings settings = profile.Resolve();
            Require(!settings.ParticlesActive && !settings.AtmosphereActive && !settings.FogActive,
                kind + " zero modules must deactivate both stages");
            cases.Add(kind + " all modules disabled");
        }
    }

    private static void CheckFrozenCycle(Mine.Weather.WeatherProfile profile,
        System.Collections.Generic.List<string> cases)
    {
        foreach (Mine.Weather.WeatherKind kind in System.Enum.GetValues(typeof(Mine.Weather.WeatherKind)))
        {
            profile.ConfigurePreset(kind, 1f, 0f, 0.5f, 1f);
            RequireNear(profile.Resolve().CycleRate, 0f, kind + " zero Wind freezes particle cycle");
            Require(profile.Resolve().ParticlesActive, kind + " frozen particles still contribute");
            profile.Particles.Opacity = 0f;
            profile.Fog.Strength = 0f;
            profile.Tone.Strength = 0f;
            Require(profile.Resolve().AtmosphereActive, kind + " zero cycle does not disable lens");

            profile.Lens.Strength = 0f;
            profile.Fog.Strength = 0.5f;
            Require(profile.Resolve().FogActive && profile.Resolve().AtmosphereActive,
                kind + " zero cycle does not disable fog");

            profile.Fog.Strength = 0f;
            profile.Tone.Strength = 0.5f;
            Require(profile.Resolve().AtmosphereActive, kind + " zero cycle does not disable tone");
            cases.Add(kind + " cycle freeze preserves module contributions");
        }
    }

    private static void CheckWhiteTint(Mine.Weather.WeatherProfile profile,
        System.Collections.Generic.List<string> cases)
    {
        profile.ConfigurePreset(Mine.Weather.WeatherKind.Rain);
        profile.Particles.Opacity = 0f;
        profile.Lens.Strength = 0f;
        profile.Fog.Strength = 0f;
        profile.Tone.Strength = 1f;
        profile.Tone.Tint = UnityEngine.Color.white;

        Mine.Weather.ResolvedWeatherSettings settings = profile.Resolve();
        Require(!settings.ParticlesActive && !settings.FogActive && settings.AtmosphereActive,
            "White Tint still needs the Atmosphere pass because tone includes desaturation");
        cases.Add("White Tint keeps tone stage active");
    }

    private static void CheckInvalidInputDoesNotMutate(Mine.Weather.WeatherProfile profile,
        System.Collections.Generic.List<string> cases)
    {
        profile.ConfigurePreset(Mine.Weather.WeatherKind.Snow, 1f, 0.5f, 0.3f, 1f);
        var common = profile.Common;
        var particles = profile.Particles;
        var lens = profile.Lens;
        var fog = profile.Fog;
        var tone = profile.Tone;
        common.Kind = (Mine.Weather.WeatherKind)999;
        common.Randomness = float.NaN;
        common.CycleRate = float.PositiveInfinity;
        particles.Shape = float.NegativeInfinity;
        particles.Opacity = float.NaN;
        particles.RefractionScale = float.PositiveInfinity;
        lens.Strength = float.PositiveInfinity;
        lens.EdgeBaseWidth = float.NaN;
        lens.EdgeNoiseWidth = float.NegativeInfinity;
        fog.Strength = float.NaN;
        fog.BaseDistance = float.NaN;
        fog.NoiseDistance = float.PositiveInfinity;
        fog.NoiseScrollX = float.NegativeInfinity;
        tone.Strength = float.NaN;
        tone.Tint = new UnityEngine.Color(float.NaN, float.PositiveInfinity, float.NegativeInfinity, float.NaN);
        profile.AlphaPolicy = (Mine.Weather.WeatherAlphaPolicy)999;

        Mine.Weather.ResolvedWeatherSettings settings = profile.Resolve();
        Require(settings.Kind == Mine.Weather.WeatherKind.Rain, "Invalid weather mode resolves to Rain");
        Require(!settings.LegacySnowAlpha, "Invalid Alpha policy resolves to input preservation");
        RequireFiniteSettings(settings);
        Require(settings.Randomness >= 0f && settings.Randomness <= 1f, "Resolved randomness has a legal range");
        Require(settings.CycleRate >= 0f && settings.CycleRate <= 2f, "Resolved cycle has a legal range");
        Require(settings.FogBaseDistance > 0f && settings.FogNoiseDistance >= 0f, "Resolved snow distances are safe");

        Require(System.Object.ReferenceEquals(profile.Common, common)
            && System.Object.ReferenceEquals(profile.Particles, particles)
            && System.Object.ReferenceEquals(profile.Lens, lens)
            && System.Object.ReferenceEquals(profile.Fog, fog)
            && System.Object.ReferenceEquals(profile.Tone, tone), "Resolve must not replace source settings objects");
        Require((int)common.Kind == 999 && (int)profile.AlphaPolicy == 999, "Resolve must not write back invalid enums");
        Require(float.IsNaN(common.Randomness) && common.CycleRate == float.PositiveInfinity,
            "Resolve must not write back Common values");
        Require(particles.Shape == float.NegativeInfinity && float.IsNaN(particles.Opacity)
            && particles.RefractionScale == float.PositiveInfinity, "Resolve must not write back Particles values");
        Require(lens.Strength == float.PositiveInfinity && float.IsNaN(lens.EdgeBaseWidth)
            && lens.EdgeNoiseWidth == float.NegativeInfinity, "Resolve must not write back Lens values");
        Require(float.IsNaN(fog.Strength) && float.IsNaN(fog.BaseDistance)
            && fog.NoiseDistance == float.PositiveInfinity && fog.NoiseScrollX == float.NegativeInfinity,
            "Resolve must not write back Fog values");
        Require(float.IsNaN(tone.Strength) && float.IsNaN(tone.Tint.r)
            && tone.Tint.g == float.PositiveInfinity && tone.Tint.b == float.NegativeInfinity
            && float.IsNaN(tone.Tint.a), "Resolve must not write back Tone values");
        cases.Add("Non-finite inputs and invalid enums resolve without source mutation");
    }

    private static void CheckSnowEndpoints(Mine.Weather.WeatherProfile profile,
        System.Collections.Generic.List<string> cases)
    {
        profile.ConfigurePreset(Mine.Weather.WeatherKind.Snow, 1f, 1f, 0.3f, 1f);
        Mine.Weather.ResolvedWeatherSettings settings = profile.Resolve();
        Require(settings.FogBaseDistance > 0f && !float.IsNaN(settings.FogBaseDistance)
            && !float.IsInfinity(settings.FogBaseDistance), "Snow Wind=1 must keep a positive finite fog distance");
        RequireNear(settings.CycleRate, 2f, "Snow Wind=1 cycle endpoint");
        RequireNear(settings.FogNoiseScrollX, 1f, "Snow Wind=1 independent fog scroll migration");
        cases.Add("Snow Wind=1 fog distance remains positive");

        profile.Particles.Opacity = 0f;
        profile.Fog.Strength = 0f;
        profile.Tone.Strength = 0f;
        profile.Lens.Strength = 1f;
        profile.Lens.EdgeBaseWidth = 0f;
        profile.Lens.EdgeNoiseWidth = 0f;
        Require(!profile.Resolve().AtmosphereActive, "Snow zero widths disable lens even at positive strength");
        profile.Lens.EdgeBaseWidth = 0.03f;
        Require(profile.Resolve().AtmosphereActive, "Snow positive base width allows lens contribution");
        profile.Lens.EdgeBaseWidth = 0f;
        profile.Lens.EdgeNoiseWidth = 0.3f;
        Require(profile.Resolve().AtmosphereActive, "Snow positive noise width allows lens contribution");
        cases.Add("Snow lens width endpoints");
    }

    private static void RequireFiniteSettings(Mine.Weather.ResolvedWeatherSettings settings)
    {
        float[] values =
        {
            settings.Randomness, settings.CycleRate, settings.ParticleShape, settings.ParticleOpacity,
            settings.RefractionScale, settings.LensStrength, settings.EdgeBaseWidth, settings.EdgeNoiseWidth,
            settings.FogStrength, settings.FogBaseDistance, settings.FogNoiseDistance, settings.FogNoiseScrollX,
            settings.ToneStrength, settings.ToneTint.r, settings.ToneTint.g, settings.ToneTint.b, settings.ToneTint.a
        };
        for (int i = 0; i < values.Length; i++)
        {
            Require(!float.IsNaN(values[i]) && !float.IsInfinity(values[i]), "Resolved parameter " + i + " must be finite");
        }
    }

    private static void RequireTint(UnityEngine.Color value, float red, float green, float blue, string name)
    {
        RequireNear(value.r, red, name + " R");
        RequireNear(value.g, green, name + " G");
        RequireNear(value.b, blue, name + " B");
    }

    private static void RequireNear(float actual, float expected, string name)
    {
        Require(!float.IsNaN(actual) && !float.IsInfinity(actual) && UnityEngine.Mathf.Abs(actual - expected) <= 0.000001f,
            name + ": expected " + expected + ", actual " + actual);
    }

    private static void Require(bool condition, string name)
    {
        if (!condition)
            throw new System.InvalidOperationException("Weather Profile check failed: " + name);
    }
}
