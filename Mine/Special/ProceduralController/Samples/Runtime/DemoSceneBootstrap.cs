using CelToon.PathGenerator.Runtime;
using CelToon.ProceduralController.Runtime;
using System;
using UnityEngine;
using CelToon.ProceduralController.Contracts;
using CelToon.ProceduralController.Execution;
using CelToon.ProceduralController.Environment;

namespace CelToon.ProceduralController.Samples
{
    /// <summary>Generates explicit bone fixtures and a controllable test panel inside the saved scene.</summary>
    public sealed class DemoSceneBootstrap : MonoBehaviour
    {
        private static readonly TestMorphology[] Shapes =
        {
            TestMorphology.Quadruped, TestMorphology.Hexapod, TestMorphology.Octopod,
            TestMorphology.Snake, TestMorphology.Humanoid, TestMorphology.Jellyfish, TestMorphology.Octopus
        };
        private LocomotionPipelineHost[] actors;
        private Camera testCamera;
        private int selected;
        private float nextKeyboardRoute;
        private string sceneError;
        private Material[] materials;
        private GraspableObject graspTarget;
        private Material graspMaterial;
        private bool focusedCamera;
        private bool showPanel = true;
        private int keyboardActor = -1;

        public LocomotionPipelineHost[] Actors => actors;
        public string SceneError => sceneError;

        private void Awake()
        {
            var oldActor = GameObject.Find("LocomotionTestActor");
            if (oldActor) oldActor.SetActive(false);
            materials = new[]
            {
                MakeMaterial(new Color(0.18f, 0.72f, 0.9f)),
                MakeMaterial(new Color(0.25f, 0.85f, 0.48f)),
                MakeMaterial(new Color(0.9f, 0.57f, 0.2f)),
                MakeMaterial(new Color(0.68f, 0.4f, 0.9f)),
                MakeMaterial(new Color(0.94f, 0.3f, 0.38f)),
                MakeMaterial(new Color(0.4f, 0.88f, 1f)),
                MakeMaterial(new Color(0.88f, 0.32f, 0.68f))
            };
            actors = new LocomotionPipelineHost[Shapes.Length];
            for (int i = 0; i < actors.Length; i++)
            {
                float x = (i - 3) * 5.2f;
                actors[i] = CreateActor(Shapes[i], new Vector3(x, 0.08f, -6f), materials[i]);
                if (!actors[i].IsInitialized) sceneError = Shapes[i] + ": " + actors[i].LastError;
            }
            PlaceGraspTarget();
            testCamera = FindObjectOfType<Camera>();
            if (testCamera)
            {
                testCamera.transform.position = new Vector3(0f, 19f, -26f);
                testCamera.transform.LookAt(new Vector3(0f, 0f, 1f));
                testCamera.orthographic = true;
                testCamera.orthographicSize = 19f;
                testCamera.gameObject.tag = "MainCamera";
            }
        }

        private void Update()
        {
            if (actors == null) return;
            for (int i = 0; i < actors.Length; i++)
                if (Input.GetKeyDown((KeyCode)((int)KeyCode.Alpha1 + i))) selected = i;
            if (Input.GetKeyDown(KeyCode.Space)) actors[selected].Stop();
            if (Input.GetKeyDown(KeyCode.R)) { keyboardActor = -1; ResetRoutes(); }
            if (Input.GetKeyDown(KeyCode.G)) PlaceGraspTarget();
            if (Input.GetKeyDown(KeyCode.F))
            {
                focusedCamera = !focusedCamera;
                if (!focusedCamera && testCamera)
                {
                    testCamera.transform.position = new Vector3(0f, 19f, -26f);
                    testCamera.transform.LookAt(new Vector3(0f, 0f, 1f)); testCamera.orthographicSize = 19f;
                }
            }
            if (Input.GetKeyDown(KeyCode.Tab)) showPanel = !showPanel;
            if (Input.GetMouseButtonDown(1) && testCamera)
            {
                Ray ray = testCamera.ScreenPointToRay(Input.mousePosition);
                if (Physics.Raycast(ray, out var hit, 100f, 1, QueryTriggerInteraction.Ignore))
                    actors[selected].SetDestination(hit.point);
            }
            Vector3 direction = new Vector3((Input.GetKey(KeyCode.D) ? 1f : 0f) - (Input.GetKey(KeyCode.A) ? 1f : 0f),
                0f, (Input.GetKey(KeyCode.W) ? 1f : 0f) - (Input.GetKey(KeyCode.S) ? 1f : 0f));
            if (keyboardActor >= 0 && (direction.sqrMagnitude == 0f || keyboardActor != selected || Input.GetMouseButtonDown(1)))
            {
                // Do not cancel a right-click route that was just submitted.
                if (!Input.GetMouseButtonDown(1) || keyboardActor != selected) actors[keyboardActor].Stop();
                keyboardActor = -1;
            }
            if (direction.sqrMagnitude > 0f && Time.time >= nextKeyboardRoute && !Input.GetMouseButtonDown(1))
            {
                nextKeyboardRoute = Time.time + 0.12f;
                actors[selected].SetDestination(actors[selected].MotionAnchor.position + direction.normalized * 4f);
                keyboardActor = selected;
            }
        }

