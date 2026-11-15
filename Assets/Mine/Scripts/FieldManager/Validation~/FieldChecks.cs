using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Mine.Fields;
using UObject = UnityEngine.Object;

// Run with: unityctl script execute Assets/Mine/Scripts/FieldManager/Validation~/FieldChecks.cs
// Creates and destroys temporary objects only; the active manager's snapshot is restored.
// The duplicate-producer case is expected to log one Error, which is asserted and not counted.
public class Script
{
    [Serializable]
    public sealed class Report
    {
        public string suite = "FieldChecks";
        public int passed;
        public int failed;
        public int skipped;
        public List<string> details = new List<string>();
    }

    private sealed class CheckFailure : Exception
    {
        public CheckFailure(string message) : base(message) { }
    }

    private sealed class CheckSkip : Exception
    {
        public CheckSkip(string message) : base(message) { }
    }

    private sealed class StubBindings : IFieldBindings
    {
        public string FieldKey => "FieldChecksAbsent";
        public int NeutralCalls;

        public void BindNeutral(ComputeShader shader, int kernel) => NeutralCalls++;
        public void BindNeutralGlobals() => NeutralCalls++;
    }

    public static object Main()
    {
        var report = new Report();
        var errors = new List<string>();
        Application.LogCallback onLog = (message, stack, type) =>
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                errors.Add(type + ": " + message);
        };
        Application.logMessageReceived += onLog;

        var owned = new List<GameObject>();
        FieldManager manager = FieldManager.Instance;
        bool managerWasEnabled = manager != null && manager.enabled;

        try
        {
            CheckContract(report);
            CheckInactiveOrders(report, owned);
            CheckBindings(report);
            CheckNeutralPath(report, errors);

            // Own the manager slot while probing registration, then restore the scene manager.
            if (managerWasEnabled) manager.enabled = false;
            CheckDuplicateProducer(report, owned, errors);
            CheckUnregister(report, owned);
            CheckLiveOrder(report);
        }
        finally
        {
            for (int i = owned.Count - 1; i >= 0; i--)
                if (owned[i] != null) UObject.DestroyImmediate(owned[i]);
            Application.logMessageReceived -= onLog;

            // Republish the scene snapshot after the neutral and duplicate probes.
            if (manager != null && managerWasEnabled)
            {
                manager.enabled = true;
                manager.UpdateFields();
            }
            else
            {
                // No manager: standalone providers republish on the next editor repaint.
                UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
            }
        }

        if (errors.Count > 0)
        {
            report.failed++;
            report.details.Add("FAIL unexpected logs: " + string.Join(" | ", errors));
        }

