using System.IO;
using PowerCutCrew.Cables;
using PowerCutCrew.Core;
using PowerCutCrew.Fuel;
using PowerCutCrew.Generator;
using PowerCutCrew.Net;
using RTLTMPro;
using TMPro;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.Localization;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Localization.Tables;
using UnityEngine.UI;

namespace PowerCutCrew.Editor
{
    /// <summary>Builds the core loop content: tuning assets, prefabs (generator, drum, socket, cable), string keys, and the Street_Greybox layout. Re-runnable.</summary>
    public static class CoreLoopSetup
    {
        private const string Root = "Assets/_Project";
        private static readonly Color Cream = new Color32(0xFF, 0xF4, 0xDC, 0xFF);

        [MenuItem("Power Cut Crew/Setup/Build Core Loop")]
        public static void Run()
        {
            Directory.CreateDirectory($"{Root}/Config/Resources");
            Directory.CreateDirectory($"{Root}/Prefabs/CoreLoop");
            AssetDatabase.Refresh();
            ContentSetup.LoadFonts();

            CreateStrings();
            LoadOrCreate<FuelTuning>($"{Root}/Config/Resources/FuelTuning.asset");
            LoadOrCreate<GeneratorTuning>($"{Root}/Config/Resources/GeneratorTuning.asset");
            LoadOrCreate<CableTuning>($"{Root}/Config/Resources/CableTuning.asset");

            PatchCanPrefab();
            PatchChainPrefab();
            GameObject generator = SavePrefab(BuildGenerator(), "Generator");
            GameObject drum = SavePrefab(BuildDrum(), "FuelDrum");
            GameObject socket = SavePrefab(BuildSocket(), "PowerSocket");
            GameObject cable = SavePrefab(BuildCable(), "Cable");

            var catalog = LoadOrCreate<CoreLoopCatalog>($"{Root}/Config/Resources/CoreLoopCatalog.asset");
            catalog.Generator = generator.GetComponent<NetworkObject>();
            catalog.Drum = drum.GetComponent<NetworkObject>();
            catalog.Socket = socket.GetComponent<NetworkObject>();
            catalog.Cable = cable.GetComponent<NetworkObject>();
            EditorUtility.SetDirty(catalog);

            BuildStreet();
            AssetDatabase.SaveAssets();
            Debug.Log("[CoreLoopSetup] done");
        }

        // ---------- helpers ----------

        private static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            var a = AssetDatabase.LoadAssetAtPath<T>(path);
            if (a != null) { return a; }
            a = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(a, path);
            return a;
        }