        private void LateUpdate()
        {
            if (!focusedCamera || !testCamera || actors == null) return;
            Vector3 center = actors[selected].Body.position;
            if (actors[selected].Morphology == TestMorphology.Jellyfish) center -= Vector3.up;
            if (actors[selected].Morphology == TestMorphology.Snake) center -= actors[selected].MotionAnchor.forward * 1.4f;
            testCamera.transform.position = center + new Vector3(4f, 6f, -7f);
            testCamera.transform.LookAt(center); testCamera.orthographicSize = 3.2f;
        }

        private void OnDestroy()
        {
            if (materials != null) foreach (var material in materials) if (material) Destroy(material);
            if (graspMaterial) Destroy(graspMaterial);
        }

        private void ResetRoutes()
        {
            for (int i = 0; i < actors.Length; i++)
                actors[i].SetDestination(new Vector3(actors[i].MotionAnchor.position.x, actors[i].MotionAnchor.position.y, 6f));
        }

        public void PlaceGraspTarget()
        {
            if (actors == null) return;
            var octopus = actors[6];
            if (!graspTarget)
            {
                var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                sphere.name = "Grasp_TestSphere"; sphere.transform.SetParent(transform); sphere.layer = 9;
                sphere.transform.localScale = Vector3.one * 0.8f;
                graspMaterial = MakeMaterial(new Color(1f, 0.85f, 0.18f));
                sphere.GetComponent<Renderer>().sharedMaterial = graspMaterial;
                graspTarget = sphere.AddComponent<GraspableObject>(); graspTarget.Configure(0.5f);
            }
            graspTarget.transform.position = octopus.MotionAnchor.position + new Vector3(0f, 0.55f, 1.35f);
        }

        private void OnGUI()
        {
            if (actors == null) return;
            if (!showPanel) { GUI.Label(new Rect(12, 12, 550, 24), "Tab panel | F close view | 1-7 select | WASD move | G grasp object"); return; }
            GUILayout.BeginArea(new Rect(12, 12, 670, 650), GUI.skin.box);
            GUILayout.Label("1-7 select | WASD body/head | Right click goal | Space stop | R demo | G grasp object | F view | Tab panel");
            GUILayout.BeginHorizontal();
            for (int i = 0; i < actors.Length; i++)
                if (GUILayout.Button((i == selected ? "> " : "") + Shapes[i], GUILayout.Width(90))) selected = i;
            GUILayout.EndHorizontal();
            var actor = actors[selected];
            var planner = actor.GetComponent<ProceduralAnimationManager>();
            planner.MoveSpeed = ParameterSlider("Base max speed (m/s)", planner.MoveSpeed, .1f, 5f);
            planner.Acceleration = ParameterSlider("Acceleration (m/s2)", planner.Acceleration, .1f, 12f);
            planner.Deceleration = ParameterSlider("Deceleration (m/s2)", planner.Deceleration, .1f, 12f);
            planner.StoppingDistance = ParameterSlider("Stop distance (m)", planner.StoppingDistance, 0f, 1f);
            if (planner.UsesLegGait)
            {
                planner.CoupleSpeedToStride = GUILayout.Toggle(planner.CoupleSpeedToStride, "Link stride frequency and travel speed");
                planner.StrideFrequency = ParameterSlider("Stride frequency multiplier", planner.StrideFrequency, .2f, 3f);
                planner.TurnStrideMultiplier = ParameterSlider("Turn stride multiplier", planner.TurnStrideMultiplier, 1f, 4f);
                GUILayout.Label("Effective stride: " + planner.EffectiveStrideFrequency.ToString("F2") +
                    " Hz | travel speed limit: " + planner.GaitSpeedLimit.ToString("F2") + " m/s");
            }
            planner.StepHeight = ParameterSlider("Step height (m)", planner.StepHeight, 0f, .4f);
            GUILayout.Label("Planned speed: " + planner.PlannedVelocityWS.magnitude.ToString("F2") +
                " | actual speed: " + new Vector2(actor.LastMotionResult.ActualVelocityWS.x, actor.LastMotionResult.ActualVelocityWS.z).magnitude.ToString("F2") + " m/s");
            GUILayout.Label("Selected: " + actor.name + "  route: " + (actor.LastRoute?.Status.ToString() ?? "None") +
                "  version: " + (actor.LastRoute?.RouteVersion ?? 0) +
                "  points: " + (actor.LastRoute?.Waypoints?.Length ?? 0));
            GUILayout.Label("Plan modules: " + (actor.LastPlan?.ModuleIds.Length ?? 0) +
                "  solved frames: " + actor.SuccessfulFrames + "  failed: " + actor.FailedFrames);
            GUILayout.Label("Max foot reach error: " + actor.LastMaxReachError.ToString("F3") + " m");
            GUILayout.Label("Error: " + (sceneError ?? actor.LastError ?? "none"));
            GUILayout.Label("Input part: " + actor.MotionAnchor.name + " | propulsion " + actor.Propulsion.ToString("F2") +
                " | automatically grasping arms " + actor.GraspingTentacles);
            foreach (var item in actor.LastFeedback)
                GUILayout.Label(item.Module.Value + "  " + item.State + "  reach " + item.ReachError.ToString("F3") + " m");
            if (GUILayout.Button("Move all actors to route goals")) ResetRoutes();
            if (GUILayout.Button("Place grasp sphere near octopus (automatic grasp)")) PlaceGraspTarget();
            GUILayout.EndArea();
        }

