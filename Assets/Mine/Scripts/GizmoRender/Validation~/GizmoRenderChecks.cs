using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Mine.GizmoRendering;
using UObject = UnityEngine.Object;

// Run with: unityctl script execute Assets/Mine/Scripts/GizmoRender/Validation~/GizmoRenderChecks.cs
// No geometry is emitted, no asset is created, and no business component is enabled.
// Save() is deliberately exercised on a nonpersistent configuration. Unity must either support
// that no-op or GizmoMemberSet.Save must guard it; any Error/Exception/Assert log fails this check.
public class Script
{
    [Serializable]
    public sealed class Report
    {
        public string suite = "GizmoRenderChecks";
        public int passed;
        public int failed;
        public int visibilityCases;
        public int knownFailureControls;
        public List<string> details = new List<string>();
    }

    private sealed class CheckFailure : Exception
    {
        public CheckFailure(string message) : base(message) { }
    }

    private sealed class StubSource : IGizmoSource
    {
        public string Member { get; set; }
        public GizmoForm Form => GizmoForm.Volume;
        public int DrawCalls;
        public Action<GizmoCanvas> Paint;

        public void DrawGizmos(GizmoCanvas canvas)
        {
            DrawCalls++;
            Paint?.Invoke(canvas);
        }
    }

    private sealed class SceneState
    {
        public Scene Scene;
        public bool Dirty;
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
        FieldInfo cacheField = typeof(GizmoMemberSet).GetField("_cached",
            BindingFlags.Static | BindingFlags.NonPublic);
        Require(cacheField != null, "GizmoMemberSet._cached exists for isolated configuration injection");
        object savedCache = cacheField.GetValue(null);
        GizmoCanvas canvas = GetSharedCanvas();
        Color savedBaseColor = canvas.BaseColor;
        Color savedColor = Gizmos.color;
        Matrix4x4 savedMatrix = Gizmos.matrix;
        const string originalPolicyKey = "Mine.SceneGizmo.Policy";
        string policyKey = GizmoRender.PolicyKey;
        bool hadOriginalPolicy = EditorPrefs.HasKey(originalPolicyKey);
        int savedOriginalPolicy = EditorPrefs.GetInt(originalPolicyKey);
        bool hadPolicy = EditorPrefs.HasKey(policyKey);
        int savedPolicy = EditorPrefs.GetInt(policyKey);
        var scenes = new List<SceneState>();
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene scene = SceneManager.GetSceneAt(i);
            scenes.Add(new SceneState { Scene = scene, Dirty = scene.isDirty });
        }

