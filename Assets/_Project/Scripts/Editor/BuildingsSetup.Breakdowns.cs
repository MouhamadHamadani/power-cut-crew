using System.IO;
using PowerCutCrew.Breakdowns;
using PowerCutCrew.Buildings;
using PowerCutCrew.Core;
using PowerCutCrew.Generator;
using PowerCutCrew.Net;
using PowerCutCrew.Shift;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEditor;
using UnityEditor.Localization;
using UnityEngine;
using UnityEngine.Localization.Tables;

namespace PowerCutCrew.Editor
{
    /// <summary>Prompt 07: the breakdown content: tuning and catalog assets, the wrench and spare-belt prefabs, the parts on the generator prefab (sweet spots, belt slot, breaker lever, crate, wrench hook, smoke, sparks, markers), icons and strings. Re-runnable.</summary>
    public static partial class BuildingsSetup
    {
        private static readonly Vector3[] SweetSpotPositions =
        {
            new Vector3(-0.30f, 0.70f, -0.60f),   // back, upper left
            new Vector3(0.30f, 0.40f, -0.60f),    // back, lower right
            new Vector3(0.38f, 0.85f, 0.60f),     // front, upper right
            new Vector3(-0.38f, 0.30f, 0.60f),    // front, lower left
        };

        private static readonly Vector3 BeltSlotPosition = new Vector3(0.30f, 0.22f, 0.62f);
        private static readonly Vector3 BreakerBoxPosition = new Vector3(-0.38f, 1.10f, -0.28f);
        private static readonly Vector3 CratePosition = new Vector3(1.45f, 0.25f, -1.0f);
        private static readonly Vector3 HookPosition = new Vector3(-1.25f, 0.95f, -0.9f);

        [MenuItem("Power Cut Crew/Setup/Build Breakdowns")]
        public static void RunBreakdowns()
        {
            Directory.CreateDirectory($"{Root}/Config/Resources");
            Directory.CreateDirectory($"{Root}/Prefabs/Breakdowns");
            Directory.CreateDirectory($"{Root}/Art/Materials");
            AssetDatabase.Refresh();
            ContentSetup.LoadFonts();

            EditorUtility.SetDirty(LoadOrCreate<BreakdownTuning>($"{Root}/Config/Resources/BreakdownTuning.asset"));
            EditorUtility.SetDirty(AssetDatabase.LoadAssetAtPath<PlayerTuning>($"{Root}/Config/Resources/PlayerTuning.asset"));   // saved again so the name tag fields show up in the asset
            CreateBreakdownStrings();

            GameObject wrench = SaveBreakdownPrefab(BuildWrench(), "Wrench");
            GameObject belt = SaveBreakdownPrefab(BuildSpareBelt(), "SpareBelt");
            var catalog = LoadOrCreate<BreakdownCatalog>($"{Root}/Config/Resources/BreakdownCatalog.asset");
            catalog.Wrench = wrench.GetComponent<NetworkObject>();
            catalog.Belt = belt.GetComponent<NetworkObject>();
            EditorUtility.SetDirty(catalog);

            PatchGeneratorPrefab();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[Setup] breakdowns built");
        }

        private static GameObject SaveBreakdownPrefab(GameObject go, string name)
        {
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(go, $"{Root}/Prefabs/Breakdowns/{name}.prefab");
            Object.DestroyImmediate(go);
            return prefab;
        }

        private static void SetInter(Interactable i, string key, float range, bool hold)
        {
            var so = new SerializedObject(i);
            so.FindProperty("_promptKey").stringValue = key;
            so.FindProperty("_maxRange").floatValue = range;
            so.FindProperty("_hold").boolValue = hold;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetBool(Object target, string field, bool value)
        {
            var so = new SerializedObject(target);
            so.FindProperty(field).boolValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetString(Object target, string field, string value)
        {
            var so = new SerializedObject(target);
            so.FindProperty(field).stringValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static Material ParticleMat(string name, Color color)
        {
            string path = $"{Root}/Art/Materials/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit")) { name = name };
                AssetDatabase.CreateAsset(m, path);
            }
            m.SetColor("_BaseColor", color);
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 0f);
            m.SetFloat("_SrcBlend", 5f);
            m.SetFloat("_DstBlend", 10f);
            m.SetFloat("_ZWrite", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = 3000;
            EditorUtility.SetDirty(m);
            return m;
        }