        private static Material Mat(string name, Color c, bool emissive = false, string shader = "Universal Render Pipeline/Lit")
        {
            string path = $"{Root}/Art/Materials/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m != null) { return m; }
            m = new Material(Shader.Find(shader)) { name = name };
            m.SetColor("_BaseColor", c);
            if (emissive)
            {
                m.EnableKeyword("_EMISSION");
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        private static GameObject SavePrefab(GameObject go, string name)
        {
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(go, $"{Root}/Prefabs/CoreLoop/{name}.prefab");
            Object.DestroyImmediate(go);
            return prefab;
        }

        private static GameObject Visual(string name, PrimitiveType type, Transform parent, Vector3 pos, Vector3 scale, Material m, bool collider = false)
        {
            GameObject v = GameObject.CreatePrimitive(type);
            v.name = name;
            if (!collider) { Object.DestroyImmediate(v.GetComponent<Collider>()); }
            v.transform.SetParent(parent, false);
            v.transform.localPosition = pos;
            v.transform.localScale = scale;
            v.GetComponent<Renderer>().sharedMaterial = m;
            return v;
        }

        private static void Set(Object target, string field, Object value)
        {
            var so = new SerializedObject(target);
            so.FindProperty(field).objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetArray(Object target, string field, Object[] values)
        {
            var so = new SerializedObject(target);
            SerializedProperty arr = so.FindProperty(field);
            arr.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++) { arr.GetArrayElementAtIndex(i).objectReferenceValue = values[i]; }
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetInteractable(Interactable i, string key, float range, bool hold)
        {
            var so = new SerializedObject(i);
            so.FindProperty("_promptKey").stringValue = key;
            so.FindProperty("_maxRange").floatValue = range;
            so.FindProperty("_hold").boolValue = hold;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // ---------- strings ----------

        private static void CreateStrings()
        {
            StringTableCollection ui = LocalizationEditorSettings.GetStringTableCollection("UI");
            Add(ui, "gen.pull_cord", "Pull cord", "اسحب الحبل");
            Add(ui, "gen.turn_off", "Turn off", "أطفئ المولد");
            Add(ui, "gen.too_hot", "Too hot", "المولد ساخن جداً");
            Add(ui, "gen.pour", "Pour fuel", "اسكب الوقود");
            Add(ui, "gen.state.off", "Off", "متوقف");
            Add(ui, "gen.state.running", "Running", "يعمل");
            Add(ui, "gen.state.out_of_fuel", "Out of fuel", "نفد الوقود");
            Add(ui, "gen.state.overheated", "Overheated", "حرارة زائدة");
            Add(ui, "gen.fuel", "Fuel", "الوقود");
            Add(ui, "gen.heat", "Heat", "الحرارة");
            Add(ui, "gen.load_format", "{0} / {1} A", "{0} / {1} أمبير");
            Add(ui, "fuel.fill", "Fill can", "املأ الغالون");
            Add(ui, "cable.plug_in", "Plug in", "وصّل القابس");
            Add(ui, "cable.unplug", "Unplug", "افصل القابس");
            Add(ui, "cable.take_plug", "Take plug", "خذ القابس");
            AssetDatabase.SaveAssets();
        }

        private static void Add(StringTableCollection coll, string key, string en, string ar)
        {
            if (coll.SharedData.GetEntry(key) == null) { coll.SharedData.AddKey(key); }
            ((StringTable)coll.GetTable("en")).AddEntry(key, en);
            ((StringTable)coll.GetTable("ar")).AddEntry(key, ar);
            EditorUtility.SetDirty(coll.GetTable("en"));
            EditorUtility.SetDirty(coll.GetTable("ar"));
            EditorUtility.SetDirty(coll.SharedData);
        }

        // ---------- existing prefabs ----------

        private static void PatchCanPrefab()
        {
            string path = $"{Root}/Prefabs/Net/Can.prefab";
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            if (root.GetComponent<FuelCan>() == null)
            {
                Transform visual = root.transform.Find("Visual");
                GameObject bar = Visual("FuelBar", PrimitiveType.Cube, visual, new Vector3(0f, -0.45f, 0f), new Vector3(1.04f, 0.001f, 1.04f), Mat("FuelBar", new Color(1f, 0.55f, 0.1f)));
                root.AddComponent<FuelCan>();
                var item = root.GetComponent<NetworkPhysicsItem>();
                SetArray(item, "_renderers", new Object[] { visual.GetComponent<Renderer>(), bar.GetComponent<Renderer>() });
                var so = new SerializedObject(item);
                so.FindProperty("_kind").stringValue = "can";
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            PrefabUtility.SaveAsPrefabAsset(root, path);
            PrefabUtility.UnloadPrefabContents(root);
        }

        private static void PatchChainPrefab()
        {
            string path = $"{Root}/Prefabs/Net/Chain.prefab";
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            var stream = root.GetComponent<SnapshotStream>();
            var so = new SerializedObject(stream);
            so.FindProperty("_verifyTarget").boolValue = true;
            so.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(root, path);
            PrefabUtility.UnloadPrefabContents(root);
        }

        // ---------- generator ----------

        private static GameObject BuildGenerator()
        {
            var root = new GameObject("Generator");
            var body = root.AddComponent<BoxCollider>();
            body.size = new Vector3(1.2f, 1f, 1.2f);
            body.center = new Vector3(0f, 0.5f, 0f);
            Visual("Body", PrimitiveType.Cube, root.transform, new Vector3(0f, 0.5f, 0f), new Vector3(1.2f, 1f, 1.2f), Mat("Generator", new Color(0.95f, 0.8f, 0.1f)));
            Material dark = Mat("GeneratorParts", new Color(0.18f, 0.18f, 0.2f));

            // pull cord: a handle on the +z side
            GameObject cord = new GameObject("Cord");
            cord.transform.SetParent(root.transform, false);
            cord.transform.localPosition = new Vector3(0f, 0.6f, 0.62f);
            cord.AddComponent<BoxCollider>().size = new Vector3(0.3f, 0.3f, 0.15f);
            Visual("Handle", PrimitiveType.Cube, cord.transform, Vector3.zero, new Vector3(0.3f, 0.3f, 0.15f), Mat("CordRed", new Color(0.8f, 0.1f, 0.1f)));
            SetInteractable(cord.AddComponent<GeneratorSwitch>(), "gen.pull_cord", 3f, false);

            // tank cap on top: hold E to pour
            GameObject tank = new GameObject("Tank");
            tank.transform.SetParent(root.transform, false);
            tank.transform.localPosition = new Vector3(0f, 1.08f, 0f);
            tank.AddComponent<BoxCollider>().size = new Vector3(0.4f, 0.16f, 0.4f);
            Visual("Cap", PrimitiveType.Cylinder, tank.transform, Vector3.zero, new Vector3(0.4f, 0.08f, 0.4f), dark);
            SetInteractable(tank.AddComponent<GeneratorTank>(), "gen.pour", 3f, true);

            // outlets with their cable reels on the -x and +x sides
            var outlets = new Transform[2];
            for (int i = 0; i < 2; i++)
            {
                float side = i == 0 ? -1f : 1f;
                GameObject o = new GameObject($"Outlet{i}");
                o.transform.SetParent(root.transform, false);
                o.transform.localPosition = new Vector3(side * 0.66f, 0.35f, 0f);
                o.transform.localRotation = Quaternion.LookRotation(new Vector3(side, 0f, 0f));
                o.AddComponent<BoxCollider>().size = new Vector3(0.3f, 0.3f, 0.2f);
                Visual("Reel", PrimitiveType.Cylinder, o.transform, Vector3.zero, new Vector3(0.28f, 0.1f, 0.28f), dark).transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                var reel = o.AddComponent<CableReel>();
                SetInteractable(reel, "cable.take_plug", 3f, false);
                var rso = new SerializedObject(reel);
                rso.FindProperty("_outlet").intValue = i;
                rso.ApplyModifiedPropertiesWithoutUndo();
                outlets[i] = o.transform;
            }

            ParticleSystem sputter = BuildSputter(root.transform);
            BuildPanel(root.transform);

            root.AddComponent<NetworkObject>();
            var net = root.AddComponent<GeneratorNet>();
            SetArray(net, "_outlets", outlets);
            Set(net, "_cordPoint", cord.transform);
            Set(net, "_sputter", sputter);
            return root;
        }

        private static ParticleSystem BuildSputter(Transform parent)
        {
            var go = new GameObject("Sputter");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0f, 1.1f, -0.2f);
            go.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.playOnAwake = false;
            main.loop = false;
            main.duration = 0.5f;
            main.startLifetime = 1.3f;
            main.startSpeed = 1.6f;
            main.startSize = 0.35f;
            main.startColor = new Color(0.55f, 0.55f, 0.55f, 0.85f);
            main.gravityModifier = -0.05f;
            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 16) });
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 22f;
            shape.radius = 0.08f;
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0f, 1f) });
            col.color = grad;
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.5f, 1f, 1.6f));
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Mesh;
            r.mesh = Resources.GetBuiltinResource<Mesh>("Sphere.fbx");
            r.sharedMaterial = Mat("SputterSmoke", new Color(0.55f, 0.55f, 0.55f, 0.85f), false, "Universal Render Pipeline/Particles/Unlit");
            return ps;
        }

        private static void BuildPanel(Transform parent)
        {
            var canvasGo = new GameObject("Panel", typeof(RectTransform));
            canvasGo.transform.SetParent(parent, false);
            canvasGo.transform.localPosition = new Vector3(0f, 2.05f, 0f);
            canvasGo.transform.localScale = Vector3.one * 0.0045f;
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            var rt = (RectTransform)canvasGo.transform;
            rt.sizeDelta = new Vector2(440f, 270f);
            var bg = ContentSetup.UiObject("Background", rt);
            ContentSetup.Stretch(bg);
            bg.gameObject.AddComponent<Image>().color = new Color(0.05f, 0.07f, 0.16f, 0.82f);

            var state = ContentSetup.MakeText("State", rt, "gen.state.off", ContentSetup.BodyFontEn, ContentSetup.BodyFontAr, 52, new Color32(0xFF, 0xC9, 0x3C, 0xFF));
            Place((RectTransform)state.transform, 0f, 90f, 420f, 70f);

            var fuelCap = ContentSetup.MakeText("FuelCaption", rt, "gen.fuel", ContentSetup.BodyFontEn, ContentSetup.BodyFontAr, 34, Cream);
            Place((RectTransform)fuelCap.transform, -150f, 25f, 130f, 44f);
            Image fuelBar = Bar("FuelBar", rt, 25f, new Color(1f, 0.6f, 0.1f));

            var heatCap = ContentSetup.MakeText("HeatCaption", rt, "gen.heat", ContentSetup.BodyFontEn, ContentSetup.BodyFontAr, 34, Cream);
            Place((RectTransform)heatCap.transform, -150f, -30f, 130f, 44f);
            Image heatBar = Bar("HeatBar", rt, -30f, new Color(0.2f, 0.8f, 0.3f));

            var load = ContentSetup.MakeText("Load", rt, "gen.load_format", ContentSetup.BodyFontEn, ContentSetup.BodyFontAr, 46, Cream);
            Place((RectTransform)load.transform, 0f, -92f, 420f, 60f);

            var panel = canvasGo.AddComponent<GeneratorPanel>();
            Set(panel, "_stateLabel", state.GetComponent<PowerCutCrew.Localization.LocalizedRtlText>());
            Set(panel, "_loadLabel", load.GetComponent<PowerCutCrew.Localization.LocalizedRtlText>());
            Set(panel, "_fuelBar", fuelBar.rectTransform);
            Set(panel, "_heatBar", heatBar.rectTransform);
            Set(panel, "_heatImage", heatBar);
        }

        private static void Place(RectTransform rt, float x, float y, float w, float h)
        {
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(x, y);
            rt.sizeDelta = new Vector2(w, h);
        }

        private static Image Bar(string name, RectTransform parent, float y, Color color)
        {
            RectTransform back = ContentSetup.UiObject(name + "Back", parent);
            Place(back, 70f, y, 250f, 28f);
            back.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.6f);
            RectTransform fill = ContentSetup.UiObject(name, back);
            fill.anchorMin = new Vector2(0f, 0f);
            fill.anchorMax = new Vector2(0f, 1f);
            fill.pivot = new Vector2(0f, 0.5f);
            fill.anchoredPosition = Vector2.zero;
            fill.sizeDelta = new Vector2(1f, 0f);
            var img = fill.gameObject.AddComponent<Image>();
            img.color = color;
            return img;
        }

        // ---------- drum, socket, cable ----------

        private static GameObject BuildDrum()
        {
            var root = new GameObject("FuelDrum");
            var col = root.AddComponent<CapsuleCollider>();
            col.radius = 0.4f;
            col.height = 1f;
            col.center = new Vector3(0f, 0.5f, 0f);
            Visual("Barrel", PrimitiveType.Cylinder, root.transform, new Vector3(0f, 0.5f, 0f), new Vector3(0.8f, 0.5f, 0.8f), Mat("Drum", new Color(0.75f, 0.12f, 0.1f)));
            Visual("Cap", PrimitiveType.Cylinder, root.transform, new Vector3(0f, 1.03f, 0f), new Vector3(0.3f, 0.03f, 0.3f), Mat("GeneratorParts", new Color(0.18f, 0.18f, 0.2f)));
            root.AddComponent<NetworkObject>();
            SetInteractable(root.AddComponent<FuelDrum>(), "fuel.fill", 3f, true);
            return root;
        }

        private static GameObject BuildSocket()
        {
            var root = new GameObject("PowerSocket");
            // plate on the wall (forward points away from the wall, towards the generator)
            var plate = new GameObject("Plate");
            plate.transform.SetParent(root.transform, false);
            plate.transform.localPosition = new Vector3(0f, 0.8f, 0f);
            plate.AddComponent<BoxCollider>().size = new Vector3(0.45f, 0.55f, 0.12f);
            Visual("Mesh", PrimitiveType.Cube, plate.transform, Vector3.zero, new Vector3(0.45f, 0.55f, 0.12f), Mat("Socket", new Color(0.1f, 0.1f, 0.12f)));
            var plug = new GameObject("PlugPoint");
            plug.transform.SetParent(plate.transform, false);
            plug.transform.localPosition = new Vector3(0f, 0f, 0.14f);

            var lampGo = new GameObject("Lamp");
            lampGo.transform.SetParent(root.transform, false);
            lampGo.transform.localPosition = new Vector3(0f, 1.6f, 0.25f);
            var light = lampGo.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.85f, 0.5f);
            light.range = 7f;
            light.intensity = 8f;
            light.enabled = false;
            GameObject bulb = Visual("Bulb", PrimitiveType.Sphere, lampGo.transform, Vector3.zero, Vector3.one * 0.22f, Mat("Lamp", new Color(0.15f, 0.15f, 0.15f), true));

            root.AddComponent<NetworkObject>();
            var socket = root.AddComponent<PowerSocket>();
            Set(socket, "_lamp", light);
            Set(socket, "_lampRenderer", bulb.GetComponent<Renderer>());
            Set(socket, "_plugPoint", plug.transform);
            // the interactable sits on the plate collider
            SetInteractable(plate.AddComponent<SocketInteractable>(), "cable.plug_in", 3f, false);
            return root;
        }

        private static GameObject BuildCable()
        {
            var root = new GameObject("Cable");
            var rb = root.AddComponent<Rigidbody>();
            rb.mass = CableTuning.Instance != null ? CableTuning.Instance.PlugMass : 1f;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            root.AddComponent<BoxCollider>().size = new Vector3(0.2f, 0.2f, 0.3f);
            GameObject visual = Visual("Visual", PrimitiveType.Cube, root.transform, Vector3.zero, new Vector3(0.2f, 0.2f, 0.3f), Mat("Plug", new Color(0.2f, 0.4f, 0.9f)));
            root.AddComponent<NetworkObject>();

            var item = root.AddComponent<NetworkPhysicsItem>();
            Set(item, "_visualRoot", visual.transform);
            SetArray(item, "_renderers", new Object[] { visual.GetComponent<Renderer>() });
            var iso = new SerializedObject(item);
            iso.FindProperty("_kind").stringValue = "plug";
            iso.FindProperty("_springHold").boolValue = true;
            iso.ApplyModifiedPropertiesWithoutUndo();
            SetInteractable(root.AddComponent<ItemInteractable>(), "interact.pickup", 3f, false);

            var stream = root.AddComponent<SnapshotStream>();
            var sso = new SerializedObject(stream);
            sso.FindProperty("_syncRotation").boolValue = false;
            sso.FindProperty("_stride").intValue = 2;
            sso.FindProperty("_rateHz").floatValue = 20f;
            sso.FindProperty("_streamIndex").intValue = 0;
            sso.ApplyModifiedPropertiesWithoutUndo();

            var cable = root.AddComponent<CableNet>();
            Set(cable, "_segmentMesh", Resources.GetBuiltinResource<Mesh>("Cube.fbx"));
            Set(cable, "_segmentMaterial", Mat("Rope", new Color(0.08f, 0.08f, 0.08f)));
            return root;
        }

        // ---------- scene ----------

        private static void BuildStreet()
        {
            var scene = EditorSceneManager.OpenScene($"{Root}/Scenes/Street_Greybox.unity", OpenSceneMode.Single);
            if (Object.FindFirstObjectByType<CoreLoopAnchors>() != null) { EditorSceneManager.SaveScene(scene); return; }

            Vector3 gen = Vector3.zero;
            Vector3 drumPos = new Vector3(-8f, 0f, 0f);
            // sockets at roughly 5, 8, 10 and 11 m from the generator, each on the wall of a placeholder building
            float[] dist = { 5f, 8f, 10f, 11f };
            float[] angle = { 0f, 270f, 45f, 225f };           // degrees, 0 = +x, 90 = +z
            float[] amps = { 5f, 5f, 5f, 8f };
            Material wall = Mat("Building", new Color(0.55f, 0.55f, 0.6f));

            var anchorsGo = new GameObject("CoreLoopAnchors");
            var anchors = anchorsGo.AddComponent<CoreLoopAnchors>();
            var genT = new GameObject("GeneratorAnchor").transform;
            genT.SetParent(anchorsGo.transform, false);
            genT.position = gen;
            var drumT = new GameObject("DrumAnchor").transform;
            drumT.SetParent(anchorsGo.transform, false);
            drumT.position = drumPos;
            var sockets = new Transform[4];
            for (int i = 0; i < 4; i++)
            {
                float rad = angle[i] * Mathf.Deg2Rad;
                Vector3 dir = new Vector3(Mathf.Cos(rad), 0f, Mathf.Sin(rad));
                Vector3 pos = gen + dir * dist[i];
                var s = new GameObject($"SocketAnchor{i}").transform;
                s.SetParent(anchorsGo.transform, false);
                s.SetPositionAndRotation(pos, Quaternion.LookRotation(-dir));   // faces the generator
                sockets[i] = s;
                // building behind the socket
                GameObject b = GameObject.CreatePrimitive(PrimitiveType.Cube);
                b.name = $"Building{i}";
                b.transform.position = pos + dir * 2.15f + Vector3.up * 1.5f;
                b.transform.rotation = Quaternion.LookRotation(dir);
                b.transform.localScale = new Vector3(4f, 3f, 4f);
                b.GetComponent<Renderer>().sharedMaterial = wall;
            }
            var so = new SerializedObject(anchors);
            so.FindProperty("_generator").objectReferenceValue = genT;
            so.FindProperty("_drum").objectReferenceValue = drumT;
            SerializedProperty sp = so.FindProperty("_sockets");
            sp.arraySize = 4;
            SerializedProperty ap = so.FindProperty("_socketAmps");
            ap.arraySize = 4;
            for (int i = 0; i < 4; i++)
            {
                sp.GetArrayElementAtIndex(i).objectReferenceValue = sockets[i];
                ap.GetArrayElementAtIndex(i).floatValue = amps[i];
            }
            SerializedProperty fp = so.FindProperty("_canStartFuel");
            fp.arraySize = 10;
            for (int i = 0; i < 10; i++) { fp.GetArrayElementAtIndex(i).floatValue = i < 4 ? FuelTuning.Instance.CanCapacity : 0f; }
            so.ApplyModifiedPropertiesWithoutUndo();

            // move the Prompt 03 placeholders out of the way: cans near the drum, the chain east, obstacles clear of the buildings
            for (int i = 0; i < 10; i++)
            {
                GameObject c = GameObject.Find($"CanSpawn{i}");
                if (c != null) { c.transform.position = new Vector3(-9.5f + (i % 5) * 1.2f, 0.3f, 3f + (i / 5) * 1.6f); }
            }
            GameObject chain = GameObject.Find("ChainAnchor");
            if (chain != null) { chain.transform.position = new Vector3(10f, 0f, -3f); }
            Vector3[] obstacles = { new Vector3(-4f, 0.5f, 8f), new Vector3(3.5f, 0.75f, -4f), new Vector3(13f, 0.5f, 8f), new Vector3(-15f, 1f, -4f) };
            for (int i = 0; i < obstacles.Length; i++)
            {
                GameObject o = GameObject.Find($"Obstacle{i}");
                if (o != null) { o.transform.position = obstacles[i]; }
            }
            EditorSceneManager.SaveScene(scene);
        }
    }
}