        GizmoMemberSet fixture = null;
        GameObject temporaryObject = null;
        Application.logMessageReceived += onLog;
        try
        {
            fixture = ScriptableObject.CreateInstance<GizmoMemberSet>();
            fixture.hideFlags = HideFlags.HideAndDontSave;
            cacheField.SetValue(null, fixture);

            Run(report, "fixture.no_asset_or_original_config_access", () =>
            {
                Require(!EditorUtility.IsPersistent(fixture), "fixture must remain nonpersistent");
                Equal("", AssetDatabase.GetAssetPath(fixture), "fixture asset path");
                Require(ReferenceEquals(fixture, GizmoMemberSet.Ensure()), "Ensure must use injected cache");
                Equal(originalPolicyKey, policyKey, "existing editor preference key is preserved");
                fixture.Save();
                Equal("", AssetDatabase.GetAssetPath(fixture), "Save must not create an asset");
            });

            Run(report, "validator.known_wrong_expectation_is_caught", () =>
            {
                bool caught = false;
                try { Equal(17, 18, "deliberately incorrect expectation"); }
                catch (CheckFailure) { caught = true; }
                Require(caught, "assertion must reject a known wrong expected value");
                report.knownFailureControls++;
            });

            CheckVisibility(report, fixture);
            CheckLabelsAndRosters(report, fixture);
            CheckDispatchState(report, fixture, canvas);
            CheckRealCallbacks(report);

            Run(report, "roster.destroyed_unity_source_is_removed", () =>
            {
                Type windType = FindType("Mine.Fields.WindSource");
                Require(windType != null, "real WindSource type exists");
                temporaryObject = new GameObject("__GizmoRenderChecks_DestroyedSource__");
                temporaryObject.hideFlags = HideFlags.HideAndDontSave;
                temporaryObject.SetActive(false);
                var source = temporaryObject.AddComponent(windType) as IGizmoSource;
                Require(source != null, "real WindSource implements IGizmoSource");
                source.Member = "Destroyed source";
                GizmoMember member = fixture.EnsureMember(source.Member);
                Require(ReferenceEquals(member, GizmoRender.ResolveMember(source)), "real source resolves");
                Equal(1, member.Sources.Count, "real source is attached before destruction");
                UObject.DestroyImmediate(temporaryObject);
                temporaryObject = null;
                Equal(0, member.Sources.Count, "Unity fake-null source is pruned through interface");
            });
        }
        catch (Exception exception)
        {
            Fail(report, "fixture.unexpected_exception", exception);
        }
        finally
        {
            Cleanup(report, "cleanup.temporary_gameobject", () =>
            {
                if (temporaryObject != null) UObject.DestroyImmediate(temporaryObject);
            });
            Cleanup(report, "cleanup.cached_configuration", () => cacheField.SetValue(null, savedCache));
            Cleanup(report, "cleanup.editor_preferences", () =>
            {
                RestorePreference(policyKey, hadPolicy, savedPolicy);
                if (!string.Equals(policyKey, originalPolicyKey, StringComparison.Ordinal))
                    RestorePreference(originalPolicyKey, hadOriginalPolicy, savedOriginalPolicy);
            });
            Cleanup(report, "cleanup.gizmos_state", () =>
            {
                Gizmos.color = savedColor;
                Gizmos.matrix = savedMatrix;
                typeof(GizmoCanvas).GetProperty("BaseColor").SetValue(canvas, savedBaseColor, null);
            });
            Cleanup(report, "cleanup.nonpersistent_configuration", () =>
            {
                if (fixture != null) UObject.DestroyImmediate(fixture);
            });
            Application.logMessageReceived -= onLog;
        }

        Run(report, "cleanup.original_scenes_unchanged", () =>
        {
            Equal(scenes.Count, SceneManager.sceneCount, "loaded scene count");
            foreach (SceneState state in scenes)
            {
                Require(state.Scene.IsValid(), "original scene remains loaded");
                Equal(state.Dirty, state.Scene.isDirty, "original scene dirty flag: " + state.Scene.name);
            }
            Require(ReferenceEquals(savedCache, cacheField.GetValue(null)), "original cached configuration restored");
            Equal(hadPolicy, EditorPrefs.HasKey(policyKey), "policy preference presence restored");
            if (hadPolicy) Equal(savedPolicy, EditorPrefs.GetInt(policyKey), "policy preference value restored");
            SameStroke(savedColor, Gizmos.color, "original Gizmos.color restored");
            SameMatrix(savedMatrix, Gizmos.matrix, "original Gizmos.matrix restored");
            SameColor(savedBaseColor, canvas.BaseColor, "original shared BaseColor restored");
        });
        Run(report, "unity.no_unexpected_error_logs", () =>
            Require(errors.Count == 0, string.Join(" | ", errors.ToArray())));
        Run(report, "suite.required_cases_executed", () =>
        {
            Equal(48, report.visibilityCases, "all enabled/presence/policy/pass combinations ran");
            Equal(1, report.knownFailureControls, "known wrong expectation was rejected");
            Require(report.passed >= 60, "the suite executed its functional cases");
        });