        // ---------- the wrench and the spare belt ----------

        private static NetworkTransform AddServerTransform(GameObject go)
        {
            var nt = go.AddComponent<NetworkTransform>();
            nt.AuthorityMode = NetworkTransform.AuthorityModes.Server;
            nt.SyncScaleX = nt.SyncScaleY = nt.SyncScaleZ = false;
            nt.UseUnreliableDeltas = true;
            nt.Interpolate = true;
            return nt;
        }

        private static GameObject BuildWrench()
        {
            var root = new GameObject("Wrench");
            var rb = root.AddComponent<Rigidbody>();
            rb.mass = 2f;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            var box = root.AddComponent<BoxCollider>();
            box.size = new Vector3(0.14f, 0.1f, 0.6f);
            box.center = new Vector3(0f, 0f, 0.05f);
            var visual = new GameObject("Visual");
            visual.transform.SetParent(root.transform, false);
            Material grip = Mat("WrenchGrip", new Color(0.9f, 0.32f, 0.08f));
            Material steel = Mat("WrenchSteel", new Color(0.62f, 0.66f, 0.74f));
            GameObject handle = Cube("Handle", visual.transform, new Vector3(0f, 0f, -0.05f), new Vector3(0.06f, 0.05f, 0.42f), grip, false);
            GameObject neck = Cube("Neck", visual.transform, new Vector3(0f, 0f, 0.2f), new Vector3(0.05f, 0.04f, 0.1f), steel, false);
            GameObject head = Cube("Head", visual.transform, new Vector3(0f, 0f, 0.29f), new Vector3(0.14f, 0.05f, 0.1f), steel, false);
            GameObject jawA = Cube("JawA", visual.transform, new Vector3(-0.06f, 0f, 0.35f), new Vector3(0.035f, 0.05f, 0.07f), steel, false);
            GameObject jawB = Cube("JawB", visual.transform, new Vector3(0.06f, 0f, 0.35f), new Vector3(0.035f, 0.05f, 0.07f), steel, false);
            GameObject end = Cube("End", visual.transform, new Vector3(0f, 0f, -0.27f), new Vector3(0.1f, 0.05f, 0.05f), steel, false);

            root.AddComponent<NetworkObject>();
            AddServerTransform(root);
            var item = root.AddComponent<NetworkPhysicsItem>();
            Set(item, "_visualRoot", visual.transform);
            SetArray(item, "_renderers", new Object[] { handle.GetComponent<Renderer>(), neck.GetComponent<Renderer>(), head.GetComponent<Renderer>(), jawA.GetComponent<Renderer>(), jawB.GetComponent<Renderer>(), end.GetComponent<Renderer>() });
            SetString(item, "_kind", "wrench");
            SetBool(item, "_ignoredByWanderBots", true);
            SetInter(root.AddComponent<ItemInteractable>(), "brk.take_wrench", 3f, false);
            root.AddComponent<WrenchItem>();
            return root;
        }

        private static GameObject BuildSpareBelt()
        {
            var root = new GameObject("SpareBelt");
            var rb = root.AddComponent<Rigidbody>();
            rb.mass = 1f;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            root.AddComponent<BoxCollider>().size = new Vector3(0.32f, 0.08f, 0.32f);
            var visual = new GameObject("Visual");
            visual.transform.SetParent(root.transform, false);
            GameObject ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            ring.name = "Ring";
            Object.DestroyImmediate(ring.GetComponent<Collider>());
            ring.transform.SetParent(visual.transform, false);
            ring.transform.localScale = new Vector3(0.32f, 0.03f, 0.32f);
            ring.GetComponent<Renderer>().sharedMaterial = Mat("Belt", new Color(0.07f, 0.07f, 0.08f));
            GameObject hub = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            hub.name = "Hub";
            Object.DestroyImmediate(hub.GetComponent<Collider>());
            hub.transform.SetParent(visual.transform, false);
            hub.transform.localScale = new Vector3(0.14f, 0.04f, 0.14f);
            hub.GetComponent<Renderer>().sharedMaterial = Mat("WrenchSteel", new Color(0.62f, 0.66f, 0.74f));

            root.AddComponent<NetworkObject>();
            AddServerTransform(root);
            var item = root.AddComponent<NetworkPhysicsItem>();
            Set(item, "_visualRoot", visual.transform);
            SetArray(item, "_renderers", new Object[] { ring.GetComponent<Renderer>(), hub.GetComponent<Renderer>() });
            SetString(item, "_kind", SpareBelt.Kind);
            SetBool(item, "_ignoredByWanderBots", true);
            SetInter(root.AddComponent<ItemInteractable>(), "brk.take_belt_item", 3f, false);
            root.AddComponent<SpareBelt>();
            return root;
        }

