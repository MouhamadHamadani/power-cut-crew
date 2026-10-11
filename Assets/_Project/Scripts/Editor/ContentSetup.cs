using System.Collections.Generic;
using System.IO;
using PowerCutCrew.Localization;
using PowerCutCrew.UI;
using RTLTMPro;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.Localization;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem.UI;
using UnityEngine.Localization;
using UnityEngine.Localization.Metadata;
using UnityEngine.Localization.Settings;
using UnityEngine.Localization.Tables;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace PowerCutCrew.Editor
{
    /// <summary>One-shot content creation: TMP resources, font assets, localization tables, scenes.</summary>
    public static class ContentSetup
    {
        private const string FontDir = "Assets/_Project/Art/Fonts";
        private const string LocDir = "Assets/_Project/Settings/Localization";
        private const string SceneDir = "Assets/_Project/Scenes";
        private const string RenderingDir = "Assets/_Project/Settings/Rendering";

        private static readonly Color Background = new Color32(0x0F, 0x16, 0x30, 0xFF);
        private static readonly Color Title = new Color32(0xFF, 0xC9, 0x3C, 0xFF);
        private static readonly Color ButtonText = new Color32(0xFF, 0xF4, 0xDC, 0xFF);
        private static readonly Color ButtonFill = new Color32(0x1E, 0x2A, 0x52, 0xFF);

        private static TMP_FontAsset _bowlby, _lalezar, _notoSans, _notoArabic;

        [MenuItem("Power Cut Crew/Setup/Import TMP Essentials")]
        public static void ImportTmp()
        {
            if (Directory.Exists("Assets/TextMesh Pro"))
            {
                EditorApplication.Exit(0);
                return;
            }
            AssetDatabase.importPackageCompleted += _ => EditorApplication.Exit(0);
            AssetDatabase.importPackageFailed += (_, e) =>
            {
                Debug.LogError("TMP import failed: " + e);
                EditorApplication.Exit(1);
            };
            AssetDatabase.ImportPackage(Path.GetFullPath("Packages/com.unity.ugui/Package Resources/TMP Essential Resources.unitypackage"), false);
        }

        [MenuItem("Power Cut Crew/Setup/Build Content")]
        public static void Build()
        {
            CreateFonts();
            CreateLocalization();
            CreateScenes();
            AssetDatabase.SaveAssets();
            Debug.Log("[ContentSetup] done");
        }

        // ---------- fonts ----------

        private static TMP_FontAsset MakeFont(string ttf, string assetName)
        {
            string path = $"{FontDir}/{assetName}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
            if (existing != null)
            {
                return existing;
            }
            var font = AssetDatabase.LoadAssetAtPath<Font>($"{FontDir}/{ttf}");
            var fa = TMP_FontAsset.CreateFontAsset(font, 90, 9, UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA, 1024, 1024,
                AtlasPopulationMode.Dynamic, true);
            fa.name = assetName;
            AssetDatabase.CreateAsset(fa, path);
            fa.material.name = assetName + " Material";
            AssetDatabase.AddObjectToAsset(fa.material, fa);
            foreach (Texture2D tex in fa.atlasTextures)
            {
                tex.name = assetName + " Atlas";
                AssetDatabase.AddObjectToAsset(tex, fa);
            }
            return fa;
        }

        private static uint[] Range(uint from, uint to)
        {
            var list = new List<uint>();
            for (uint c = from; c <= to; c++)
            {
                list.Add(c);
            }
            return list.ToArray();
        }

        private static void LogCoverage(TMP_FontAsset fa, string label, uint from, uint to)
        {
            fa.TryAddCharacters(Range(from, to), out uint[] missing);
            int total = (int)(to - from + 1);
            Debug.Log($"[Fonts] {fa.name} {label} U+{from:X4}-{to:X4}: {total - (missing?.Length ?? 0)}/{total} glyphs available");
        }

        private static void CreateFonts()
        {
            _bowlby = MakeFont("BowlbyOne-Regular.ttf", "BowlbyOne SDF");
            _lalezar = MakeFont("Lalezar-Regular.ttf", "Lalezar SDF");
            _notoSans = MakeFont("NotoSans-Variable.ttf", "NotoSans SDF");
            _notoArabic = MakeFont("NotoSansArabic-Variable.ttf", "NotoSansArabic SDF");

            // Mixed-string support: each font falls back to the other script's body font.
            _bowlby.fallbackFontAssetTable = new List<TMP_FontAsset> { _notoSans, _notoArabic };
            _lalezar.fallbackFontAssetTable = new List<TMP_FontAsset> { _notoArabic, _notoSans };
            _notoSans.fallbackFontAssetTable = new List<TMP_FontAsset> { _notoArabic };
            _notoArabic.fallbackFontAssetTable = new List<TMP_FontAsset> { _notoSans };

            // Verify coverage of the required ranges (dynamic atlases populate on demand at runtime).
            foreach (TMP_FontAsset fa in new[] { _lalezar, _notoArabic })
            {
                LogCoverage(fa, "Arabic", 0x0600, 0x06FF);
                LogCoverage(fa, "Presentation Forms-A", 0xFB50, 0xFDFF);
                LogCoverage(fa, "Presentation Forms-B", 0xFE70, 0xFEFF);
                LogCoverage(fa, "Basic Latin", 0x0020, 0x007E);
                fa.ClearFontAssetData(true);
            }
            foreach (TMP_FontAsset fa in new[] { _bowlby, _lalezar, _notoSans, _notoArabic })
            {
                fa.ClearFontAssetData(true);
                EditorUtility.SetDirty(fa);
            }
        }

        // ---------- localization ----------

        private static void CreateLocalization()
        {
            Directory.CreateDirectory(LocDir);
            AssetDatabase.Refresh();

            var settings = LocalizationEditorSettings.ActiveLocalizationSettings;
            if (settings == null)
            {
                settings = ScriptableObject.CreateInstance<LocalizationSettings>();
                AssetDatabase.CreateAsset(settings, $"{LocDir}/Localization Settings.asset");
                LocalizationEditorSettings.ActiveLocalizationSettings = settings;
            }

            Locale en = EnsureLocale("en");
            Locale ar = EnsureLocale("ar");

            // English is the default regardless of the system locale.
            var selectors = settings.GetStartupLocaleSelectors();
            selectors.Clear();
            selectors.Add(new SpecificLocaleSelector { LocaleId = new LocaleIdentifier("en") });
            EditorUtility.SetDirty(settings);

            StringTableCollection ui = LocalizationEditorSettings.GetStringTableCollection("UI");
            if (ui == null)
            {
                ui = LocalizationEditorSettings.CreateStringTableCollection("UI", LocDir, new List<Locale> { en, ar });
            }
            Add(ui, "game.title", "Power Cut Crew", "شباب الموتور");
            Add(ui, "menu.play", "Play", "العب");
            Add(ui, "menu.settings", "Settings", "الإعدادات");
            Add(ui, "menu.quit", "Quit", "خروج");
            // Label of the temporary language toggle: shows the language you would switch to.
            Add(ui, "menu.language", "العربية", "English");
            AssetDatabase.SaveAssets();
        }

        private static Locale EnsureLocale(string code)
        {
            Locale existing = LocalizationEditorSettings.GetLocale(new LocaleIdentifier(code));
            if (existing != null)
            {
                return existing;
            }
            Locale locale = Locale.CreateLocale(new LocaleIdentifier(code));
            AssetDatabase.CreateAsset(locale, $"{LocDir}/{locale.LocaleName.Replace(' ', '_').Replace("(", "").Replace(")", "")}.asset");
            LocalizationEditorSettings.AddLocale(locale);
            return locale;
        }

        private static void Add(StringTableCollection coll, string key, string en, string ar)
        {
            if (coll.SharedData.GetEntry(key) == null)
            {
                coll.SharedData.AddKey(key);
            }
            ((StringTable)coll.GetTable("en")).AddEntry(key, en);
            ((StringTable)coll.GetTable("ar")).AddEntry(key, ar);
            EditorUtility.SetDirty(coll.GetTable("en"));
            EditorUtility.SetDirty(coll.GetTable("ar"));
            EditorUtility.SetDirty(coll.SharedData);
        }

        // ---------- scenes ----------

        private static void CreateScenes()
        {
            _bowlby = _bowlby ? _bowlby : AssetDatabase.LoadAssetAtPath<TMP_FontAsset>($"{FontDir}/BowlbyOne SDF.asset");
            _lalezar = _lalezar ? _lalezar : AssetDatabase.LoadAssetAtPath<TMP_FontAsset>($"{FontDir}/Lalezar SDF.asset");
            _notoSans = _notoSans ? _notoSans : AssetDatabase.LoadAssetAtPath<TMP_FontAsset>($"{FontDir}/NotoSans SDF.asset");
            _notoArabic = _notoArabic ? _notoArabic : AssetDatabase.LoadAssetAtPath<TMP_FontAsset>($"{FontDir}/NotoSansArabic SDF.asset");

            BuildBootstrap();
            BuildMainMenu();
            BuildStreet();

            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene($"{SceneDir}/Bootstrap.unity", true),
                new EditorBuildSettingsScene($"{SceneDir}/MainMenu.unity", true),
                new EditorBuildSettingsScene($"{SceneDir}/Street_Greybox.unity", true),
            };
        }

        private static Scene NewScene()
        {
            return EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }

        private static Camera AddCamera(Vector3 pos, Color clear)
        {
            var go = new GameObject("Main Camera") { tag = "MainCamera" };
            go.transform.position = pos;
            var cam = go.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = clear;
            go.AddComponent<AudioListener>();
            go.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing = true;
            return cam;
        }

        private static void BuildBootstrap()
        {
            Scene s = NewScene();
            AddCamera(new Vector3(0, 1, -10), Background);
            new GameObject("Bootstrap").AddComponent<Bootstrap>();
            EditorSceneManager.SaveScene(s, $"{SceneDir}/Bootstrap.unity");
        }

        private static RectTransform UiObject(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        private static RTLTextMeshPro MakeText(string name, Transform parent, string key, TMP_FontAsset en, TMP_FontAsset ar,
            float size, Color color)
        {
            RectTransform rt = UiObject(name, parent);
            var label = rt.gameObject.AddComponent<RTLTextMeshPro>();
            label.font = en;
            label.fontSize = size;
            label.color = color;
            label.alignment = TextAlignmentOptions.Center;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.raycastTarget = false;
            var loc = rt.gameObject.AddComponent<LocalizedRtlText>();
            loc.Text = new LocalizedString("UI", key);
            loc.EnglishFont = en;
            loc.ArabicFont = ar;
            return label;
        }

        private static Button MakeButton(string name, Transform parent, string key, Vector2 size)
        {
            RectTransform rt = UiObject(name, parent);
            rt.sizeDelta = size;
            var img = rt.gameObject.AddComponent<Image>();
            img.color = ButtonFill;
            var btn = rt.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;
            var colors = btn.colors;
            colors.highlightedColor = new Color(1.4f, 1.4f, 1.4f, 1f);
            colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
            btn.colors = colors;
            var le = rt.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = size.x;
            le.preferredHeight = size.y;
            RTLTextMeshPro label = MakeText("Label", rt, key, _notoSans, _notoArabic, 48, ButtonText);
            Stretch((RectTransform)label.transform);
            return btn;
        }

        private static void BuildMainMenu()
        {
            Scene s = NewScene();
            AddCamera(new Vector3(0, 1, -10), Background);

            var es = new GameObject("EventSystem", typeof(UnityEngine.EventSystems.EventSystem));
            es.AddComponent<InputSystemUIInputModule>().AssignDefaultActions();

            var canvasGo = new GameObject("Canvas", typeof(RectTransform));
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();
            var controller = canvasGo.AddComponent<MainMenuController>();

            var bg = UiObject("Background", canvasGo.transform);
            Stretch(bg);
            bg.gameObject.AddComponent<Image>().color = Background;

            RTLTextMeshPro title = MakeText("Title", canvasGo.transform, "game.title", _bowlby, _lalezar, 150, Title);
            var trt = (RectTransform)title.transform;
            trt.anchorMin = new Vector2(0.5f, 1f);
            trt.anchorMax = new Vector2(0.5f, 1f);
            trt.pivot = new Vector2(0.5f, 1f);
            trt.anchoredPosition = new Vector2(0, -120);
            trt.sizeDelta = new Vector2(1600, 220);

            RectTransform stack = UiObject("Buttons", canvasGo.transform);
            stack.anchorMin = stack.anchorMax = stack.pivot = new Vector2(0.5f, 0.5f);
            stack.anchoredPosition = new Vector2(0, -120);
            stack.sizeDelta = new Vector2(600, 500);
            var vlg = stack.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 30;
            vlg.childAlignment = TextAnchor.UpperCenter;
            vlg.childControlWidth = vlg.childControlHeight = true;
            vlg.childForceExpandWidth = vlg.childForceExpandHeight = false;

            MakeButton("PlayButton", stack, "menu.play", new Vector2(600, 110));
            MakeButton("SettingsButton", stack, "menu.settings", new Vector2(600, 110));
            Button quit = MakeButton("QuitButton", stack, "menu.quit", new Vector2(600, 110));
            UnityEventTools.AddPersistentListener(quit.onClick, controller.Quit);

            Button toggle = MakeButton("LanguageToggle", canvasGo.transform, "menu.language", new Vector2(260, 80));
            var lrt = (RectTransform)toggle.transform;
            lrt.anchorMin = lrt.anchorMax = lrt.pivot = new Vector2(1f, 0f);
            lrt.anchoredPosition = new Vector2(-40, 40);
            Object.DestroyImmediate(toggle.GetComponent<LayoutElement>());
            UnityEventTools.AddPersistentListener(toggle.onClick, controller.ToggleLanguage);

            EditorSceneManager.SaveScene(s, $"{SceneDir}/MainMenu.unity");
        }

        private static void BuildStreet()
        {
            Scene s = NewScene();
            Color night = new Color(0.02f, 0.03f, 0.07f);
            Camera cam = AddCamera(new Vector3(0, 5, -12), night);
            cam.transform.rotation = Quaternion.Euler(18, 0, 0);

            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.localScale = new Vector3(6, 1, 6);

            var moon = new GameObject("Moon");
            moon.transform.rotation = Quaternion.Euler(50, -30, 0);
            var ml = moon.AddComponent<Light>();
            ml.type = LightType.Directional;
            ml.color = new Color(0.55f, 0.65f, 1f);
            ml.intensity = 0.4f;
            ml.shadows = LightShadows.Soft;

            var lamp = new GameObject("Warm Light");
            lamp.transform.position = new Vector3(0, 3, 0);
            var pl = lamp.AddComponent<Light>();
            pl.type = LightType.Point;
            pl.color = new Color(1f, 0.7f, 0.4f);
            pl.intensity = 20f;
            pl.range = 15f;

            string profilePath = $"{RenderingDir}/Street_VolumeProfile.asset";
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(profilePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, profilePath);
                var bloom = profile.Add<UnityEngine.Rendering.Universal.Bloom>(true);
                bloom.threshold.Override(0.9f);
                bloom.intensity.Override(0.8f);
                bloom.scatter.Override(0.6f);
                EditorUtility.SetDirty(profile);
            }
            var vol = new GameObject("Global Volume").AddComponent<Volume>();
            vol.isGlobal = true;
            vol.sharedProfile = profile;

            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.05f, 0.07f, 0.14f);

            EditorSceneManager.SaveScene(s, $"{SceneDir}/Street_Greybox.unity");
        }
    }
}