        string json = JsonUtility.ToJson(report, true);
        if (report.failed != 0) throw new InvalidOperationException(json);
        return json;
    }

    private static void CheckVisibility(Report report, GizmoMemberSet fixture)
    {
        GizmoMember member = fixture.EnsureMember("Visibility");
        var source = new StubSource { Member = member.Label };
        // Columns: Hidden(false,true), OnSelected(false,true), Always(false,true).
        // Rows: Default, Hidden, OnSelected, Always. This is the declared contract's explicit table.
        bool[,] expected =
        {
            { false, false, false, true, true, false },
            { false, false, false, false, false, false },
            { false, true, false, true, false, true },
            { true, false, true, false, true, false }
        };
        for (int enabled = 0; enabled <= 1; enabled++)
        for (int presence = 0; presence < 3; presence++)
        for (int policy = 0; policy < 4; policy++)
        for (int pass = 0; pass < 2; pass++)
        {
            member.Enabled = enabled == 1;
            member.Presence = (GizmoPresence)presence;
            GizmoRender.Policy = (GizmoPolicy)policy;
            bool wanted = enabled == 1 && expected[policy, presence * 2 + pass];
            bool selectedPass = pass == 1;
            string name = "visibility.enabled=" + enabled + ".presence=" + presence +
                          ".policy=" + policy + ".selectedPass=" + pass;
            Run(report, name, () =>
            {
                Equal(wanted, GizmoRender.IsVisible(source, selectedPass), name + " IsVisible");
                int before = source.DrawCalls;
                GizmoRender.Draw(source, selectedPass);
                Equal(wanted ? 1 : 0, source.DrawCalls - before, name + " Draw dispatch");
            });
            report.visibilityCases++;
        }
        GizmoRender.Policy = GizmoPolicy.Default;

        Run(report, "roster.hidden_and_disabled_sources_attach_before_visibility", () =>
        {
            GizmoMember disabled = fixture.EnsureMember("Disabled roster");
            disabled.Enabled = false;
            var disabledSource = new StubSource { Member = disabled.Label };
            GizmoRender.Draw(disabledSource, false);
            Equal(1, disabled.Sources.Count, "disabled source must still be discoverable");
            Equal(0, disabledSource.DrawCalls, "disabled source must not paint");
            GizmoMember hidden = fixture.EnsureMember("Hidden roster");
            hidden.Presence = GizmoPresence.Hidden;
            var hiddenSource = new StubSource { Member = hidden.Label };
            GizmoRender.Draw(hiddenSource, false);
            Equal(1, hidden.Sources.Count, "hidden source must still be discoverable");
            Equal(0, hiddenSource.DrawCalls, "hidden source must not paint");
        });
    }

    private static void CheckLabelsAndRosters(Report report, GizmoMemberSet fixture)
    {
        Run(report, "labels.normalize_blank_and_trim", () =>
        {
            Equal("Unassigned", GizmoMemberSet.NormalizeLabel(null), "null label");
            Equal("Unassigned", GizmoMemberSet.NormalizeLabel("\t \r\n"), "whitespace label");
            Equal("Trimmed", GizmoMemberSet.NormalizeLabel("  Trimmed\t"), "trimmed label");
        });

        Run(report, "labels.blank_scan_is_stable_and_deduplicated", () =>
        {
            var one = new StubSource { Member = null };
            var two = new StubSource { Member = "" };
            var three = new StubSource { Member = " \t" };
            var four = new StubSource { Member = " Unassigned " };
            IGizmoSource[] sources = { one, two, three, four, one };
            int before = fixture.Members.Count;
            fixture.RosterFrom(sources);
            Equal(before + 1, fixture.Members.Count, "one shared blank-label member is registered");
            GizmoMember unassigned = fixture.Find("Unassigned");
            Require(unassigned != null, "Unassigned member is registered");
            Equal(4, unassigned.Sources.Count, "duplicate source references are excluded");
            fixture.RosterFrom(sources);
            Equal(before + 1, fixture.Members.Count, "second scan creates no additional member");
            Equal(4, unassigned.Sources.Count, "second scan retains four unique sources");
            Require(ReferenceEquals(unassigned, fixture.Find(null)), "Find uses normalized blank key");
            Require(ReferenceEquals(unassigned, GizmoMemberSet.ResolveMember(" \t")), "Resolve uses same blank key");
            Require(ReferenceEquals(unassigned, GizmoRender.ResolveMember(one)), "draw-time resolution agrees with scan");
            Equal(4, unassigned.Sources.Count, "draw-time attach does not duplicate source");
        });

        Run(report, "labels.unique_add_and_validated_rename", () =>
        {
            GizmoMember first = fixture.AddMember("  Unique  ");
            GizmoMember second = fixture.AddMember("Unique");
            Equal("Unique", first.Label, "added key is trimmed");
            Require(!string.Equals(first.Label, second.Label, StringComparison.Ordinal), "added labels are unique");
            var source = new StubSource { Member = first.Label };
            GizmoRender.ResolveMember(source);
            string error;
            Require(!fixture.TryRename(second, " \t", out error), "blank rename rejected");
            Require(!string.IsNullOrEmpty(error), "blank rejection explains the error");
            Require(!fixture.TryRename(second, " Unique ", out error), "normalized duplicate rename rejected");
            Require(!string.IsNullOrEmpty(error), "duplicate rejection explains the error");
            Require(fixture.TryRename(first, " Unique ", out error), "self rename to normalized same key succeeds");
            Require(fixture.TryRename(first, " Renamed ", out error), "unique normalized rename succeeds");
            Equal("Renamed", first.Label, "renamed key is trimmed");
            Equal("Unique", source.Member, "rename does not mutate source declarations");
            Equal(0, first.Sources.Count, "renamed member no longer lists old-label source");
        });

        Run(report, "roster.regroup_removes_old_membership", () =>
        {
            GizmoMember oldMember = fixture.EnsureMember("Regroup old");
            GizmoMember newMember = fixture.EnsureMember("Regroup new");
            var source = new StubSource { Member = oldMember.Label };
            GizmoRender.ResolveMember(source);
            Equal(1, oldMember.Sources.Count, "source initially attached to old member");
            source.Member = " Regroup new ";
            Equal(0, oldMember.Sources.Count, "reading old roster prunes changed membership");
            Require(ReferenceEquals(newMember, GizmoRender.ResolveMember(source)), "regroup resolves normalized key");
            Equal(1, newMember.Sources.Count, "source attached to new member once");
            GizmoRender.ResolveMember(source);
            Equal(1, newMember.Sources.Count, "repeated resolve does not duplicate membership");
        });
    }

    private static void CheckDispatchState(Report report, GizmoMemberSet fixture, GizmoCanvas sharedCanvas)
    {
        GizmoRender.Policy = GizmoPolicy.Default;
        GizmoMember outerMember = fixture.EnsureMember("State outer");
        GizmoMember innerMember = fixture.EnsureMember("State inner");
        outerMember.Presence = GizmoPresence.Always;
        innerMember.Presence = GizmoPresence.Always;
        outerMember.Color = new Color(0.81f, 0.17f, 0.24f, 0.69f);
        innerMember.Color = new Color(0.12f, 0.37f, 0.91f, 0.58f);
        Color sentinelColor = new Color(0.31f, 0.46f, 0.57f, 0.68f);
        Matrix4x4 sentinelMatrix = Matrix4x4.TRS(new Vector3(4, 5, 6), Quaternion.Euler(9, 17, 23), Vector3.one * 2);

        Run(report, "dispatch.nested_draw_restores_all_state", () =>
        {
            Color baseBefore = sharedCanvas.BaseColor;
            Gizmos.color = sentinelColor;
            Gizmos.matrix = sentinelMatrix;
            Color outerStroke = new Color(0.45f, 0.52f, 0.63f, 0.74f);
            Matrix4x4 outerSpace = Matrix4x4.Translate(new Vector3(8, 9, 10));
            var inner = new StubSource { Member = innerMember.Label };
            inner.Paint = canvas =>
            {
                SameColor(innerMember.Color, canvas.BaseColor, "inner base color");
                SameStroke(innerMember.Color, Gizmos.color, "inner starting color");
                SameMatrix(Matrix4x4.identity, Gizmos.matrix, "inner starts in world space");
                canvas.Color = Color.yellow;
                canvas.Matrix = Matrix4x4.Scale(Vector3.one * 3);
            };
            var outer = new StubSource { Member = outerMember.Label };
            outer.Paint = canvas =>
            {
                SameColor(outerMember.Color, canvas.BaseColor, "outer base color");
                SameStroke(outerMember.Color, Gizmos.color, "outer starting color");
                SameMatrix(Matrix4x4.identity, Gizmos.matrix, "outer starts in world space");
                canvas.Color = outerStroke;
                canvas.Matrix = outerSpace;
                GizmoRender.Draw(inner, false);
                SameColor(outerMember.Color, canvas.BaseColor, "inner restores outer base color");
                SameStroke(outerStroke, Gizmos.color, "inner restores outer stroke");
                SameMatrix(outerSpace, Gizmos.matrix, "inner restores outer space");
            };
            GizmoRender.Draw(outer, false);
            Equal(1, outer.DrawCalls, "outer painted once");
            Equal(1, inner.DrawCalls, "inner painted once");
            SameStroke(sentinelColor, Gizmos.color, "nested draw restores caller color");
            SameMatrix(sentinelMatrix, Gizmos.matrix, "nested draw restores caller matrix");
            SameColor(baseBefore, sharedCanvas.BaseColor, "nested draw restores prior BaseColor");
        });

        Run(report, "dispatch.exception_restores_all_state", () =>
        {
            Color baseBefore = sharedCanvas.BaseColor;
            Gizmos.color = sentinelColor;
            Gizmos.matrix = sentinelMatrix;
            var expectedException = new InvalidOperationException("GizmoRenderChecks expected source failure");
            var source = new StubSource { Member = outerMember.Label };
            source.Paint = canvas =>
            {
                SameColor(outerMember.Color, canvas.BaseColor, "exception source starting base color");
                SameMatrix(Matrix4x4.identity, Gizmos.matrix, "exception source starts in world space");
                canvas.Color = Color.black;
                canvas.Matrix = Matrix4x4.Scale(Vector3.one * 5);
                throw expectedException;
            };
            Exception observed = null;
            try { GizmoRender.Draw(source, false); }
            catch (Exception exception) { observed = exception; }
            Require(ReferenceEquals(expectedException, observed), "original source exception propagates");
            Equal(1, source.DrawCalls, "throwing source painted once");
            SameStroke(sentinelColor, Gizmos.color, "exception restores caller color");
            SameMatrix(sentinelMatrix, Gizmos.matrix, "exception restores caller matrix");
            SameColor(baseBefore, sharedCanvas.BaseColor, "exception restores prior BaseColor");
        });
    }

    private static void CheckRealCallbacks(Report report)
    {
        string[] names =
        {
            "InteriorMapBakeVolume", "InteriorMapBakeDirection", "Mine.Fields.WindSource",
            "Mine.Fields.WindFieldProvider", "FishSimulation", "RainSimulation"
        };
        foreach (string name in names)
        {
            string typeName = name;
            Run(report, "consumer.callbacks." + typeName, () =>
            {
                Type type = FindType(typeName);
                Require(type != null, "consumer type exists: " + typeName);
                Require(typeof(IGizmoSource).IsAssignableFrom(type), "consumer implements IGizmoSource: " + typeName);
                foreach (string callback in new[] { "OnDrawGizmos", "OnDrawGizmosSelected" })
                {
                    MethodInfo method = type.GetMethod(callback, BindingFlags.Instance | BindingFlags.Public |
                        BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                    Require(method != null, "consumer declares " + callback);
                    Equal(typeof(void), method.ReturnType, callback + " returns void");
                    Equal(0, method.GetParameters().Length, callback + " has no parameters");
                }
            });
        }
    }

    private static GizmoCanvas GetSharedCanvas()
    {
        foreach (FieldInfo field in typeof(GizmoRender).GetFields(BindingFlags.Static | BindingFlags.NonPublic))
            if (field.FieldType == typeof(GizmoCanvas)) return (GizmoCanvas)field.GetValue(null);
        throw new CheckFailure("GizmoRender shared canvas was not found");
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

    private static void RestorePreference(string key, bool existed, int value)
    {
        if (existed) EditorPrefs.SetInt(key, value);
        else EditorPrefs.DeleteKey(key);
    }

    private static void Run(Report report, string name, Action test)
    {
        try
        {
            test();
            report.passed++;
            report.details.Add("PASS " + name);
        }
        catch (Exception exception) { Fail(report, name, exception); }
    }

    private static void Cleanup(Report report, string name, Action action)
    {
        try { action(); }
        catch (Exception exception) { Fail(report, name, exception); }
    }

    private static void Fail(Report report, string name, Exception exception)
    {
        report.failed++;
        report.details.Add("FAIL " + name + ": " + exception.GetType().Name + ": " + exception.Message);
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

    private static void SameStroke(Color expected, Color actual, string message)
    {
        SameColor((Color)(Color32)expected, actual, message);
    }

    private static void SameColor(Color expected, Color actual, string message)
    {
        Require(Mathf.Abs(expected.r - actual.r) < 0.00001f && Mathf.Abs(expected.g - actual.g) < 0.00001f &&
                Mathf.Abs(expected.b - actual.b) < 0.00001f && Mathf.Abs(expected.a - actual.a) < 0.00001f,
            message + "; expected=" + expected + ", actual=" + actual);
    }

    private static void SameMatrix(Matrix4x4 expected, Matrix4x4 actual, string message)
    {
        for (int index = 0; index < 16; index++)
            Require(Mathf.Abs(expected[index] - actual[index]) < 0.00001f,
                message + "; element=" + index + ", expected=" + expected[index] + ", actual=" + actual[index]);
    }
}