        // ---------- the generator ----------

        private static ParticleSystem MakeParticles(string name, Transform parent, Vector3 localPosition, Vector3 euler, Color color, float lifetime, float speed, float size, float rate, float angle, float gravity)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localRotation = Quaternion.Euler(euler);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ParticleSystem.MainModule main = ps.main;
            main.playOnAwake = false;
            main.loop = true;
            main.startLifetime = lifetime;
            main.startSpeed = speed;
            main.startSize = size;
            main.startColor = color;
            main.gravityModifier = gravity;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            ParticleSystem.EmissionModule emission = ps.emission;
            emission.rateOverTime = rate;
            emission.enabled = rate > 0f;
            ParticleSystem.ShapeModule shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = angle;
            shape.radius = 0.06f;
            ParticleSystem.ColorOverLifetimeModule col = ps.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) }, new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0f, 1f) });
            col.color = grad;
            ParticleSystem.SizeOverLifetimeModule sol = ps.sizeOverLifetime;
            sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.5f, 1f, 1.7f));
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Mesh;
            r.mesh = Resources.GetBuiltinResource<Mesh>("Sphere.fbx");
            r.sharedMaterial = ParticleMat(name, color);
            return ps;
        }

        private static void PatchGeneratorPrefab()
        {
            string path = $"{Root}/Prefabs/CoreLoop/Generator.prefab";
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                // start clean: a re-run replaces what an earlier run added
                Transform old = root.transform.Find("Breakdowns");
                if (old != null) { Object.DestroyImmediate(old.gameObject); }
                foreach (Component c in new Component[] { root.GetComponent<BreakdownNet>(), root.GetComponent<BreakdownVisuals>(), root.GetComponent<BreakdownMarkers>() })
                {
                    if (c != null) { Object.DestroyImmediate(c); }
                }

                var container = new GameObject("Breakdowns").transform;
                container.SetParent(root.transform, false);
                Material dark = Mat("GeneratorParts", new Color(0.18f, 0.18f, 0.2f));
                Material glow = Mat("SweetSpotGlow", Lamp, true);

                // the four sweet spots (only one glows, and only while the engine sputters)
                var spots = new Transform[SweetSpotPositions.Length];
                var spotGlows = new Renderer[SweetSpotPositions.Length];
                for (int i = 0; i < spots.Length; i++)
                {
                    float side = SweetSpotPositions[i].z < 0f ? -1f : 1f;
                    GameObject s = Cube($"SweetSpot{i}", container, SweetSpotPositions[i] + new Vector3(0f, 0f, 0.012f * side), new Vector3(0.24f, 0.24f, 0.03f), glow, false);
                    s.GetComponent<MeshFilter>().sharedMesh = Resources.GetBuiltinResource<Mesh>("Sphere.fbx");
                    s.transform.localScale = new Vector3(0.24f, 0.24f, 0.05f);
                    s.GetComponent<Renderer>().enabled = false;
                    var marker = new GameObject($"SweetSpotPoint{i}").transform;
                    marker.SetParent(container, false);
                    marker.localPosition = SweetSpotPositions[i];
                    spots[i] = marker;
                    spotGlows[i] = s.GetComponent<Renderer>();
                }

                // the belt slot: a cover plate with a pulley, a glow plate and a fill bar
                var slot = new GameObject("BeltSlot");
                slot.transform.SetParent(container, false);
                slot.transform.localPosition = BeltSlotPosition;
                slot.AddComponent<BoxCollider>().size = new Vector3(0.36f, 0.36f, 0.16f);
                Cube("Cover", slot.transform, Vector3.zero, new Vector3(0.34f, 0.34f, 0.05f), dark, false);
                GameObject slotGlow = Cube("Glow", slot.transform, new Vector3(0f, 0f, 0.03f), new Vector3(0.28f, 0.28f, 0.02f), Mat("SlotGlow", Lamp, true), false);
                GameObject pulley = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                pulley.name = "Pulley";
                Object.DestroyImmediate(pulley.GetComponent<Collider>());
                pulley.transform.SetParent(slot.transform, false);
                pulley.transform.localPosition = new Vector3(0f, 0f, 0.05f);
                pulley.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                pulley.transform.localScale = new Vector3(0.16f, 0.025f, 0.16f);
                pulley.GetComponent<Renderer>().sharedMaterial = Mat("WrenchSteel", new Color(0.62f, 0.66f, 0.74f));
                SetInter(slot.AddComponent<BeltSlot>(), "brk.fit_belt", 3f, true);
                Transform beltBar = MakeBar("BeltBar", container, BeltSlotPosition + new Vector3(-0.17f, 0.27f, 0.04f), 0.34f);

                // the breaker box on top of the housing, with its lever
                var box = new GameObject("BreakerBox");
                box.transform.SetParent(container, false);
                box.transform.localPosition = BreakerBoxPosition;
                var boxCollider = box.AddComponent<BoxCollider>();
                boxCollider.size = new Vector3(0.32f, 0.5f, 0.3f);
                boxCollider.center = new Vector3(0f, 0.12f, 0f);
                Cube("Box", box.transform, Vector3.zero, new Vector3(0.3f, 0.2f, 0.25f), dark, false);
                var pivot = new GameObject("LeverPivot").transform;
                pivot.SetParent(box.transform, false);
                pivot.localPosition = new Vector3(0f, 0.1f, 0f);
                GameObject rod = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                rod.name = "Rod";
                Object.DestroyImmediate(rod.GetComponent<Collider>());
                rod.transform.SetParent(pivot, false);
                rod.transform.localPosition = new Vector3(0f, 0.1f, 0f);
                rod.transform.localScale = new Vector3(0.03f, 0.1f, 0.03f);
                rod.GetComponent<Renderer>().sharedMaterial = Mat("WrenchSteel", new Color(0.62f, 0.66f, 0.74f));
                GameObject knob = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                knob.name = "Knob";
                Object.DestroyImmediate(knob.GetComponent<Collider>());
                knob.transform.SetParent(pivot, false);
                knob.transform.localPosition = new Vector3(0f, 0.21f, 0f);
                knob.transform.localScale = Vector3.one * 0.09f;
                knob.GetComponent<Renderer>().sharedMaterial = Mat("LeverKnob", new Color(0.85f, 0.15f, 0.1f), true);
                SetInter(box.AddComponent<BreakerLever>(), "brk.reset_breaker", 3f, true);
                Transform resetBar = MakeBar("ResetBar", container, BreakerBoxPosition + new Vector3(-0.15f, 0.52f, 0f), 0.3f);

                // the spare-parts crate
                var crate = new GameObject("SparesCrate");
                crate.transform.SetParent(container, false);
                crate.transform.localPosition = CratePosition;
                crate.AddComponent<BoxCollider>().size = new Vector3(0.75f, 0.5f, 0.55f);
                Cube("Wood", crate.transform, Vector3.zero, new Vector3(0.75f, 0.5f, 0.55f), Mat("CrateWood", new Color(0.45f, 0.3f, 0.15f)), false);
                Cube("Lid", crate.transform, new Vector3(0f, 0.27f, 0f), new Vector3(0.79f, 0.05f, 0.59f), Mat("CrateLid", new Color(0.32f, 0.21f, 0.1f)), false);
                for (int i = 0; i < 2; i++)
                {
                    GameObject coil = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    coil.name = $"Coil{i}";
                    Object.DestroyImmediate(coil.GetComponent<Collider>());
                    coil.transform.SetParent(crate.transform, false);
                    coil.transform.localPosition = new Vector3(-0.17f + i * 0.34f, 0.33f, 0f);
                    coil.transform.localScale = new Vector3(0.26f, 0.03f, 0.26f);
                    coil.GetComponent<Renderer>().sharedMaterial = Mat("Belt", new Color(0.07f, 0.07f, 0.08f));
                }
                SetInter(crate.AddComponent<SparePartsCrate>(), "brk.take_belt", 3f, false);
                var crateSpawn = new GameObject("CrateSpawn").transform;
                crateSpawn.SetParent(container, false);
                crateSpawn.localPosition = CratePosition + new Vector3(0f, 0.7f, 0f);

                // the wrench hook: a post with a shelf the wrench lies on
                Material post = Mat("HookPost", new Color(0.3f, 0.3f, 0.33f));
                var hookPost = Cube("HookPost", container, new Vector3(HookPosition.x, HookPosition.y * 0.5f, HookPosition.z), new Vector3(0.08f, HookPosition.y, 0.08f), post, true);
                var shelf = Cube("HookShelf", container, HookPosition, new Vector3(0.7f, 0.05f, 0.3f), post, true);
                var hook = new GameObject("WrenchHook").transform;
                hook.SetParent(container, false);
                hook.localPosition = HookPosition + new Vector3(0f, 0.09f, 0f);
                hook.localRotation = Quaternion.Euler(0f, 90f, 0f);
                if (hookPost == null || shelf == null) { Debug.Log("[Setup] hook missing"); }

                // the outlets' lamps
                Transform[] outlets = new[] { root.transform.Find("Outlet0"), root.transform.Find("Outlet1") };
                var lamps = new Renderer[outlets.Length];
                for (int i = 0; i < outlets.Length; i++)
                {
                    GameObject lamp = Cube($"Lamp{i}", container, outlets[i].localPosition + new Vector3(0f, 0.2f, 0f), new Vector3(0.12f, 0.1f, 0.12f), Mat("OutletLamp", new Color(0.1f, 0.35f, 0.15f), true), false);
                    lamps[i] = lamp.GetComponent<Renderer>();
                }

                // smoke and sparks
                ParticleSystem blackSmoke = MakeParticles("BlackSmoke", container, new Vector3(0.2f, 1.1f, -0.3f), new Vector3(-90f, 0f, 0f), new Color(0.12f, 0.12f, 0.12f, 0.9f), 1.4f, 1.3f, 0.34f, 9f, 18f, -0.04f);
                ParticleSystem greySmoke = MakeParticles("GreySmoke", container, new Vector3(0.7f, 0.5f, 0.3f), new Vector3(-45f, 90f, 0f), new Color(0.62f, 0.62f, 0.64f, 0.8f), 1.6f, 1.1f, 0.3f, 8f, 20f, -0.03f);
                ParticleSystem sparks = MakeParticles("Sparks", container, BreakerBoxPosition + new Vector3(0f, 0.3f, 0f), new Vector3(-90f, 0f, 0f), new Color(1f, 0.78f, 0.2f, 1f), 0.6f, 3f, 0.06f, 0f, 60f, 0.8f);
                ParticleSystem.MainModule sm = sparks.main;
                sm.loop = false;
                ParticleSystem.EmissionModule se = sparks.emission;
                se.enabled = true;
                se.rateOverTime = 0f;
                ParticleSystem.EmissionModule be = blackSmoke.emission;
                be.enabled = false;
                ParticleSystem.EmissionModule ge = greySmoke.emission;
                ge.enabled = false;

                // the visuals
                var visuals = root.AddComponent<BreakdownVisuals>();
                Set(visuals, "_body", root.transform.Find("Body"));
                Set(visuals, "_blackSmoke", blackSmoke);
                Set(visuals, "_greySmoke", greySmoke);
                Set(visuals, "_sparks", sparks);
                SetArray(visuals, "_sweetSpotGlows", spotGlows);
                Set(visuals, "_beltSlotGlow", slotGlow.GetComponent<Renderer>());
                Set(visuals, "_leverKnob", knob.GetComponent<Renderer>());
                Set(visuals, "_leverPivot", pivot);
                SetArray(visuals, "_outletLamps", lamps);
                Set(visuals, "_beltBar", beltBar);
                Set(visuals, "_resetBar", resetBar);
                Set(visuals, "_oldBeltMaterial", Mat("Belt", new Color(0.07f, 0.07f, 0.08f)));

                // the net behaviour
                var net = root.AddComponent<BreakdownNet>();
                SetArray(net, "_sweetSpots", spots);
                Set(net, "_beltSlot", slot.transform);
                Set(net, "_lever", pivot);
                Set(net, "_crateSpawn", crateSpawn);
                Set(net, "_wrenchHook", hook);

                // the generator's markers: one per kind of breakdown, hidden until it is active
                var markerUi = AssetDatabase.LoadAssetAtPath<GameObject>($"{Root}/Prefabs/Buildings/BuildingMarkerUI.prefab").GetComponent<BuildingMarkerUI>();
                var markers = new BuildingMarker[BreakdownTypes.Count];
                for (int i = 0; i < markers.Length; i++)
                {
                    var anchor = new GameObject($"BreakdownMarker{i}");
                    anchor.transform.SetParent(container, false);
                    anchor.transform.localPosition = new Vector3(0f, 3.3f, 0f);
                    markers[i] = anchor.AddComponent<BuildingMarker>();
                    Set(markers[i], "_uiPrefab", markerUi);
                    SetBool(markers[i], "_startHidden", true);
                }
                var markerDriver = root.AddComponent<BreakdownMarkers>();
                SetArray(markerDriver, "_markers", markers);

                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        /// <summary>A thin bar that fills from its left edge: an empty pivot scaled in x, with the cube offset by half its width.</summary>
        private static Transform MakeBar(string name, Transform parent, Vector3 leftEdge, float width)
        {
            var pivot = new GameObject(name).transform;
            pivot.SetParent(parent, false);
            pivot.localPosition = leftEdge;
            GameObject back = Cube("Back", pivot, new Vector3(width * 0.5f, 0f, 0f), new Vector3(width + 0.02f, 0.06f, 0.035f), Mat("BarBack", new Color(0.05f, 0.07f, 0.16f)), false);
            GameObject fill = Cube("Fill", pivot, new Vector3(width * 0.5f, 0f, 0.012f), new Vector3(width, 0.045f, 0.03f), Mat("BarFill", Lamp, true), false);
            if (back == null || fill == null) { Debug.Log("[Setup] bar missing"); }
            pivot.gameObject.SetActive(false);
            return pivot;
        }

        // ---------- icons: wrench, fan belt, breaker ----------

        private static Sprite[] MakeBreakdownIcons()
        {
            Sprite wrench = MakeIcon("status_wrench", (x, y) =>
            {
                if (SegmentDistance(x, y, 26f, 26f, 84f, 84f) < 10f) { return true; }                    // the handle
                if (Dist(x, y, 28f, 28f) < 14f) { return true; }                                           // its round end
                bool head = Dist(x, y, 94f, 94f) < 28f;
                bool mouth = SegmentDistance(x, y, 96f, 96f, 130f, 130f) < 11f;                            // the jaw opening
                return head && !mouth;
            });
            Sprite belt = MakeIcon("status_belt", (x, y) =>
            {
                if (Dist(x, y, 38f, 64f) < 10f || Dist(x, y, 90f, 64f) < 10f) { return true; }            // the two pulleys
                float d = SegmentDistance(x, y, 38f, 64f, 90f, 64f);
                bool ring = d > 24f && d < 33f;                                                            // the belt around them
                bool gap = Mathf.Abs(x - 64f) < 9f && y > 84f;                                             // snapped at the top
                return ring && !gap;
            });
            Sprite breaker = MakeIcon("status_breaker", (x, y) =>
            {
                float bx = Mathf.Abs(x - 64f), by = Mathf.Abs(y - 60f);
                if (bx < 38f && by < 50f && !(bx < 30f && by < 42f)) { return true; }                      // the box
                if (SegmentDistance(x, y, 64f, 38f, 78f, 82f) < 7f) { return true; }                       // the lever, slanting down
                return Dist(x, y, 80f, 88f) < 11f;                                                         // its knob
            });
            return new[] { wrench, belt, breaker };
        }

        // ---------- strings ----------

        public static readonly string[] BreakdownKeys =
        {
            "brk.take_wrench", "brk.take_belt", "brk.take_belt_item", "brk.fit_belt", "brk.reset_breaker",
            "brk.marker.sputter", "brk.marker.sputter.fix", "brk.marker.belt", "brk.marker.belt.fix", "brk.marker.breaker", "brk.marker.breaker.fix",
            "help.legend.sputter", "help.legend.belt", "help.legend.breaker",
            "tip.sputter", "tip.belt", "tip.breaker",
            "shift.sum.fixed", "shift.sum.fixtime",
            "bld.line.sputter.0", "bld.line.sputter.1", "bld.line.belt.0", "bld.line.belt.1", "bld.line.breaker.0", "bld.line.breaker.1",
        };

        private static void CreateBreakdownStrings()
        {
            StringTableCollection ui = LocalizationEditorSettings.GetStringTableCollection("UI");
            Add(ui, "brk.take_wrench", "Take the wrench", "خذ المفتاح");
            Add(ui, "brk.take_belt", "Take a spare belt", "خذ سيراً احتياطياً");
            Add(ui, "brk.take_belt_item", "Pick up the belt", "التقط السير");
            Add(ui, "brk.fit_belt", "Fit the belt (hold)", "ركّب السير (اضغط مطولاً)");
            Add(ui, "brk.reset_breaker", "Reset the breaker (hold)", "أعِد رفع القاطع (اضغط مطولاً)");
            Add(ui, "brk.marker.sputter", "Engine coughing", "المحرّك يسعل");
            Add(ui, "brk.marker.sputter.fix", "Hit the glow with the wrench", "اضرب المضيء بالمفتاح");
            Add(ui, "brk.marker.belt", "Fan belt snapped", "انقطع السير");
            Add(ui, "brk.marker.belt.fix", "Fit a belt from the crate", "ركّب سيراً من الصندوق");
            Add(ui, "brk.marker.breaker", "Breaker tripped", "فصل القاطع");
            Add(ui, "brk.marker.breaker.fix", "Lift the lever back up", "أعِد رفع القاطع");
            Add(ui, "help.legend.sputter", "Engine coughing: hit the glowing spot with the wrench", "المحرّك يتعطّل: اضربه بالمفتاح على النقطة المضيئة");
            Add(ui, "help.legend.belt", "Belt snapped: fit a new one from the crate", "انقطع السير: ركّب سيراً جديداً");
            Add(ui, "help.legend.breaker", "Breaker tripped: lift the lever back up", "فصل القاطع: أعِد رفعه");
            Add(ui, "tip.sputter", "swing the wrench at the glowing spot (it hangs by the generator)", "اضرب النقطة المضيئة بالمفتاح (معلّق بجانب المولّد)");
            Add(ui, "tip.belt", "take a spare belt from the crate, then hold it at the glowing slot", "خذ سيراً من الصندوق ثم اضغط مطولاً عند الفتحة المضيئة");
            Add(ui, "tip.breaker", "hold the lever to lift the breaker back up", "اضغط مطولاً على الذراع لإعادة رفع القاطع");
            Add(ui, "shift.sum.fixed", "Breakdowns fixed: {0}", "أعطال تم إصلاحها: {0}");
            Add(ui, "shift.sum.fixtime", "Average fix time: {0} s", "متوسط وقت الإصلاح: {0} ثانية");
            Add(ui, "bld.line.sputter.0", "The lights are blinking like a disco!", "الضو عم يرقص متل الديسكو!");
            Add(ui, "bld.line.sputter.1", "Is the generator coughing, or is that the neighbor's grandpa?", "المولّد عم يسعل، ولا هيدا جدّو لجارنا؟");
            Add(ui, "bld.line.belt.0", "Something is squealing! The generator or the cat?", "في شي عم يصرخ! المولّد ولا القطة؟");
            Add(ui, "bld.line.belt.1", "Smells like burnt rubber. Not complaining, just saying.", "ريحة مطّاط محروق. مش عم اشتكي، بس عم قول.");
            Add(ui, "bld.line.breaker.0", "Bang! I think the fuse box just sneezed.", "بوم! صندوق الكهربا عطس!");
            Add(ui, "bld.line.breaker.1", "Who turned off the lights?! Fix the breaker!", "مين طفا الضو؟! صلّحوا القاطع!");
            AssetDatabase.SaveAssets();
        }
    }
}