        private static float ParameterSlider(string label, float value, float minimum, float maximum)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(260));
            float result = GUILayout.HorizontalSlider(value, minimum, maximum, GUILayout.Width(240));
            GUILayout.Label(result.ToString("F2"), GUILayout.Width(65));
            GUILayout.EndHorizontal();
            return result;
        }

        private LocomotionPipelineHost CreateActor(TestMorphology shape, Vector3 position, Material material)
        {
            var root = new GameObject(shape.ToString() + "_Rig");
            root.transform.SetParent(transform);
            root.transform.position = position;
            root.layer = 8;
            bool jellyfish = shape == TestMorphology.Jellyfish;
            Transform driver = MakeBone(root.transform, shape == TestMorphology.Snake ? "HeadInput" : "BodyInput",
                position + (jellyfish ? Vector3.up * 3f : Vector3.zero));
            driver.gameObject.layer = 8;
            var character = driver.gameObject.AddComponent<CharacterController>();
            character.height = shape == TestMorphology.Humanoid ? 1.9f : 1.1f;
            character.radius = shape == TestMorphology.Snake ? 0.3f : 0.38f;
            character.center = Vector3.up * (character.height * 0.5f);
            character.stepOffset = 0.25f;
            var nav = root.AddComponent<NavigationManager>();
            var planner = root.AddComponent<ProceduralAnimationManager>();
            var rootController = root.AddComponent<RootMotionController>();
            rootController.Configure(character, driver, jellyfish);
            root.AddComponent<PhysicsGroundProbe>();
            var binding = root.AddComponent<RigBinding>();
            var host = root.AddComponent<LocomotionPipelineHost>();
            if (jellyfish || shape == TestMorphology.Octopus)
            {
                Transform body = MakeBone(root.transform, jellyfish ? "Bell" : "Body", driver.position + Vector3.up * (jellyfish ? 0.15f : 0.85f));
                MakePrimitive(body, PrimitiveType.Sphere, "BodyMesh", body.position,
                    jellyfish ? new Vector3(1.25f, 0.6f, 1.25f) : new Vector3(1f, 0.85f, 1f), material);
                var controllers = new IModuleController[9];
                var bodyController = root.AddComponent<PoseModuleController>();
                bodyController.Configure("Body", ModuleKind.Body, body); controllers[0] = bodyController;
                var independent = new GameObject("IndependentTentacles"); independent.transform.SetParent(root.transform);
                for (int arm = 0; arm < 8; arm++)
                {
                    float angle = arm * Mathf.PI * 2f / 8f;
                    Vector3 radial = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                    Vector3 anchor = body.position + radial * 0.35f - Vector3.up * 0.12f;
                    var bones = new Transform[13];
                    for (int point = 0; point < bones.Length; point++)
                    {
                        Vector3 offset = jellyfish ? Vector3.down * point * 0.23f :
                            radial * point * 0.18f - Vector3.up * Mathf.Min(0.65f, point * 0.10f);
                        bones[point] = MakeBone(independent.transform, "Tentacle" + arm + "_" + point, anchor + offset);
                        float radius = Mathf.Lerp(0.085f, 0.018f, point / 12f);
                        MakePrimitive(bones[point], PrimitiveType.Sphere, "JointMesh", bones[point].position, Vector3.one * radius * 2f, material);
                        if (point > 0)
                        {
                            var link = MakePrimitive(independent.transform, PrimitiveType.Cylinder, "ArmMesh", Vector3.zero, Vector3.one, material);
                            link.AddComponent<BoneVisualLink>().Configure(bones[point - 1], bones[point], radius);
                        }
                    }
                    var controller = root.AddComponent<ChainModuleController>();
                    controller.Configure("Tentacle" + arm, ModuleKind.Tentacle, bones, 0.23f); controllers[arm + 1] = controller;
                }
                binding.Configure(host.GetInstanceID(), body, Array.Empty<RigBinding.ModuleBinding>());
                host.Configure(shape, planner, rootController, binding, controllers, 0);
            }
            else if (shape == TestMorphology.Snake)
            {
                Transform head = MakeBone(root.transform, "Head", position + Vector3.up * 0.32f);
                MakePrimitive(head, PrimitiveType.Sphere, "HeadMesh", head.position, new Vector3(0.65f, 0.46f, 0.7f), material);
                var bones = new Transform[11]; bones[0] = head;
                Transform previous = head;
                for (int i = 0; i < 10; i++)
                {
                    Transform segment = MakeBone(root.transform, "Spine" + i.ToString("00"), position + new Vector3(0f, 0.32f, -(i + 1) * 0.43f));
                    MakePrimitive(segment, PrimitiveType.Sphere, "SegmentMesh", segment.position, Vector3.one * (0.58f - i * 0.027f), material);
                    bones[i + 1] = segment;
                    var link = MakePrimitive(root.transform, PrimitiveType.Cylinder, "SpineLink" + i.ToString("00"),
                        Vector3.zero, Vector3.one, material);
                    link.AddComponent<BoneVisualLink>().Configure(previous, segment, 0.17f - i * 0.009f);
                    previous = segment;
                }
                binding.Configure(host.GetInstanceID(), head, Array.Empty<RigBinding.ModuleBinding>());
                var controller = root.AddComponent<ChainModuleController>();
                controller.Configure("Spine", ModuleKind.Spine, bones, 0.43f);
                host.Configure(shape, planner, rootController, binding, new IModuleController[] { controller }, 10);
            }
            else
            {
                bool human = shape == TestMorphology.Humanoid;
                int limbCount = human ? 2 : shape == TestMorphology.Quadruped ? 4 : shape == TestMorphology.Hexapod ? 6 : 8;
                float bodyHeight = human ? 1.28f : 0.88f;
                Transform body = MakeBone(root.transform, "Body", position + Vector3.up * bodyHeight);
                MakePrimitive(body, human ? PrimitiveType.Capsule : PrimitiveType.Cube, "BodyMesh", body.position,
                    human ? new Vector3(0.58f, 0.52f, 0.36f) : new Vector3(1.25f, 0.35f, 1.9f), material);
                if (human) BuildHumanDetails(body, material);
                var modules = new RigBinding.ModuleBinding[limbCount];
                var controllers = new IModuleController[limbCount + 1];
                var bodyController = root.AddComponent<PoseModuleController>();
                bodyController.Configure("Body", ModuleKind.Body, body);
                controllers[0] = bodyController;
                for (int i = 0; i < limbCount; i++)
                {
                    float side = i % 2 == 0 ? -1f : 1f;
                    float longitudinal = human ? 0f : 0.7f - (i / 2) * (1.4f / Mathf.Max(1f, limbCount / 2f - 1f));
                    Vector3 upperPoint = position + new Vector3(side * (human ? 0.25f : 0.62f), human ? 0.96f : 0.83f, longitudinal);
                    // Bent reference knee leaves reach reserve for planted feet and pelvis bob.
                    Vector3 lowerPoint = position + new Vector3(side * (human ? 0.32f : 1.07f), human ? 0.51f : 0.45f, longitudinal + (human ? 0.42f : 0.05f));
                    Vector3 footPoint = position + new Vector3(side * (human ? 0.34f : 1.05f), 0.06f, longitudinal + 0.18f);
                    Transform upper = MakeBone(body, "Leg" + i + "_Upper", upperPoint);
                    Transform lower = MakeBone(upper, "Leg" + i + "_Lower", lowerPoint);
                    Transform foot = MakeBone(lower, "Leg" + i + "_Foot", footPoint);
                    DrawSegment(upper, upperPoint, lowerPoint, human ? 0.11f : 0.075f, material);
                    DrawSegment(lower, lowerPoint, footPoint, human ? 0.095f : 0.065f, material);
                    MakePrimitive(foot, PrimitiveType.Sphere, "FootMesh", footPoint, Vector3.one * 0.14f, material);
                    string id = "Leg" + i;
                    modules[i] = new RigBinding.ModuleBinding { ModuleId = id, Kind = ModuleKind.Limb, Required = true, Upper = upper, Lower = lower, Foot = foot };
                    var controller = root.AddComponent<LimbIKController>();
                    controller.Configure(binding, id);
                    controllers[i + 1] = controller;
                }
                binding.Configure(host.GetInstanceID(), body, modules);
                host.Configure(shape, planner, rootController, binding, controllers, 0);
            }
            host.UseRouteProvider(nav);
            return host;
        }

        private static void BuildHumanDetails(Transform body, Material material)
        {
            Vector3 center = body.position;
            Transform head = MakeBone(body, "Head", center + Vector3.up * 0.75f);
            MakePrimitive(head, PrimitiveType.Sphere, "HeadMesh", head.position, Vector3.one * 0.38f, material);
            for (int side = -1; side <= 1; side += 2)
            {
                Vector3 shoulderPoint = center + new Vector3(side * 0.42f, 0.25f, 0f);
                Vector3 elbowPoint = center + new Vector3(side * 0.68f, -0.18f, 0f);
                Vector3 handPoint = center + new Vector3(side * 0.7f, -0.58f, 0.08f);
                Transform shoulder = MakeBone(body, side < 0 ? "LeftArmUpper" : "RightArmUpper", shoulderPoint);
                Transform elbow = MakeBone(shoulder, "Forearm", elbowPoint);
                MakeBone(elbow, "Hand", handPoint);
                DrawSegment(shoulder, shoulderPoint, elbowPoint, 0.08f, material);
                DrawSegment(elbow, elbowPoint, handPoint, 0.07f, material);
            }
        }

        private static Transform MakeBone(Transform parent, string name, Vector3 worldPoint)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent);
            go.transform.position = worldPoint;
            return go.transform;
        }

        private static void DrawSegment(Transform parent, Vector3 a, Vector3 b, float radius, Material material)
        {
            Vector3 difference = b - a;
            var mesh = MakePrimitive(parent, PrimitiveType.Capsule, "BoneMesh", (a + b) * 0.5f,
                new Vector3(radius * 2f, difference.magnitude * 0.5f, radius * 2f), material);
            mesh.transform.rotation = Quaternion.FromToRotation(Vector3.up, difference.normalized);
        }

        private static GameObject MakePrimitive(Transform parent, PrimitiveType type, string name, Vector3 point, Vector3 scale, Material material)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name; go.transform.SetParent(parent); go.transform.position = point; go.transform.localScale = scale;
            var collider = go.GetComponent<Collider>(); if (collider) Destroy(collider);
            var renderer = go.GetComponent<Renderer>(); if (renderer) renderer.sharedMaterial = material;
            return go;
        }

        private static Material MakeMaterial(Color color)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (!shader) shader = Shader.Find("Standard");
            var material = new Material(shader);
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            else material.color = color;
            return material;
        }
    }

    public sealed class BoneVisualLink : MonoBehaviour
    {
        private Transform start;
        private Transform end;
        private float radius;
        public void Configure(Transform a, Transform b, float width) { start = a; end = b; radius = width; }
        private void LateUpdate()
        {
            if (!start || !end) return;
            Vector3 offset = end.position - start.position;
            transform.position = (start.position + end.position) * 0.5f;
            transform.rotation = Quaternion.FromToRotation(Vector3.up, offset.normalized);
            transform.localScale = new Vector3(radius * 2f, offset.magnitude * 0.5f, radius * 2f);
        }
    }
}
