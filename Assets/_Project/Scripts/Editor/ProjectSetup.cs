using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Rendering.Universal;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace PowerCutCrew.Editor
{
    /// <summary>One-shot project configuration (rendering, quality, player). Re-runnable. Run via -executeMethod.</summary>
    public static class ProjectSetup
    {
        private const string RenderingDir = "Assets/_Project/Settings/Rendering";

        private struct Tier
        {
            public string Name;
            public int Msaa;
            public bool SoftShadows;
            public bool Shadows;
        }

        private static readonly Tier[] Tiers =
        {
            new Tier { Name = "Low", Msaa = 1, SoftShadows = false, Shadows = false },
            new Tier { Name = "Medium", Msaa = 2, SoftShadows = false, Shadows = true },
            new Tier { Name = "High", Msaa = 4, SoftShadows = true, Shadows = true },
        };

        [MenuItem("Power Cut Crew/Setup/Configure Project")]
        public static void Run()
        {
            ImportTmpEssentials();
            MoveGlobalRenderingAssets();
            var assets = CreateUrpAssets();
            ConfigureQuality(assets);
            ConfigurePlayer();
            ConfigureMisc();
            AssetDatabase.SaveAssets();
            Debug.Log("[ProjectSetup] done");
        }

        private static void ImportTmpEssentials()
        {
            if (Directory.Exists("Assets/TextMesh Pro"))
            {
                return;
            }
            string pkg = Path.GetFullPath("Packages/com.unity.ugui/Package Resources/TMP Essential Resources.unitypackage");
            AssetDatabase.ImportPackage(pkg, false);
        }

        private static void MoveGlobalRenderingAssets()
        {
            Directory.CreateDirectory(RenderingDir);
            AssetDatabase.Refresh();
            foreach (string n in new[] { "UniversalRenderPipelineGlobalSettings.asset", "DefaultVolumeProfile.asset" })
            {
                if (File.Exists("Assets/" + n))
                {
                    string err = AssetDatabase.MoveAsset("Assets/" + n, RenderingDir + "/" + n);
                    if (!string.IsNullOrEmpty(err))
                    {
                        Debug.LogError(err);
                    }
                }
            }
        }

        private static UniversalRenderPipelineAsset[] CreateUrpAssets()
        {
            var result = new UniversalRenderPipelineAsset[Tiers.Length];
            for (int i = 0; i < Tiers.Length; i++)
            {
                Tier t = Tiers[i];
                string path = $"{RenderingDir}/URP_{t.Name}.asset";
                var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(path);
                if (asset == null)
                {
                    var renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
                    AssetDatabase.CreateAsset(renderer, $"{RenderingDir}/URP_{t.Name}_Renderer.asset");
                    asset = UniversalRenderPipelineAsset.Create(renderer);
                    AssetDatabase.CreateAsset(asset, path);
                }
                var so = new SerializedObject(asset);
                so.FindProperty("m_MSAA").intValue = t.Msaa;
                so.FindProperty("m_SupportsHDR").boolValue = true;
                so.FindProperty("m_SoftShadowsSupported").boolValue = t.SoftShadows;
                so.FindProperty("m_MainLightShadowsSupported").boolValue = t.Shadows;
                so.FindProperty("m_AdditionalLightShadowsSupported").boolValue = t.Shadows;
                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(asset);
                result[i] = asset;
            }
            return result;
        }

        private static void ConfigureQuality(UniversalRenderPipelineAsset[] assets)
        {
            var keep = new List<string>();
            foreach (Tier t in Tiers)
            {
                keep.Add(t.Name);
            }

            var qs = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/QualitySettings.asset")[0]);
            SerializedProperty levels = qs.FindProperty("m_QualitySettings");
            for (int i = levels.arraySize - 1; i >= 0; i--)
            {
                if (!keep.Contains(levels.GetArrayElementAtIndex(i).FindPropertyRelative("name").stringValue))
                {
                    levels.DeleteArrayElementAtIndex(i);
                }
            }
            for (int i = 0; i < levels.arraySize; i++)
            {
                SerializedProperty lvl = levels.GetArrayElementAtIndex(i);
                int idx = keep.IndexOf(lvl.FindPropertyRelative("name").stringValue);
                lvl.FindPropertyRelative("customRenderPipeline").objectReferenceValue = assets[idx];
            }
            // keep array in Low, Medium, High order
            qs.FindProperty("m_CurrentQuality").intValue = 2;
            SerializedProperty perPlatform = qs.FindProperty("m_PerPlatformDefaultQuality");
            if (perPlatform != null)
            {
                for (int i = 0; i < perPlatform.arraySize; i++)
                {
                    perPlatform.GetArrayElementAtIndex(i).FindPropertyRelative("second").intValue = 2;
                }
            }
            qs.ApplyModifiedProperties();

            GraphicsSettings.defaultRenderPipeline = assets[2];
            QualitySettings.SetQualityLevel(2, true);
        }

        private static void ConfigurePlayer()
        {
            PlayerSettings.companyName = "Build Syntax";
            PlayerSettings.productName = "Power Cut Crew";
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Standalone, "com.buildsyntax.powercutcrew");
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);
            PlayerSettings.SetApiCompatibilityLevel(NamedBuildTarget.Standalone, ApiCompatibilityLevel.NET_Standard);
            PlayerSettings.runInBackground = true;
            PlayerSettings.visibleInBackground = true;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 1280;
            PlayerSettings.defaultScreenHeight = 720;
            PlayerSettings.resizableWindow = true;

            // Active Input Handling = Input System Package (New) only
            var ps = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset")[0]);
            ps.FindProperty("activeInputHandler").intValue = 1;
            ps.ApplyModifiedProperties();
        }

        private static void ConfigureMisc()
        {
            EditorSettings.serializationMode = SerializationMode.ForceText;
            VersionControlSettings.mode = "Visible Meta Files";
            Time.fixedDeltaTime = 0.02f;
            UnityEditor.Analytics.AnalyticsSettings.enabled = false;
        }
    }
}