        report.details.Add("passed=" + report.passed + " failed=" + report.failed + " skipped=" + report.skipped);
        return report;
    }

    private static void CheckContract(Report report)
    {
        Run(report, "contract.fft_is_wave_provider", () =>
        {
            Require(typeof(WaveFieldProvider).IsSubclassOf(typeof(FieldProvider)), "WaveFieldProvider derives from FieldProvider");
            Require(typeof(FFTWaveFieldProvider).IsSubclassOf(typeof(WaveFieldProvider)), "FFTWaveFieldProvider derives from WaveFieldProvider");
            Require(typeof(FFTWaveFieldProvider).IsSealed, "FFTWaveFieldProvider is sealed");
        });

        Run(report, "contract.keys", () =>
        {
            Equal("Wind", WindFieldBindings.Instance.FieldKey, "wind key");
            Equal("Wave", WaveFieldBindings.Instance.FieldKey, "wave key");
            Equal("Wind", WindFieldBindings.Key, "wind const key");
            Equal("Wave", WaveFieldBindings.Key, "wave const key");
        });

        Run(report, "contract.no_enum", () =>
        {
            Require(FindType("Mine.Fields.FieldKind") == null, "FieldKind no longer exists");
        });
    }

    private static void CheckInactiveOrders(Report report, List<GameObject> owned)
    {
        Run(report, "contract.orders", () =>
        {
            GameObject root = Own(owned, "FieldChecks Orders");
            root.SetActive(false);
            WindFieldProvider wind = root.AddComponent<WindFieldProvider>();
            FFTWaveFieldProvider wave = root.AddComponent<FFTWaveFieldProvider>();

            Equal(-200, wind.FieldOrder, "wind order");
            Equal(-100, wave.FieldOrder, "wave order");
            Require(wind.FieldOrder < wave.FieldOrder, "wind schedules before wave");
        });
    }

    private static void CheckBindings(Report report)
    {
        Run(report, "bindings.neutral_globals", () =>
        {
            WindFieldBindings.Instance.BindNeutralGlobals();
            Equal(0f, Shader.GetGlobalFloat("_WindFieldValid"), "wind validity cleared");
            Equal(0f, Shader.GetGlobalFloat("_WindFieldTime"), "wind time cleared");
            Require(ReferenceEquals(Texture2D.blackTexture, Shader.GetGlobalTexture("_WindFieldTexture")), "wind texture neutral");

            WaveFieldBindings.Instance.BindNeutralGlobals();
            Equal(0f, Shader.GetGlobalFloat("_WaveFieldValid"), "wave validity cleared");
            Require(ReferenceEquals(Texture2D.grayTexture, Shader.GetGlobalTexture("_WaveNormal0")), "wave normal neutral");
            Require(ReferenceEquals(Texture2D.blackTexture, Shader.GetGlobalTexture("_WaveDisplacement0")), "wave displacement neutral");
        });

        Run(report, "bindings.neutral_compute", () =>
        {
            ComputeShader compute = AssetDatabase.LoadAssetAtPath<ComputeShader>(
                "Assets/Mine/Scripts/FieldManager/Wind/WindField.compute");
            Require(compute != null, "wind compute asset exists");
            int kernel = compute.FindKernel("BuildWind");
            WindFieldBindings.Instance.BindNeutral(compute, kernel);
            WaveFieldBindings.Instance.BindNeutral(compute, kernel);
        });
    }

    private static void CheckNeutralPath(Report report, List<string> errors)
    {
        Run(report, "registry.absent_provider_binds_neutral", () =>
        {
            ComputeShader compute = AssetDatabase.LoadAssetAtPath<ComputeShader>(
                "Assets/Mine/Scripts/FieldManager/Wind/WindField.compute");
            Require(compute != null, "wind compute asset exists");
            int kernel = compute.FindKernel("BuildWind");

            var bindings = new StubBindings();
            bool bound = FieldManager.BindCompute(bindings, compute, kernel);
            Require(!bound, "absent provider reports false");
            Equal(1, bindings.NeutralCalls, "absent provider binds neutral once");
        });
    }

    private static void CheckDuplicateProducer(Report report, List<GameObject> owned, List<string> errors)
    {
        Run(report, "registry.duplicate_rejected", () =>
        {
            ComputeShader compute = AssetDatabase.LoadAssetAtPath<ComputeShader>(
                "Assets/Mine/Scripts/FieldManager/Wind/WindField.compute");
            Require(compute != null, "wind compute asset exists");

            bool hadWind = FieldManager.GetProvider(WindFieldBindings.Key) != null;
            int before = errors.Count;

            GameObject root = Own(owned, "FieldChecks Registry");
            root.SetActive(false);
            FieldManager manager = root.AddComponent<FieldManager>();
            WindFieldProvider wind = root.AddComponent<WindFieldProvider>();
            SetField(wind, "_computeShader", compute);
            root.SetActive(true);

            if (!hadWind)
            {
                Require(FieldManager.GetProvider(WindFieldBindings.Key) == wind, "first wind registers");
                GameObject second = Own(owned, "FieldChecks Duplicate");
                second.SetActive(false);
                WindFieldProvider duplicate = second.AddComponent<WindFieldProvider>();
                SetField(duplicate, "_computeShader", compute);
                second.SetActive(true);

                Require(!duplicate.enabled, "duplicate producer disabled");
                Require(ReferenceEquals(wind, FieldManager.GetProvider(WindFieldBindings.Key)), "first producer retained");
                Require(ConsumeDuplicateLog(errors, before), "duplicate log emitted");
            }
            else
            {
                Require(!wind.enabled, "duplicate of scene producer disabled");
                Require(ConsumeDuplicateLog(errors, before), "duplicate log emitted");
            }
        });
    }

    private static void CheckUnregister(Report report, List<GameObject> owned)
    {
        Run(report, "registry.unregister_resets_globals", () =>
        {
            if (FieldManager.GetProvider(WaveFieldBindings.Key) != null)
                throw new CheckSkip("a scene wave provider owns the globals");

            GameObject root = Own(owned, "FieldChecks Unregister");
            root.SetActive(false);
            FFTWaveFieldProvider wave = root.AddComponent<FFTWaveFieldProvider>();
            Require(Register(wave), "inert wave registers");

            Unregister(wave);
            Equal(0f, Shader.GetGlobalFloat("_WaveFieldValid"), "wave validity cleared");
            Equal(1f, Shader.GetGlobalFloat("_WavePatchSize0"), "patch size neutral");
            Require(ReferenceEquals(Texture2D.grayTexture, Shader.GetGlobalTexture("_WaveNormal0")), "wave normal neutral");
            Require(ReferenceEquals(Texture2D.blackTexture, Shader.GetGlobalTexture("_WaveDisplacement0")), "wave displacement neutral");
        });
    }

    private static void CheckLiveOrder(Report report)
    {
        Run(report, "registry.live_order", () =>
        {
            var providers = GetProviders();
            int wind = providers.FindIndex(p => p != null && p.FieldKey == WindFieldBindings.Key);
            if (wind < 0) throw new CheckSkip("needs a registered wind provider");

            // Register an inert wave producer when the scene has none, so the ordering
            // contract is exercised without allocating FFT resources.
            FieldProvider wave = providers.Find(p => p != null && p.FieldKey == WaveFieldBindings.Key);
            GameObject temporary = null;
            if (wave == null)
            {
                temporary = new GameObject("FieldChecks Order Wave");
                temporary.SetActive(false);
                wave = temporary.AddComponent<FFTWaveFieldProvider>();
                Require(Register(wave), "inert wave registers");
            }

            try
            {
                providers = GetProviders();
                wind = providers.FindIndex(p => p != null && p.FieldKey == WindFieldBindings.Key);
                int waveIndex = providers.FindIndex(p => p != null && p.FieldKey == WaveFieldBindings.Key);
                Require(wind >= 0 && waveIndex >= 0, "both channels registered");
                Require(wind < waveIndex, "registered wind precedes registered wave");
            }
            finally
            {
                if (temporary != null)
                {
                    Unregister(wave);
                    UObject.DestroyImmediate(temporary);
                }
            }
        });
    }

    private static bool Register(FieldProvider provider)
    {
        MethodInfo method = typeof(FieldManager).GetMethod("Register", BindingFlags.Static | BindingFlags.NonPublic);
        Require(method != null, "register method exists");
        return (bool)method.Invoke(null, new object[] { provider });
    }

    private static void Unregister(FieldProvider provider)
    {
        MethodInfo method = typeof(FieldManager).GetMethod("Unregister", BindingFlags.Static | BindingFlags.NonPublic);
        Require(method != null, "unregister method exists");
        method.Invoke(null, new object[] { provider });
    }

    private static List<FieldProvider> GetProviders()
    {
        FieldInfo field = typeof(FieldManager).GetField("Providers", BindingFlags.Static | BindingFlags.NonPublic);
        Require(field != null, "providers field exists");
        return (List<FieldProvider>)field.GetValue(null);
    }

    private static bool ConsumeDuplicateLog(List<string> errors, int from)
    {
        bool found = false;
        for (int i = errors.Count - 1; i >= from; i--)
        {
            if (!errors[i].Contains("Duplicate field producer")) continue;
            found = true;
            errors.RemoveAt(i);
        }
        return found;
    }

    private static void SetField(object target, string name, object value)
    {
        FieldInfo field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        Require(field != null, "field exists: " + name);
        field.SetValue(target, value);
    }

    private static GameObject Own(List<GameObject> owned, string name)
    {
        var go = new GameObject(name);
        owned.Add(go);
        return go;
    }

    private static Type FindType(string name)
    {
        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type type = assembly.GetType(name, false);
            if (type != null) return type;
        }
        return null;
    }

    private static void Run(Report report, string name, Action test)
    {
        try
        {
            test();
            report.passed++;
            report.details.Add("PASS " + name);
        }
        catch (CheckSkip skip)
        {
            report.skipped++;
            report.details.Add("SKIP " + name + ": " + skip.Message);
        }
        catch (Exception exception)
        {
            report.failed++;
            report.details.Add("FAIL " + name + ": " + exception.GetType().Name + ": " + exception.Message);
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new CheckFailure(message);
    }

    private static void Equal<T>(T expected, T actual, string message)
    {
        Require(EqualityComparer<T>.Default.Equals(expected, actual),
            message + "; expected=" + expected + ", actual=" + actual);
    }
}
