using System;
using System.Collections.Generic;
using System.IO;
using PowerCutCrew.Buildings;
using PowerCutCrew.Localization;
using PowerCutCrew.Shift;
using RTLTMPro;
using TMPro;
using UnityEditor;
using UnityEditor.Localization;
using UnityEngine;
using UnityEngine.UI;

namespace PowerCutCrew.Editor
{
    /// <summary>Prompt 06: the screen-space marker prefab, the status icons (drawn here, nothing downloaded), the legend, the controls card, the corner hint and the tips.</summary>
    public static partial class BuildingsSetup
    {
        private static readonly Color Ink = new Color(0.05f, 0.07f, 0.16f, 1f);
        private static Sprite[] _statusIcons;
        private static Sprite _arrowSprite;
        private static Sprite _dotSprite;

        // ---------- icons ----------

        private static Sprite MakeIcon(string name, Func<float, float, bool> inside)
        {
            const int size = 128;
            const int ss = 3;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    int hit = 0;
                    for (int sy = 0; sy < ss; sy++)
                    {
                        for (int sx = 0; sx < ss; sx++)
                        {
                            if (inside(x + (sx + 0.5f) / ss, y + (sy + 0.5f) / ss)) { hit++; }
                        }
                    }
                    pixels[y * size + x] = new Color(1f, 1f, 1f, hit / (float)(ss * ss));
                }
            }
            tex.SetPixels(pixels);
            Directory.CreateDirectory($"{Root}/Art/Icons");
            string path = $"{Root}/Art/Icons/{name}.png";
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.filterMode = FilterMode.Bilinear;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        private static bool InPolygon(float x, float y, float[] pts)
        {
            bool inside = false;
            int n = pts.Length / 2;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                float xi = pts[i * 2], yi = pts[i * 2 + 1], xj = pts[j * 2], yj = pts[j * 2 + 1];
                if ((yi > y) != (yj > y) && x < (xj - xi) * (y - yi) / (yj - yi) + xi) { inside = !inside; }
            }
            return inside;
        }

        private static float Dist(float x, float y, float cx, float cy) => Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));

        private static float SegmentDistance(float x, float y, float ax, float ay, float bx, float by)
        {
            float dx = bx - ax, dy = by - ay;
            float t = Mathf.Clamp01(((x - ax) * dx + (y - ay) * dy) / (dx * dx + dy * dy));
            return Dist(x, y, ax + t * dx, ay + t * dy);
        }

        /// <summary>White shapes on transparent, tinted in the UI: a moon (Quiet), a bulb (Powered), a lightning bolt (Unpowered), an angry face (Angry), an arrow.</summary>
        private static void GenerateIcons()
        {
            Sprite moon = MakeIcon("status_quiet", (x, y) => Dist(x, y, 62f, 64f) < 46f && Dist(x, y, 86f, 74f) > 38f);
            Sprite bulb = MakeIcon("status_powered", (x, y) =>
                Dist(x, y, 64f, 78f) < 36f
                || (y > 36f && y < 62f && Mathf.Abs(x - 64f) < 14f + (y - 36f) * 0.45f)
                || (y > 22f && y <= 36f && Mathf.Abs(x - 64f) < 21f)
                || (y > 10f && y <= 19f && Mathf.Abs(x - 64f) < 14f));
            float[] boltPts = { 76f, 124f, 26f, 64f, 56f, 64f, 42f, 4f, 102f, 72f, 72f, 72f, 90f, 124f };
            Sprite bolt = MakeIcon("status_unpowered", (x, y) => InPolygon(x, y, boltPts));
            Sprite angry = MakeIcon("status_angry", (x, y) =>
            {
                float d = Dist(x, y, 64f, 64f);
                if (d < 58f && d > 47f) { return true; }                                                    // the head
                if (Dist(x, y, 45f, 68f) < 7.5f || Dist(x, y, 83f, 68f) < 7.5f) { return true; }          // the eyes
                if (SegmentDistance(x, y, 26f, 94f, 56f, 80f) < 5f || SegmentDistance(x, y, 102f, 94f, 72f, 80f) < 5f) { return true; }   // the brows
                float m = Dist(x, y, 64f, 30f);
                return m > 15f && m < 24f && y > 32f;                                                       // the frown
            });
            _dotSprite = MakeIcon("status_dot", (x, y) => Dist(x, y, 64f, 64f) < 58f);
            float[] arrowPts = { 8f, 10f, 120f, 64f, 8f, 118f, 34f, 64f };
            _arrowSprite = MakeIcon("status_arrow", (x, y) => InPolygon(x, y, arrowPts));
            Sprite[] breakdownIcons = MakeBreakdownIcons();
            _statusIcons = new[] { moon, bulb, bolt, angry, breakdownIcons[0], breakdownIcons[1], breakdownIcons[2] };   // the four moods, then wrench, belt, breaker
        }

        // ---------- the marker on screen ----------

        private static GameObject BuildMarkerUi()
        {
            var root = new GameObject("BuildingMarkerUI", typeof(RectTransform));
            var rt = (RectTransform)root.transform;
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            rt.pivot = new Vector2(0.5f, 0f);
            rt.sizeDelta = new Vector2(360f, 150f);
            var panel = root.AddComponent<Image>();
            panel.color = BuildingMarker.QuietColor;
            panel.raycastTarget = false;
            var group = root.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            group.interactable = false;

            Image leader = Panel("Leader", rt, new Color32(0xFF, 0xF4, 0xDC, 0xE6));
            leader.rectTransform.SetAsFirstSibling();   // behind the panel
            leader.gameObject.SetActive(false);
            Image dot = Panel("Dot", rt, BuildingMarker.QuietColor);
            dot.sprite = _dotSprite;
            Place(dot.rectTransform, 0f, 0f, 30f, 30f);
            dot.gameObject.SetActive(false);

            Image icon = Panel("Icon", rt, Ink);
            icon.sprite = _statusIcons[0];
            icon.preserveAspect = true;
            Place(icon.rectTransform, -125f, 20f, 88f, 88f);

            RTLTextMeshPro amps = ContentSetup.MakeText("Amps", rt, "bld.amps_format", ContentSetup.BodyFontEn, ContentSetup.BodyFontAr, 54f, Ink);
            amps.enableAutoSizing = true;   // the far form is narrower
            amps.fontSizeMin = 26f;
            amps.fontSizeMax = 54f;
            Place((RectTransform)amps.transform, 45f, 20f, 230f, 80f);
            RTLTextMeshPro nameText = ContentSetup.MakeText("Name", rt, "bld.name.0", ContentSetup.BodyFontEn, ContentSetup.BodyFontAr, 34f, Ink);
            nameText.enableAutoSizing = true;
            nameText.fontSizeMin = 22f;
            nameText.fontSizeMax = 34f;
            Place((RectTransform)nameText.transform, 0f, -50f, 340f, 48f);
            RTLTextMeshPro spike = ContentSetup.MakeText("Spike", rt, "bld.spike_format", ContentSetup.BodyFontEn, ContentSetup.BodyFontAr, 28f, Ink);
            Place((RectTransform)spike.transform, 150f, 58f, 70f, 36f);
            spike.gameObject.SetActive(false);

            Image arrow = Panel("Arrow", rt, BuildingMarker.UnpoweredColor);
            arrow.sprite = _arrowSprite;
            Place(arrow.rectTransform, 0f, 0f, 70f, 70f);
            arrow.gameObject.SetActive(false);

            // the speech bubble stacks directly above the marker
            Image bubble = Panel("Bubble", rt, new Color(1f, 0.97f, 0.9f, 0.97f));
            bubble.rectTransform.anchorMin = bubble.rectTransform.anchorMax = new Vector2(0.5f, 1f);
            bubble.rectTransform.pivot = new Vector2(0.5f, 0f);
            bubble.rectTransform.anchoredPosition = new Vector2(0f, 12f);
            bubble.rectTransform.sizeDelta = new Vector2(440f, 110f);
            RTLTextMeshPro line = ContentSetup.MakeText("Line", bubble.transform, "bld.line.request.0", ContentSetup.BodyFontEn, ContentSetup.BodyFontAr, 32f, new Color(0.08f, 0.09f, 0.16f));
            line.textWrappingMode = TextWrappingModes.Normal;
            line.enableAutoSizing = true;
            line.fontSizeMin = 20f;
            line.fontSizeMax = 32f;
            ContentSetup.Stretch((RectTransform)line.transform);
            ((RectTransform)line.transform).offsetMin = new Vector2(14f, 8f);
            ((RectTransform)line.transform).offsetMax = new Vector2(-14f, -8f);
            bubble.gameObject.SetActive(false);

            var ui = root.AddComponent<BuildingMarkerUI>();
            Set(ui, "_root", rt);
            Set(ui, "_panel", panel);
            Set(ui, "_icon", icon);
            Set(ui, "_arrow", arrow);
            Set(ui, "_dot", dot);
            Set(ui, "_leader", leader);
            Set(ui, "_amps", Loc(amps));
            Set(ui, "_name", Loc(nameText));
            Set(ui, "_spike", Loc(spike));
            Set(ui, "_bubble", bubble.rectTransform);
            Set(ui, "_bubbleText", Loc(line));
            Set(ui, "_group", group);
            SetArray(ui, "_icons", _statusIcons);
            return root;
        }

        // ---------- help: legend, controls card, hint, tips ----------

        private static readonly Color[] LegendColors =
        {
            BuildingMarker.QuietColor, BuildingMarker.PoweredColor, BuildingMarker.UnpoweredColor, BuildingMarker.AngryColor,
            BuildingMarker.UnpoweredColor, BuildingMarker.UnpoweredColor, BuildingMarker.UnpoweredColor,   // the breakdowns flash like an unpowered building
        };
        private static readonly string[] LegendKeys =
        {
            "help.legend.quiet", "help.legend.powered", "help.legend.unpowered", "help.legend.angry",
            "help.legend.sputter", "help.legend.belt", "help.legend.breaker",
        };
        private static readonly string[] ActionKeys =
        {
            "help.act.move", "help.act.look", "help.act.jump", "help.act.sprint", "help.act.crouch",
            "help.act.interact", "help.act.grab", "help.act.usehit", "help.act.drop", "help.act.help",
        };

        /// <summary>The seven icons (four moods, three breakdowns) with a line each. Rows are laid out left to right; the HUD mirrors the listed rectangles in Arabic.</summary>
        private static RectTransform BuildLegend(string name, Transform parent, float rowHeight, List<RectTransform> mirror)
        {
            RectTransform legend = ContentSetup.UiObject(name, parent);
            int rows = LegendKeys.Length;
            for (int i = 0; i < rows; i++)
            {
                float y = ((rows - 1) * 0.5f - i) * rowHeight;
                float icon = rowHeight * 0.82f;
                Image bg = Panel($"IconBg{i}", legend, LegendColors[i]);
                Place(bg.rectTransform, -480f, y, icon, icon);
                Image glyph = Panel("Icon", bg.transform, i == 3 ? new Color32(0xFF, 0xF4, 0xDC, 0xFF) : Ink);
                glyph.sprite = _statusIcons[i];
                glyph.preserveAspect = true;
                Place(glyph.rectTransform, 0f, 0f, icon * 0.78f, icon * 0.78f);
                RTLTextMeshPro text = ContentSetup.MakeText($"Text{i}", legend, LegendKeys[i], ContentSetup.BodyFontEn, ContentSetup.BodyFontAr, rowHeight * 0.6f, Cream);
                text.alignment = TextAlignmentOptions.Left;
                text.enableAutoSizing = true;   // the breakdown lines are longer
                text.fontSizeMin = rowHeight * 0.38f;
                text.fontSizeMax = rowHeight * 0.6f;
                Place((RectTransform)text.transform, 40f, y, 900f, rowHeight);
                mirror.Add(bg.rectTransform);
                mirror.Add((RectTransform)text.transform);
            }
            Place(legend, 0f, 0f, 1100f, rowHeight * rows);
            return legend;
        }

        private static void AddHelp(GameObject hudRoot, RectTransform canvas)
        {
            var mirror = new List<RectTransform>();

            // legend under the lobby hint, clear of the big 3-2-1
            Image legendBg = Panel("LobbyLegend", canvas, new Color(0.05f, 0.07f, 0.16f, 0.82f));
            legendBg.rectTransform.anchorMin = legendBg.rectTransform.anchorMax = legendBg.rectTransform.pivot = new Vector2(0.5f, 1f);
            legendBg.rectTransform.anchoredPosition = new Vector2(0f, -655f);
            legendBg.rectTransform.sizeDelta = new Vector2(1180f, 7f * 52f + 24f);
            RectTransform legendRows = BuildLegend("Rows", legendBg.transform, 52f, mirror);
            legendRows.anchoredPosition = Vector2.zero;

            // the corner hint
            RTLTextMeshPro hint = ContentSetup.MakeText("CornerHint", canvas, "help.hint", ContentSetup.BodyFontEn, ContentSetup.BodyFontAr, 38f, new Color(Cream.r, Cream.g, Cream.b, 0.85f));
            hint.alignment = TextAlignmentOptions.BottomLeft;
            var hrt = (RectTransform)hint.transform;
            hrt.anchorMin = hrt.anchorMax = hrt.pivot = new Vector2(0f, 0f);
            hrt.anchoredPosition = new Vector2(30f, 24f);
            hrt.sizeDelta = new Vector2(560f, 60f);
            mirror.Add(hrt);

            // the controls card
            Image card = Panel("ControlsCard", canvas, new Color(0.08f, 0.1f, 0.22f, 1f));
            Place(card.rectTransform, 0f, 0f, 1200f, 1040f);
            RTLTextMeshPro title = ContentSetup.MakeText("Title", card.rectTransform, "help.card.title", ContentSetup.BodyFontEn, ContentSetup.BodyFontAr, 64f, Lamp);
            Place((RectTransform)title.transform, 0f, 455f, 900f, 76f);
            var rowBindings = new LocalizedRtlText[ActionKeys.Length];
            for (int i = 0; i < ActionKeys.Length; i++)
            {
                float y = 385f - i * 40f;
                RTLTextMeshPro label = ContentSetup.MakeText($"Action{i}", card.rectTransform, ActionKeys[i], ContentSetup.BodyFontEn, ContentSetup.BodyFontAr, 34f, Cream);
                label.alignment = TextAlignmentOptions.Left;
                Place((RectTransform)label.transform, -300f, y, 440f, 40f);
                RTLTextMeshPro binding = ContentSetup.MakeText($"Binding{i}", card.rectTransform, "help.binding", ContentSetup.BodyFontEn, ContentSetup.BodyFontAr, 32f, Lamp);
                binding.alignment = TextAlignmentOptions.Left;
                Place((RectTransform)binding.transform, 220f, y, 600f, 40f);
                rowBindings[i] = Loc(binding);
                mirror.Add((RectTransform)label.transform);
                mirror.Add((RectTransform)binding.transform);
            }
            RectTransform cardLegend = BuildLegend("Legend", card.rectTransform, 44f, mirror);
            cardLegend.anchoredPosition = new Vector2(0f, -175f);
            RTLTextMeshPro close = ContentSetup.MakeText("CloseHint", card.rectTransform, "help.card.close", ContentSetup.BodyFontEn, ContentSetup.BodyFontAr, 34f, Cream);
            close.alignment = TextAlignmentOptions.Left;
            Place((RectTransform)close.transform, -330f, -455f, 460f, 60f);
            Button hintButton = ContentSetup.MakeButton("HintToggle", card.rectTransform, "help.hint_hide", new Vector2(520f, 70f));
            Place((RectTransform)hintButton.transform, 300f, -455f, 520f, 70f);
            mirror.Add((RectTransform)close.transform);
            mirror.Add((RectTransform)hintButton.transform);

            // tips along the bottom
            Image tip = Panel("Tip", canvas, new Color(0.05f, 0.07f, 0.16f, 0.9f));
            tip.rectTransform.anchorMin = tip.rectTransform.anchorMax = tip.rectTransform.pivot = new Vector2(0.5f, 0f);
            tip.rectTransform.anchoredPosition = new Vector2(0f, 80f);
            tip.rectTransform.sizeDelta = new Vector2(1400f, 120f);
            RTLTextMeshPro tipText = ContentSetup.MakeText("Text", tip.transform, "tip.fuel", ContentSetup.BodyFontEn, ContentSetup.BodyFontAr, 42f, Cream);
            tipText.textWrappingMode = TextWrappingModes.Normal;
            ContentSetup.Stretch((RectTransform)tipText.transform);
            ((RectTransform)tipText.transform).offsetMin = new Vector2(24f, 8f);
            ((RectTransform)tipText.transform).offsetMax = new Vector2(-24f, -8f);

            var help = hudRoot.AddComponent<HelpHud>();
            Set(help, "_legend", legendBg.gameObject);
            Set(help, "_hint", Loc(hint));
            Set(help, "_card", card.gameObject);
            SetArray(help, "_rowBindings", rowBindings);
            Set(help, "_closeText", Loc(close));
            Set(help, "_hintButton", hintButton);
            Set(help, "_hintButtonText", hintButton.GetComponentInChildren<LocalizedRtlText>(true));
            Set(help, "_tip", tip.gameObject);
            Set(help, "_tipText", Loc(tipText));
            SetArray(help, "_mirror", mirror.ToArray());
            card.gameObject.SetActive(false);
        }

        // ---------- strings ----------

        public static readonly string[] ReadabilityKeys =
        {
            "bld.spike_format",
            "help.legend.quiet", "help.legend.powered", "help.legend.unpowered", "help.legend.angry",
            "help.act.move", "help.act.look", "help.act.jump", "help.act.sprint", "help.act.crouch",
            "help.act.interact", "help.act.grab", "help.act.usehit", "help.act.drop", "help.act.help",
            "help.binding", "help.mouse", "help.hint", "help.hint_hide", "help.hint_show", "help.card.title", "help.card.close",
            "tip.plug", "tip.unpowered", "tip.fuel", "tip.overheat",
        };

        private static void CreateReadabilityStrings()
        {
            StringTableCollection ui = LocalizationEditorSettings.GetStringTableCollection("UI");
            Add(ui, "bld.spike_format", "×{0}", "×{0}");
            Add(ui, "help.legend.quiet", "Doesn't need power right now", "لا تحتاج كهرباء الآن");
            Add(ui, "help.legend.powered", "Has power", "الكهرباء واصلة");
            Add(ui, "help.legend.unpowered", "Needs a cable now!", "تحتاج كابلاً الآن!");
            Add(ui, "help.legend.angry", "Angry: stopped paying", "غاضبة: توقفت عن الدفع");
            Add(ui, "help.act.move", "Move", "تحرّك");
            Add(ui, "help.act.look", "Look", "انظر");
            Add(ui, "help.act.jump", "Jump", "اقفز");
            Add(ui, "help.act.sprint", "Sprint", "اركض");
            Add(ui, "help.act.crouch", "Crouch", "انحنِ");
            Add(ui, "help.act.interact", "Interact", "تفاعل");
            Add(ui, "help.act.grab", "Grab", "أمسك");
            Add(ui, "help.act.usehit", "Use / Hit", "استخدم / اضرب");
            Add(ui, "help.act.drop", "Drop", "أفلت");
            Add(ui, "help.act.help", "This card", "هذه البطاقة");
            Add(ui, "help.binding", "{0}", "{0}");
            Add(ui, "help.mouse", "Mouse", "الماوس");
            Add(ui, "help.hint", "controls", "التحكم");   // the key name is added by RtlBinding
            Add(ui, "help.hint_hide", "Hide the corner hint", "إخفاء التلميح في الزاوية");
            Add(ui, "help.hint_show", "Show the corner hint", "إظهار التلميح في الزاوية");
            Add(ui, "help.card.title", "Controls", "التحكم");
            Add(ui, "help.card.close", "close", "إغلاق");
            Add(ui, "tip.plug", "grab the plug and carry it to a flashing building", "أمسك القابس وانقله إلى مبنى يومض");
            Add(ui, "tip.unpowered", "A building is in the dark! Plug a cable into its socket", "مبنى بلا كهرباء! صِل كابلاً بمقبسه");
            Add(ui, "tip.fuel", "Fuel is low: fill the tank from a jerrycan or the drum", "الوقود منخفض: املأ الخزان من الجالون أو البرميل");
            Add(ui, "tip.overheat", "The generator is overheating: unplug a cable to let it cool", "المولّد يسخن: افصل كابلاً ليبرد");
        }
    }
}
