using System.IO;
using PowerCutCrew.Net;
using RTLTMPro;
using TMPro;
using Unity.Cinemachine;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.Localization;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Localization.Tables;
using UnityEngine.UI;
using Netcode.Transports;
using PowerCutCrew.UI;

namespace PowerCutCrew.Editor
{
    /// <summary>Builds the networking foundation content: tuning assets, prefabs, string keys, scene edits. Re-runnable.</summary>
    public static class NetFoundationSetup
    {
        private const string Root = "Assets/_Project";
        private static readonly Color PanelFill = new Color32(0x1E, 0x2A, 0x52, 0xFF);
        private static readonly Color ButtonText = new Color32(0xFF, 0xF4, 0xDC, 0xFF);

        [MenuItem("Power Cut Crew/Setup/Build Net Foundation")]
        public static void Run()
        {
            Directory.CreateDirectory($"{Root}/Config/Resources");
            Directory.CreateDirectory($"{Root}/Prefabs/Net");
            AssetDatabase.Refresh();
            ContentSetup.LoadFonts();

            CreateStrings();
            LoadOrCreate<NetTuning>($"{Root}/Config/Resources/NetTuning.asset");
            LoadOrCreate<PlayerTuning>($"{Root}/Config/Resources/PlayerTuning.asset");

            GameObject player = SavePrefab(BuildPlayer(), "Player");
            GameObject can = SavePrefab(BuildCan(), "Can");
            GameObject chain = SavePrefab(BuildChain(), "Chain");
            var catalog = LoadOrCreate<NetPrefabCatalog>($"{Root}/Config/NetPrefabCatalog.asset");
            catalog.Player = player.GetComponent<NetworkObject>();
            catalog.Can = can.GetComponent<NetworkObject>();
            catalog.Chain = chain.GetComponent<NetworkObject>();
            EditorUtility.SetDirty(catalog);
            GameObject session = SavePrefab(BuildSession(catalog), "NetworkSession");

            BuildBootstrap(session);
            BuildMainMenu();
            BuildStreet();
            File.WriteAllText("steam_appid.txt", "480");
            AssetDatabase.SaveAssets();
            Debug.Log("[NetFoundationSetup] done");
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

        private static Material Mat(string name, Color c)
        {
            string path = $"{Root}/Art/Materials/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m != null) { return m; }
            m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
            m.SetColor("_BaseColor", c);
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        private static GameObject SavePrefab(GameObject go, string name)
        {
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(go, $"{Root}/Prefabs/Net/{name}.prefab");
            Object.DestroyImmediate(go);
            return prefab;
        }

        private static GameObject Visual(string name, PrimitiveType type, Transform parent, Vector3 pos, Vector3 scale, Material m)
        {
            GameObject v = GameObject.CreatePrimitive(type);
            v.name = name;
            Object.DestroyImmediate(v.GetComponent<Collider>());
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

        private static NetworkTransform AddNt(GameObject go, bool ownerAuthoritative)
        {
            var nt = go.AddComponent<NetworkTransform>();
            nt.AuthorityMode = ownerAuthoritative ? NetworkTransform.AuthorityModes.Owner : NetworkTransform.AuthorityModes.Server;
            nt.SyncScaleX = nt.SyncScaleY = nt.SyncScaleZ = false;
            nt.UseUnreliableDeltas = true;
            nt.Interpolate = true;
            return nt;
        }

        // ---------- strings ----------

        private static void CreateStrings()
        {
            StringTableCollection ui = LocalizationEditorSettings.GetStringTableCollection("UI");
            Add(ui, "net.host_local", "Host (Local)", "استضافة (محلي)");
            Add(ui, "net.host_steam", "Host (Steam)", "استضافة (ستيم)");
            Add(ui, "net.join", "Join", "انضم");
            Add(ui, "net.back", "Back", "رجوع");
            Add(ui, "net.ip_placeholder", "IP address", "عنوان IP");
            Add(ui, "net.connecting", "Connecting...", "جارٍ الاتصال...");
            Add(ui, "net.player_name", "Player {0}", "لاعب {0}", true);
            Add(ui, "net.error.steam_unavailable", "Steam is not running. Start Steam, or use Host (Local).", "ستيم غير مشغّل. شغّل ستيم أو استخدم الاستضافة المحلية.");
            Add(ui, "net.error.steam_lobby", "Could not create the Steam lobby.", "تعذّر إنشاء غرفة ستيم.");
            Add(ui, "net.error.host_left", "The host left the game.", "غادر المضيف اللعبة.");
            Add(ui, "net.error.full", "The game is full.", "اللعبة ممتلئة.");
            Add(ui, "net.error.version", "Your game version is different from the host's.", "إصدار لعبتك مختلف عن إصدار المضيف.");
            Add(ui, "net.error.connect_failed", "Could not connect to the host.", "تعذّر الاتصال بالمضيف.");
            Add(ui, "interact.pickup", "Pick up", "التقط");
            AssetDatabase.SaveAssets();
        }

        private static void Add(StringTableCollection coll, string key, string en, string ar, bool smart = false)
        {
            if (coll.SharedData.GetEntry(key) == null) { coll.SharedData.AddKey(key); }
            StringTableEntry e = ((StringTable)coll.GetTable("en")).AddEntry(key, en);
            StringTableEntry a = ((StringTable)coll.GetTable("ar")).AddEntry(key, ar);
            e.IsSmart = smart;
            a.IsSmart = smart;
            EditorUtility.SetDirty(coll.GetTable("en"));
            EditorUtility.SetDirty(coll.GetTable("ar"));
            EditorUtility.SetDirty(coll.SharedData);
        }

        // ---------- prefabs ----------

        private static GameObject BuildPlayer()
        {
            var root = new GameObject("Player");
            var cc = root.AddComponent<CharacterController>();
            cc.radius = 0.4f;
            cc.height = 1.8f;
            cc.center = new Vector3(0f, 0.9f, 0f);
            cc.stepOffset = 0.35f;

            GameObject body = Visual("Body", PrimitiveType.Capsule, root.transform, new Vector3(0f, 0.9f, 0f), new Vector3(0.8f, 0.9f, 0.8f), Mat("Player", Color.white));

            // lets the host's physics (chain, items) feel other players; the character controller ignores it
            var push = new GameObject("PushCollider");
            push.transform.SetParent(root.transform, false);
            push.AddComponent<Rigidbody>().isKinematic = true;
            var pc = push.AddComponent<CapsuleCollider>();
            pc.radius = 0.4f;
            pc.height = 1.8f;
            pc.center = new Vector3(0f, 0.9f, 0f);
            push.AddComponent<PlayerPushCollider>();

            var camTarget = new GameObject("CameraTarget");
            camTarget.transform.SetParent(root.transform, false);
            camTarget.transform.localPosition = new Vector3(0f, 1.6f, 0f);
            var hand = new GameObject("Hand");
            hand.transform.SetParent(camTarget.transform, false);
            hand.transform.localPosition = new Vector3(0f, -0.3f, 1.0f);

            var hold = new GameObject("HoldPoint");
            hold.transform.SetParent(root.transform, false);
            hold.transform.localPosition = new Vector3(0f, 1.3f, 1.0f);

            var camGo = new GameObject("OwnerCamera");
            camGo.transform.SetParent(root.transform, false);
            var cm = camGo.AddComponent<CinemachineCamera>();
            cm.Follow = camTarget.transform;
            camGo.AddComponent<CinemachineHardLockToTarget>();
            camGo.AddComponent<CinemachineRotateWithFollowTarget>();
            camGo.SetActive(false);

            var labelGo = new GameObject("Label");
            labelGo.transform.SetParent(root.transform, false);
            labelGo.transform.localPosition = new Vector3(0f, 2.3f, 0f);
            var label = labelGo.AddComponent<RTLTextMeshPro3D>();
            label.font = ContentSetup.BodyFontEn;
            label.fontSize = 3f;
            label.alignment = TextAlignmentOptions.Center;
            label.rectTransform.sizeDelta = new Vector2(6f, 1f);

            var controller = root.AddComponent<PlayerController>();
            Set(controller, "_cameraTarget", camTarget.transform);
            Set(controller, "_hand", hand.transform);

            root.AddComponent<NetworkObject>();
            AddNt(root, true);
            var net = root.AddComponent<NetPlayer>();
            Set(net, "_holdPoint", hold.transform);
            Set(net, "_bodyRenderer", body.GetComponent<Renderer>());
            Set(net, "_label", label);
            Set(net, "_ownerCamera", camGo);
            return root;
        }

        private static GameObject BuildCan()
        {
            Vector3 size = new Vector3(0.4f, 0.5f, 0.2f);
            var root = new GameObject("Can");
            var rb = root.AddComponent<Rigidbody>();
            rb.mass = 20f;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            root.AddComponent<BoxCollider>().size = size;
            GameObject visual = Visual("Visual", PrimitiveType.Cube, root.transform, Vector3.zero, size, Mat("Can", new Color(0.55f, 0.57f, 0.6f)));
            root.AddComponent<NetworkObject>();
            AddNt(root, false);
            var item = root.AddComponent<NetworkPhysicsItem>();
            Set(item, "_visualRoot", visual.transform);
            var so = new SerializedObject(item);
            SerializedProperty arr = so.FindProperty("_renderers");
            arr.arraySize = 1;
            arr.GetArrayElementAtIndex(0).objectReferenceValue = visual.GetComponent<Renderer>();
            so.ApplyModifiedPropertiesWithoutUndo();
            var inter = root.AddComponent<ItemInteractable>();
            var iso = new SerializedObject(inter);
            iso.FindProperty("_promptKey").stringValue = "interact.pickup";
            iso.FindProperty("_maxRange").floatValue = 3f;
            iso.ApplyModifiedPropertiesWithoutUndo();
            return root;
        }

        private static GameObject BuildChain()
        {
            const int links = 8;
            var root = new GameObject("Chain");
            Material chainMat = Mat("Chain", new Color(0.8f, 0.55f, 0.15f));
            float top = 3.9f;

            var beam = new GameObject("Beam");
            beam.transform.SetParent(root.transform, false);
            beam.transform.localPosition = new Vector3(0f, top + 0.1f, 0f);
            beam.AddComponent<BoxCollider>().size = new Vector3(1.4f, 0.2f, 0.3f);
            Visual("Mesh", PrimitiveType.Cube, beam.transform, Vector3.zero, new Vector3(1.4f, 0.2f, 0.3f), Mat("Beam", new Color(0.3f, 0.3f, 0.32f)));
            var anchor = new GameObject("Anchor");
            anchor.transform.SetParent(root.transform, false);
            anchor.transform.localPosition = new Vector3(0f, top, 0f);
            anchor.AddComponent<Rigidbody>().isKinematic = true;

            var bodies = new Transform[links];
            Rigidbody prev = anchor.GetComponent<Rigidbody>();
            float spacing = 0.42f;
            for (int i = 0; i < links; i++)
            {
                var link = new GameObject($"Link{i}");
                link.transform.SetParent(root.transform, false);
                link.transform.localPosition = new Vector3(0f, top - spacing * (i + 0.5f), 0f);
                var rb = link.AddComponent<Rigidbody>();
                rb.mass = 1f;
                rb.linearDamping = 0.2f;
                rb.angularDamping = 1.5f;
                rb.solverIterations = 20;
                Vector3 size = new Vector3(0.22f, 0.36f, 0.22f);
                link.AddComponent<BoxCollider>().size = size;
                Visual("Mesh", PrimitiveType.Cube, link.transform, Vector3.zero, size, chainMat);
                var j = link.AddComponent<ConfigurableJoint>();
                j.connectedBody = prev;
                j.autoConfigureConnectedAnchor = false;
                j.anchor = new Vector3(0f, spacing * 0.5f, 0f);
                j.connectedAnchor = new Vector3(0f, i == 0 ? 0f : -spacing * 0.5f, 0f);
                j.xMotion = j.yMotion = j.zMotion = ConfigurableJointMotion.Locked;
                j.angularXMotion = j.angularYMotion = j.angularZMotion = ConfigurableJointMotion.Limited;
                j.lowAngularXLimit = new SoftJointLimit { limit = -60f };
                j.highAngularXLimit = new SoftJointLimit { limit = 60f };
                j.angularYLimit = new SoftJointLimit { limit = 60f };
                j.angularZLimit = new SoftJointLimit { limit = 60f };
                j.projectionMode = JointProjectionMode.PositionAndRotation;
                bodies[i] = link.transform;
                prev = rb;
            }
            root.AddComponent<NetworkObject>();
            var stream = root.AddComponent<SnapshotStream>();
            var so = new SerializedObject(stream);
            SerializedProperty arr = so.FindProperty("_bodies");
            arr.arraySize = bodies.Length;
            for (int i = 0; i < bodies.Length; i++) { arr.GetArrayElementAtIndex(i).objectReferenceValue = bodies[i]; }
            so.ApplyModifiedPropertiesWithoutUndo();
            return root;
        }

        private static GameObject BuildSession(NetPrefabCatalog catalog)
        {
            var go = new GameObject("NetworkSession");
            var nm = go.AddComponent<NetworkManager>();
            var udp = go.AddComponent<MeteredUnityTransport>();
            var steam = go.AddComponent<SteamNetworkingSocketsTransport>();
            nm.NetworkConfig = new NetworkConfig
            {
                NetworkTransport = udp,
                TickRate = 30,
                EnableSceneManagement = true,
                ConnectionApproval = true,
            };
            var session = go.AddComponent<NetSession>();
            Set(session, "_nm", nm);
            Set(session, "_udp", udp);
            Set(session, "_steam", steam);
            Set(session, "_catalog", catalog);
            return go;
        }

        // ---------- scenes ----------

        private static void BuildBootstrap(GameObject sessionPrefab)
        {
            var scene = EditorSceneManager.OpenScene($"{Root}/Scenes/Bootstrap.unity", OpenSceneMode.Single);
            Bootstrap b = Object.FindFirstObjectByType<Bootstrap>();
            Set(b, "_sessionPrefab", sessionPrefab.GetComponent<NetSession>());
            EditorSceneManager.SaveScene(scene);
        }

        private static void BuildMainMenu()
        {
            var scene = EditorSceneManager.OpenScene($"{Root}/Scenes/MainMenu.unity", OpenSceneMode.Single);
            if (GameObject.Find("NetPanel") != null) { EditorSceneManager.SaveScene(scene); return; }
            Canvas canvas = Object.FindFirstObjectByType<Canvas>();
            Transform buttons = canvas.transform.Find("Buttons");
            Button play = buttons.Find("PlayButton").GetComponent<Button>();

            RectTransform panel = ContentSetup.UiObject("NetPanel", canvas.transform);
            panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(0.5f, 0.5f);
            panel.anchoredPosition = new Vector2(0f, -150f);
            panel.sizeDelta = new Vector2(700f, 700f);
            var vlg = panel.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 20;
            vlg.childAlignment = TextAnchor.UpperCenter;
            vlg.childControlWidth = vlg.childControlHeight = true;
            vlg.childForceExpandWidth = vlg.childForceExpandHeight = false;

            RTLTextMeshPro message = ContentSetup.MakeText("Message", panel, "net.connecting", ContentSetup.BodyFontEn, ContentSetup.BodyFontAr, 36, new Color32(0xFF, 0xC9, 0x3C, 0xFF));
            message.textWrappingMode = TextWrappingModes.Normal;
            message.gameObject.AddComponent<LayoutElement>().preferredHeight = 120;
            var msgLoc = message.GetComponent<PowerCutCrew.Localization.LocalizedRtlText>();

            Button hostLocal = ContentSetup.MakeButton("HostLocalButton", panel, "net.host_local", new Vector2(700, 100));
            TMP_InputField field = MakeInputField(panel);
            Button join = ContentSetup.MakeButton("JoinButton", panel, "net.join", new Vector2(700, 100));
            Button hostSteam = ContentSetup.MakeButton("HostSteamButton", panel, "net.host_steam", new Vector2(700, 100));
            Button back = ContentSetup.MakeButton("BackButton", panel, "net.back", new Vector2(700, 100));

            var controller = panel.gameObject.AddComponent<NetPlayPanel>();
            var so = new SerializedObject(controller);
            so.FindProperty("_panel").objectReferenceValue = panel.gameObject;
            so.FindProperty("_mainButtons").objectReferenceValue = buttons.gameObject;
            so.FindProperty("_address").objectReferenceValue = field;
            so.FindProperty("_message").objectReferenceValue = msgLoc;
            so.ApplyModifiedPropertiesWithoutUndo();

            UnityEventTools.AddPersistentListener(play.onClick, controller.Open);
            UnityEventTools.AddPersistentListener(hostLocal.onClick, controller.HostLocal);
            UnityEventTools.AddPersistentListener(join.onClick, controller.Join);
            UnityEventTools.AddPersistentListener(hostSteam.onClick, controller.HostSteam);
            UnityEventTools.AddPersistentListener(back.onClick, controller.Back);
            EditorSceneManager.SaveScene(scene);
        }

        private static TMP_InputField MakeInputField(Transform parent)
        {
            RectTransform rt = ContentSetup.UiObject("AddressField", parent);
            rt.sizeDelta = new Vector2(700, 90);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = new Color32(0x14, 0x1C, 0x3E, 0xFF);
            var le = rt.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = 90;
            le.preferredWidth = 700;

            RectTransform area = ContentSetup.UiObject("Text Area", rt);
            ContentSetup.Stretch(area);
            area.offsetMin = new Vector2(20, 6);
            area.offsetMax = new Vector2(-20, -6);
            area.gameObject.AddComponent<RectMask2D>();

            RTLTextMeshPro placeholder = ContentSetup.MakeText("Placeholder", area, "net.ip_placeholder", ContentSetup.BodyFontEn, ContentSetup.BodyFontAr, 40, new Color(1f, 0.96f, 0.86f, 0.4f));
            ContentSetup.Stretch((RectTransform)placeholder.transform);
            var text = new GameObject("Text", typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
            text.transform.SetParent(area, false);
            ContentSetup.Stretch((RectTransform)text.transform);
            text.font = ContentSetup.BodyFontEn;
            text.fontSize = 40;
            text.color = ButtonText;
            text.alignment = TextAlignmentOptions.Center;

            var field = rt.gameObject.AddComponent<TMP_InputField>();
            field.textViewport = area;
            field.textComponent = text;
            field.placeholder = placeholder;
            field.text = "127.0.0.1";
            field.contentType = TMP_InputField.ContentType.Standard;
            return field;
        }

        private static void BuildStreet()
        {
            var scene = EditorSceneManager.OpenScene($"{Root}/Scenes/Street_Greybox.unity", OpenSceneMode.Single);
            if (Object.FindFirstObjectByType<WorldAnchors>() != null) { EditorSceneManager.SaveScene(scene); return; }
            Material obstacle = Mat("Obstacle", new Color(0.4f, 0.42f, 0.45f));

            GameObject ground = GameObject.Find("Ground");
            if (ground != null) { ground.transform.localScale = new Vector3(4f, 1f, 4f); }   // 40 x 40 m

            for (int i = 0; i < 4; i++)
            {
                var w = new GameObject($"Wall{i}");
                var bc = w.AddComponent<BoxCollider>();
                bool alongX = i < 2;
                bc.size = alongX ? new Vector3(41f, 6f, 1f) : new Vector3(1f, 6f, 41f);
                float s = (i % 2 == 0 ? 1f : -1f) * 20.5f;
                w.transform.position = alongX ? new Vector3(0f, 3f, s) : new Vector3(s, 3f, 0f);
            }
            Vector3[] pos = { new Vector3(-4f, 0.5f, 7f), new Vector3(3f, 0.75f, -7f), new Vector3(12f, 0.5f, 6f), new Vector3(-12f, 1f, -10f) };
            Vector3[] size = { new Vector3(2f, 1f, 2f), new Vector3(3f, 1.5f, 1.5f), new Vector3(1.5f, 1f, 4f), new Vector3(2.5f, 2f, 2.5f) };
            for (int i = 0; i < pos.Length; i++)
            {
                GameObject b = GameObject.CreatePrimitive(PrimitiveType.Cube);
                b.name = $"Obstacle{i}";
                b.transform.position = pos[i];
                b.transform.localScale = size[i];
                b.GetComponent<Renderer>().sharedMaterial = obstacle;
            }

            // anchors
            var anchorsGo = new GameObject("WorldAnchors");
            var anchors = anchorsGo.AddComponent<WorldAnchors>();
            Transform[] playerSpawns = new Transform[4];
            for (int i = 0; i < 4; i++)
            {
                var s = new GameObject($"PlayerSpawn{i}");
                s.transform.SetParent(anchorsGo.transform, false);
                s.transform.SetPositionAndRotation(new Vector3(-4.5f + i * 3f, 0.1f, 14f), Quaternion.Euler(0f, 180f, 0f));
                playerSpawns[i] = s.transform;
            }
            Transform[] canSpawns = new Transform[10];
            for (int i = 0; i < 10; i++)
            {
                var s = new GameObject($"CanSpawn{i}");
                s.transform.SetParent(anchorsGo.transform, false);
                s.transform.position = new Vector3(-9f + (i % 5) * 1.5f, 0.3f, -4f + (i / 5) * 6f);
                canSpawns[i] = s.transform;
            }
            var chain = new GameObject("ChainAnchor");
            chain.transform.SetParent(anchorsGo.transform, false);
            chain.transform.position = new Vector3(6f, 0f, 6f);
            var so = new SerializedObject(anchors);
            SerializedProperty ps = so.FindProperty("_playerSpawns");
            ps.arraySize = playerSpawns.Length;
            for (int i = 0; i < playerSpawns.Length; i++) { ps.GetArrayElementAtIndex(i).objectReferenceValue = playerSpawns[i]; }
            SerializedProperty cs = so.FindProperty("_canSpawns");
            cs.arraySize = canSpawns.Length;
            for (int i = 0; i < canSpawns.Length; i++) { cs.GetArrayElementAtIndex(i).objectReferenceValue = canSpawns[i]; }
            so.FindProperty("_chain").objectReferenceValue = chain.transform;
            so.ApplyModifiedPropertiesWithoutUndo();

            // Cinemachine brain on the scene camera (the owner's player prefab carries its own virtual camera)
            Camera cam = Camera.main != null ? Camera.main : Object.FindFirstObjectByType<Camera>();
            if (cam != null && cam.GetComponent<CinemachineBrain>() == null) { cam.gameObject.AddComponent<CinemachineBrain>(); }

            // interaction prompt HUD
            var canvasGo = new GameObject("HUD", typeof(RectTransform));
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            RectTransform root = ContentSetup.UiObject("InteractionPrompt", canvasGo.transform);
            root.anchorMin = root.anchorMax = root.pivot = new Vector2(0.5f, 0f);
            root.anchoredPosition = new Vector2(0f, 220f);
            root.sizeDelta = new Vector2(900f, 90f);
            root.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.45f);
            RTLTextMeshPro text = ContentSetup.MakeText("Label", root, "interact.pickup", ContentSetup.BodyFontEn, ContentSetup.BodyFontAr, 48, ButtonText);
            ContentSetup.Stretch((RectTransform)text.transform);
            var hud = canvasGo.AddComponent<InteractionPromptHud>();
            var hso = new SerializedObject(hud);
            hso.FindProperty("_label").objectReferenceValue = text.GetComponent<PowerCutCrew.Localization.LocalizedRtlText>();
            hso.FindProperty("_root").objectReferenceValue = root.gameObject;
            hso.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.SaveScene(scene);
        }
    }
}
