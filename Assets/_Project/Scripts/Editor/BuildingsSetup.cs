using System.IO;
using PowerCutCrew.Buildings;
using PowerCutCrew.Core;
using PowerCutCrew.Localization;
using PowerCutCrew.Shift;
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
    /// <summary>Builds the Prompt 05 content: tuning assets, strings, the Building prefab (marker, bubble, windows), the shift HUD and summary, the shift manager with its clipboard. Re-runnable.</summary>
    public static partial class BuildingsSetup
    {
        private const string Root = "Assets/_Project";
        private static readonly Color Cream = new Color32(0xFF, 0xF4, 0xDC, 0xFF);
        private static readonly Color Lamp = new Color32(0xFF, 0xC9, 0x3C, 0xFF);

        [MenuItem("Power Cut Crew/Setup/Build Buildings and Shift")]
        public static void Run()
        {
            Directory.CreateDirectory($"{Root}/Config/Resources");
            Directory.CreateDirectory($"{Root}/Prefabs/Buildings");
            Directory.CreateDirectory($"{Root}/Art/Materials");
            AssetDatabase.Refresh();
            ContentSetup.LoadFonts();

            EditorUtility.SetDirty(LoadOrCreate<BuildingTuning>($"{Root}/Config/Resources/BuildingTuning.asset"));   // saved again so a new field shows up in the asset
            LoadOrCreate<ShiftTuning>($"{Root}/Config/Resources/ShiftTuning.asset");
            EditorUtility.SetDirty(LoadOrCreate<HelpTuning>($"{Root}/Config/Resources/HelpTuning.asset"));
            EditorUtility.SetDirty(AssetDatabase.LoadAssetAtPath<PowerCutCrew.Net.NetTuning>($"{Root}/Config/Resources/NetTuning.asset"));
            CreateStrings();
            CreateReadabilityStrings();
            GenerateIcons();
            PatchStreams();

            GameObject hud = SavePrefab(BuildHud(), "ShiftHud");
            GameObject markerUi = SavePrefab(BuildMarkerUi(), "BuildingMarkerUI");
            GameObject building = SavePrefab(BuildBuilding(markerUi.GetComponent<BuildingMarkerUI>()), "Building");
            GameObject manager = SavePrefab(BuildShiftManager(hud.GetComponent<ShiftHud>()), "ShiftManager");

            var catalog = LoadOrCreate<BuildingsCatalog>($"{Root}/Config/Resources/BuildingsCatalog.asset");
            catalog.Building = building.GetComponent<NetworkObject>();
            catalog.ShiftManager = manager.GetComponent<NetworkObject>();
            EditorUtility.SetDirty(catalog);

            RemovePlaceholderWalls();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[Setup] buildings and shift built");
        }

        /// <summary>Cheaper snapshot streams: the chain gets the adaptive rate (and delta positions when it has no rotations).</summary>
        [MenuItem("Power Cut Crew/Setup/Patch Snapshot Streams")]
        public static void PatchStreams()
        {
            string path = $"{Root}/Prefabs/Net/Chain.prefab";
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                foreach (PowerCutCrew.Net.SnapshotStream stream in root.GetComponentsInChildren<PowerCutCrew.Net.SnapshotStream>(true))
                {
                    var so = new SerializedObject(stream);
                    so.FindProperty("_adaptiveRate").boolValue = true;
                    so.FindProperty("_deltaPositions").boolValue = !so.FindProperty("_syncRotation").boolValue;
                    so.ApplyModifiedPropertiesWithoutUndo();
                }
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
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

        private static Material Mat(string name, Color c, bool emissive = false)
        {
            string path = $"{Root}/Art/Materials/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
                AssetDatabase.CreateAsset(m, path);
            }
            m.SetColor("_BaseColor", c);
            if (emissive)
            {
                m.EnableKeyword("_EMISSION");
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                m.SetColor("_EmissionColor", c * 2.2f);
            }
            EditorUtility.SetDirty(m);
            return m;
        }

        private static GameObject SavePrefab(GameObject go, string name)
        {
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(go, $"{Root}/Prefabs/Buildings/{name}.prefab");
            Object.DestroyImmediate(go);
            return prefab;
        }

        private static GameObject Cube(string name, Transform parent, Vector3 pos, Vector3 scale, Material m, bool collider)
        {
            GameObject v = GameObject.CreatePrimitive(PrimitiveType.Cube);
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

        private static void Place(RectTransform rt, float x, float y, float w, float h)
        {
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(x, y);
            rt.sizeDelta = new Vector2(w, h);
        }

        private static LocalizedRtlText Loc(RTLTextMeshPro t) => t.GetComponent<LocalizedRtlText>();

        private static TextMeshProUGUI Plain(string name, Transform parent, string text, float size, Color color)
        {
            RectTransform rt = ContentSetup.UiObject(name, parent);
            var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            t.font = ContentSetup.BodyFontEn;
            t.text = text;
            t.fontSize = size;
            t.color = color;
            t.alignment = TextAlignmentOptions.Center;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.raycastTarget = false;
            return t;
        }

        private static Image Panel(string name, Transform parent, Color color)
        {
            RectTransform rt = ContentSetup.UiObject(name, parent);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        // ---------- the building ----------

        private static GameObject BuildBuilding(BuildingMarkerUI markerUi)
        {
            var root = new GameObject("Building");
            Material wall = Mat("Building", new Color(0.55f, 0.55f, 0.6f));
            Material dark = Mat("WindowDark", new Color(0.12f, 0.13f, 0.17f));
            Material lit = Mat("WindowLit", Lamp, true);

            // the front (towards the street) is local -z; the greybox cube is resized to the tuning entry's height at run time
            GameObject body = Cube("Body", root.transform, new Vector3(0f, 2f, 0f), new Vector3(4f, 4f, 4f), wall, true);

            var windows = new Renderer[15];
            int i = 0;
            for (int row = 0; row < 5; row++)
            {
                for (int col = 0; col < 3; col++)
                {
                    GameObject w = Cube($"Window{row}{col}", root.transform, new Vector3(-1.2f + col * 1.2f, 2.2f + row * 1.5f, -2.03f), new Vector3(0.7f, 0.7f, 0.06f), dark, false);
                    windows[i++] = w.GetComponent<Renderer>();
                }
            }

            // the world anchor of the screen-space marker: above the roof, on its front edge (BuildingNet places it by the building's height)
            var markerRoot = new GameObject("MarkerAnchor");
            markerRoot.transform.SetParent(root.transform, false);
            markerRoot.transform.localPosition = new Vector3(0f, 5.2f, -2f);
            var marker = markerRoot.AddComponent<BuildingMarker>();
            Set(marker, "_uiPrefab", markerUi);
            var bubbleScript = markerRoot.AddComponent<BuildingBubble>();
            Set(bubbleScript, "_marker", marker);

            root.AddComponent<NetworkObject>();
            var net = root.AddComponent<BuildingNet>();
            Set(net, "_marker", marker);
            Set(net, "_bubble", bubbleScript);
            SetArray(net, "_windows", windows);
            Set(net, "_windowLit", lit);
            Set(net, "_windowDark", dark);
            Set(net, "_body", body.transform);
            Set(net, "_markerRoot", markerRoot.transform);
            return root;
        }

        // ---------- HUD and summary ----------

        private static GameObject BuildHud()
        {
            var root = new GameObject("ShiftHud");
            var canvasGo = new GameObject("Canvas", typeof(RectTransform));
            canvasGo.transform.SetParent(root.transform, false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 20;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();
            var crt = (RectTransform)canvasGo.transform;
            Color text = Cream;

            // strip
            Image bar = Panel("Strip", crt, new Color(0.05f, 0.07f, 0.16f, 0.78f));
            bar.rectTransform.anchorMin = new Vector2(0.5f, 1f);
            bar.rectTransform.anchorMax = new Vector2(0.5f, 1f);
            bar.rectTransform.pivot = new Vector2(0.5f, 1f);
            bar.rectTransform.anchoredPosition = Vector2.zero;
            bar.rectTransform.sizeDelta = new Vector2(1100f, 78f);
            RTLTextMeshPro time = ContentSetup.MakeText("Time", bar.rectTransform, "shift.hud.time", ContentSetup.BodyFontEn, ContentSetup.BodyFontAr, 50f, text);
            Place((RectTransform)time.transform, -370f, 0f, 300f, 70f);
            RTLTextMeshPro money = ContentSetup.MakeText("Money", bar.rectTransform, "shift.hud.money", ContentSetup.BodyFontEn, ContentSetup.BodyFontAr, 50f, Lamp);
            Place((RectTransform)money.transform, 0f, 0f, 320f, 70f);
            RTLTextMeshPro load = ContentSetup.MakeText("Load", bar.rectTransform, "shift.hud.load", ContentSetup.BodyFontEn, ContentSetup.BodyFontAr, 50f, text);
            Place((RectTransform)load.transform, 370f, 0f, 340f, 70f);

            RTLTextMeshPro lobby = ContentSetup.MakeText("LobbyHint", crt, "shift.hud.lobby", ContentSetup.BodyFontEn, ContentSetup.BodyFontAr, 44f, Lamp);
            lobby.textWrappingMode = TextWrappingModes.Normal;
            var lrt = (RectTransform)lobby.transform;
            lrt.anchorMin = lrt.anchorMax = lrt.pivot = new Vector2(0.5f, 1f);
            lrt.anchoredPosition = new Vector2(0f, -100f);
            lrt.sizeDelta = new Vector2(1300f, 110f);

            TextMeshProUGUI countdown = Plain("Countdown", crt, "3", 360f, Lamp);
            Place(countdown.rectTransform, 0f, 60f, 600f, 420f);
            countdown.fontStyle = FontStyles.Bold;

            // summary
            var summary = new GameObject("Summary", typeof(RectTransform));
            summary.transform.SetParent(crt, false);
            ContentSetup.Stretch((RectTransform)summary.transform);
            Image dim = Panel("Dim", summary.transform, new Color(0.02f, 0.03f, 0.08f, 0.82f));
            ContentSetup.Stretch(dim.rectTransform);
            Image card = Panel("Card", summary.transform, new Color(0.08f, 0.1f, 0.22f, 0.96f));
            Place(card.rectTransform, 0f, 0f, 1000f, 860f);
            RTLTextMeshPro title = ContentSetup.MakeText("Title", card.rectTransform, "shift.sum.title", ContentSetup.BodyFontEn, ContentSetup.BodyFontAr, 78f, Lamp);
            Place((RectTransform)title.transform, 0f, 360f, 900f, 110f);
            string[] keys = { "shift.sum.uptime", "shift.sum.money", "shift.sum.angry", "shift.sum.overheats", "shift.sum.snaps", "shift.sum.pulls" };
            var lines = new RTLTextMeshPro[keys.Length];
            for (int i = 0; i < keys.Length; i++)
            {
                lines[i] = ContentSetup.MakeText($"Line{i}", card.rectTransform, keys[i], ContentSetup.BodyFontEn, ContentSetup.BodyFontAr, 52f, text);
                Place((RectTransform)lines[i].transform, 0f, 235f - i * 78f, 900f, 70f);
            }
            RTLTextMeshPro verdict = ContentSetup.MakeText("Verdict", card.rectTransform, "shift.verdict.good", ContentSetup.BodyFontEn, ContentSetup.BodyFontAr, 56f, Lamp);
            verdict.textWrappingMode = TextWrappingModes.Normal;
            Place((RectTransform)verdict.transform, 0f, -245f, 900f, 130f);

            var hostButtons = new GameObject("HostButtons", typeof(RectTransform));
            hostButtons.transform.SetParent(card.rectTransform, false);
            Place((RectTransform)hostButtons.transform, 0f, -365f, 900f, 100f);
            Button again = ContentSetup.MakeButton("AnotherShift", hostButtons.transform, "shift.sum.again", new Vector2(420f, 90f));
            Place((RectTransform)again.transform, -230f, 0f, 420f, 90f);
            Button menu = ContentSetup.MakeButton("BackToMenu", hostButtons.transform, "shift.sum.menu", new Vector2(420f, 90f));
            Place((RectTransform)menu.transform, 230f, 0f, 420f, 90f);
            RTLTextMeshPro waiting = ContentSetup.MakeText("Waiting", card.rectTransform, "shift.sum.waiting", ContentSetup.BodyFontEn, ContentSetup.BodyFontAr, 50f, text);
            Place((RectTransform)waiting.transform, 0f, -365f, 900f, 90f);

            var hud = root.AddComponent<ShiftHud>();
            Set(hud, "_time", Loc(time));
            Set(hud, "_money", Loc(money));
            Set(hud, "_load", Loc(load));
            Set(hud, "_lobbyHint", Loc(lobby));
            Set(hud, "_countdown", countdown);
            Set(hud, "_summaryRoot", summary);
            Set(hud, "_sumUptime", Loc(lines[0]));
            Set(hud, "_sumMoney", Loc(lines[1]));
            Set(hud, "_sumAngry", Loc(lines[2]));
            Set(hud, "_sumOverheats", Loc(lines[3]));
            Set(hud, "_sumSnaps", Loc(lines[4]));
            Set(hud, "_sumPulls", Loc(lines[5]));
            Set(hud, "_sumVerdict", Loc(verdict));
            Set(hud, "_hostButtons", hostButtons);
            Set(hud, "_waiting", waiting.gameObject);
            Set(hud, "_againButton", again);
            Set(hud, "_menuButton", menu);
            AddHelp(root, crt);
            return root;
        }

        // ---------- the shift manager (with the clipboard) ----------

        private static GameObject BuildShiftManager(ShiftHud hudPrefab)
        {
            var root = new GameObject("ShiftManager");
            Material board = Mat("ClipboardBoard", new Color(0.45f, 0.3f, 0.15f));
            Material paper = Mat("ClipboardPaper", new Color(0.95f, 0.93f, 0.85f));
            GameObject stand = Cube("Stand", root.transform, new Vector3(0f, 0.45f, 0f), new Vector3(0.08f, 0.9f, 0.08f), Mat("GeneratorParts", new Color(0.18f, 0.18f, 0.2f)), false);
            GameObject clip = Cube("Clipboard", root.transform, new Vector3(0f, 1.05f, 0f), new Vector3(0.55f, 0.75f, 0.05f), board, true);
            clip.transform.localRotation = Quaternion.Euler(-12f, 0f, 0f);
            Cube("Paper", clip.transform, new Vector3(0f, 0f, -0.6f), new Vector3(0.82f, 0.86f, 0.2f), paper, false);
            var interactable = clip.AddComponent<ShiftClipboard>();
            var iso = new SerializedObject(interactable);
            iso.FindProperty("_promptKey").stringValue = "shift.start";
            iso.FindProperty("_maxRange").floatValue = 3f;
            iso.FindProperty("_hold").boolValue = false;
            iso.ApplyModifiedPropertiesWithoutUndo();

            root.AddComponent<NetworkObject>();
            var shift = root.AddComponent<ShiftNet>();
            Set(shift, "_hudPrefab", hudPrefab);
            if (stand == null) { Debug.Log("[Setup] clipboard stand missing"); }
            return root;
        }

        // ---------- the greybox street ----------

        private static void RemovePlaceholderWalls()
        {
            var scene = EditorSceneManager.OpenScene($"{Root}/Scenes/Street_Greybox.unity", OpenSceneMode.Single);
            bool changed = false;
            for (int i = 0; i < 4; i++)
            {
                GameObject go = GameObject.Find($"Building{i}");
                if (go != null && go.GetComponent<NetworkObject>() == null) { Object.DestroyImmediate(go); changed = true; }
            }
            if (changed) { EditorSceneManager.SaveScene(scene); }
        }

        // ---------- strings ----------

        private static void Add(StringTableCollection coll, string key, string en, string ar)
        {
            if (coll.SharedData.GetEntry(key) == null) { coll.SharedData.AddKey(key); }
            ((StringTable)coll.GetTable("en")).AddEntry(key, en);
            ((StringTable)coll.GetTable("ar")).AddEntry(key, ar);
            EditorUtility.SetDirty(coll.GetTable("en"));
            EditorUtility.SetDirty(coll.GetTable("ar"));
            EditorUtility.SetDirty(coll.SharedData);
        }

        public static readonly string[] AllKeys =
        {
            "shift.start", "shift.hud.time", "shift.hud.money", "shift.hud.load", "shift.hud.lobby",
            "shift.sum.title", "shift.sum.uptime", "shift.sum.money", "shift.sum.angry", "shift.sum.overheats", "shift.sum.snaps", "shift.sum.pulls",
            "shift.sum.again", "shift.sum.menu", "shift.sum.waiting",
            "shift.verdict.loved", "shift.verdict.good", "shift.verdict.meh", "shift.verdict.bad",
            "bld.amps_format", "bld.name.0", "bld.name.1", "bld.name.2", "bld.name.3",
        };

        private static void CreateStrings()
        {
            StringTableCollection ui = LocalizationEditorSettings.GetStringTableCollection("UI");
            Add(ui, "shift.start", "Start shift", "ابدأ الوردية");
            Add(ui, "shift.hud.time", "{0}", "{0}");
            Add(ui, "shift.hud.money", "{0} $", "{0} $");
            Add(ui, "shift.hud.load", "{0} / {1} A", "{0} / {1} أمبير");
            Add(ui, "shift.hud.lobby", "Walk to the clipboard by the generator to start the shift", "اذهب إلى اللوح بجانب المولّد لبدء الوردية");
            Add(ui, "shift.sum.title", "Shift over", "انتهت الوردية");
            Add(ui, "shift.sum.uptime", "Uptime: {0}%", "نسبة تشغيل الكهرباء: {0}%");
            Add(ui, "shift.sum.money", "Money earned: {0} $", "المال المكتسب: {0} $");
            Add(ui, "shift.sum.angry", "Angry buildings: {0}", "مبانٍ غاضبة: {0}");
            Add(ui, "shift.sum.overheats", "Generator overheats: {0}", "ارتفعت حرارة المولّد: {0} مرة");
            Add(ui, "shift.sum.snaps", "Snapped cables: {0}", "كابلات انقطعت: {0}");
            Add(ui, "shift.sum.pulls", "Failed cord pulls: {0}", "محاولات سحب فاشلة: {0}");
            Add(ui, "shift.sum.again", "Another shift", "وردية أخرى");
            Add(ui, "shift.sum.menu", "Back to menu", "العودة إلى القائمة");
            Add(ui, "shift.sum.waiting", "Waiting for host", "بانتظار المضيف");
            Add(ui, "shift.verdict.loved", "The street loves you", "الشارع بيحبكن");
            Add(ui, "shift.verdict.good", "Not bad, the neighbors will pay again", "مش بطّال، الجيران رح يدفعوا مرة تانية");
            Add(ui, "shift.verdict.meh", "The neighbors are grumbling", "الجيران عم يتذمّروا");
            Add(ui, "shift.verdict.bad", "They're buying their own generator", "عم يشتروا مولّد لحالن");
            Add(ui, "bld.amps_format", "{0} A", "{0} أمبير");
            Add(ui, "bld.name.0", "Abu Khalil's building", "بناية أبو خليل");
            Add(ui, "bld.name.1", "Umm Hassan's building", "بناية أم حسن");
            Add(ui, "bld.name.2", "Hajj Salim's building", "بناية الحاج سليم");
            Add(ui, "bld.name.3", "Madame Nada's tower", "برج مدام ندى");

            // neighbor lines: four per trigger, Lebanese dialect in Arabic; the English ones are playful equivalents, not translations
            string[][] request =
            {
                new[] { "Lights, please! We're open for business!", "بدنا كهربا هلّق يا شباب!" },
                new[] { "The fridge is sweating. Plug us in!", "البراد عم يعرق، وصّلولنا!" },
                new[] { "Hello? The AC wants a word with the generator.", "ألو؟ المكيّف بدو يحكي مع المولّد!" },
                new[] { "Any day now, boys. Any day.", "يلا يا شباب، شو عم تستنوا؟" },
            };
            string[][] unpowered =
            {
                new[] { "Where's the power?!", "وين الكهربا؟!" },
                new[] { "We paid our subscription, guys!", "دفعنا الاشتراك يا شباب!" },
                new[] { "It's dark in here. Even the cat left.", "عتمة هون، حتى القطة هربت!" },
                new[] { "My ice cream is soup now!", "البوظة صارت شوربة!" },
            };
            string[][] spike =
            {
                new[] { "Everybody switched the AC on at once. Sorry!", "كلّن شغّلوا المكيّفات سوا. سامحونا!" },
                new[] { "It's 40 degrees! The AC is on, deal with it!", "الحرّ ٤٠ درجة! المكيّف شغّال، دبّروا حالكن!" },
                new[] { "More amps! The AC is hungry!", "زيدوا أمبير، المكيّف جعان!" },
                new[] { "Ignore the power spike, it's just the AC stretching.", "ما تخافوا، هيدا المكيّف عم يتمطّى!" },
            };
            string[][] angry =
            {
                new[] { "That's it! We're calling the other generator guy!", "خلص! منتّصل بصاحب المولّد التاني!" },
                new[] { "We're not paying for this!", "ما رح ندفع قرش هيك!" },
                new[] { "Candles are cheaper than you!", "الشمع أرخص منكن!" },
                new[] { "Never again! We're moving to the other street!", "مش راجعين! منهاجر عالشارع التاني!" },
            };
            AddLines(ui, "request", request);
            AddLines(ui, "unpowered", unpowered);
            AddLines(ui, "spike", spike);
            AddLines(ui, "angry", angry);
            AssetDatabase.SaveAssets();
        }

        public static readonly string[] Triggers = { "request", "unpowered", "spike", "angry" };

        private static void AddLines(StringTableCollection ui, string trigger, string[][] lines)
        {
            for (int i = 0; i < lines.Length; i++) { Add(ui, $"bld.line.{trigger}.{i}", lines[i][0], lines[i][1]); }
        }
    }
}
