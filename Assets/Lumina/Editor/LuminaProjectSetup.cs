using System;
using System.IO;
using System.Reflection;
using Lumina.Core.Audio;
using Lumina.Core.Data;
using Lumina.Runtime;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Lumina.EditorTools
{
    /// <summary>
    /// 클린 체크아웃 → Unity 로 열기 → 자동 1회 설정:
    /// Boot 씬 생성/빌드 등록, 가로 화면 고정, (URP 패키지가 있으면) 모바일용 URP 에셋 생성·적용, 빌드용 기본 머티리얼 생성.
    /// 메뉴: Lumina/…
    /// </summary>
    [InitializeOnLoad]
    public static class LuminaProjectSetup
    {
        private const string ScenePath = "Assets/Scenes/Boot.unity";
        private const string SettingsDir = "Assets/Lumina/Settings";
        private const string MatDir = "Assets/Lumina/Resources/LuminaMaterials";

        static LuminaProjectSetup()
        {
            EditorApplication.delayCall += () =>
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode) return;
                if (!File.Exists(ScenePath)) Run(false);
            };
        }

        [MenuItem("Lumina/1. 프로젝트 설정 실행 (Boot 씬 · 빌드 · URP)")]
        public static void RunFromMenu() => Run(true);

        private static void Run(bool interactive)
        {
            if (interactive && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Directory.CreateDirectory("Assets/Scenes");
            Directory.CreateDirectory(SettingsDir);
            Directory.CreateDirectory(MatDir);

            TrySetupUrp();
            CreateBaseMaterial();

            if (!File.Exists(ScenePath))
            {
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                var app = new GameObject("GameApp");
                app.AddComponent<GameApp>();
                EditorSceneManager.SaveScene(scene, ScenePath);
            }
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };

            PlayerSettings.companyName = "LuminaDev";
            PlayerSettings.productName = "LUMINA CHRONICLE VS";
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;
            PlayerSettings.allowedAutorotateToPortrait = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            if (File.Exists(ScenePath)) EditorSceneManager.OpenScene(ScenePath);
            Debug.Log("[Lumina] 프로젝트 설정 완료: Boot 씬/빌드 목록/가로 화면/" + (GraphicsSettings.defaultRenderPipeline != null ? "URP" : "Built-in 렌더러") + ". Play 를 누르세요.");
        }

        /// <summary>URP 패키지가 설치돼 있고 파이프라인이 비어 있으면 모바일용 URP 에셋을 생성해 적용(리플렉션: 패키지 없이도 컴파일).</summary>
        private static void TrySetupUrp()
        {
            if (GraphicsSettings.defaultRenderPipeline != null) return;
            var assetType = Type.GetType("UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset, Unity.RenderPipelines.Universal.Runtime");
            var rendererType = Type.GetType("UnityEngine.Rendering.Universal.UniversalRendererData, Unity.RenderPipelines.Universal.Runtime");
            if (assetType == null || rendererType == null)
            {
                Debug.LogWarning("[Lumina] URP 패키지를 찾지 못해 Built-in 렌더러로 동작합니다(Standard 셰이더 자동 대체).");
                return;
            }
            try
            {
                var rendererData = ScriptableObject.CreateInstance(rendererType);
                var ppType = Type.GetType("UnityEngine.Rendering.Universal.PostProcessData, Unity.RenderPipelines.Universal.Runtime");
                var getDefault = ppType?.GetMethod("GetDefaultPostProcessData", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                var ppField = rendererType.GetField("postProcessData", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (getDefault != null && ppField != null) ppField.SetValue(rendererData, getDefault.Invoke(null, null));
                AssetDatabase.CreateAsset(rendererData, SettingsDir + "/Lumina_URP_Renderer.asset");
                var create = assetType.GetMethod("Create", BindingFlags.Static | BindingFlags.Public);
                var asset = create.Invoke(null, new object[] { rendererData }) as RenderPipelineAsset;
                if (asset == null) throw new Exception("UniversalRenderPipelineAsset.Create 실패");
                AssetDatabase.CreateAsset(asset, SettingsDir + "/Lumina_URP_Mobile.asset");
                GraphicsSettings.defaultRenderPipeline = asset;
                Debug.Log("[Lumina] URP 에셋 생성·적용: " + SettingsDir);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Lumina] URP 자동 설정 실패 → Built-in 렌더러로 동작합니다. 수동 설정: Project Settings > Graphics 에 URP Asset 지정.\n" + ex.Message);
            }
        }

        /// <summary>빌드에서 셰이더가 제외되지 않도록 Resources 에 기본 Lit 머티리얼을 둔다.</summary>
        private static void CreateBaseMaterial()
        {
            string path = MatDir + "/BaseLit.mat";
            Shader sh = GraphicsSettings.defaultRenderPipeline != null ? Shader.Find("Universal Render Pipeline/Lit") : Shader.Find("Standard");
            if (sh == null) sh = Shader.Find("Standard");
            if (sh == null) return;
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null)
            {
                existing.shader = sh;
                EditorUtility.SetDirty(existing);
                return;
            }
            AssetDatabase.CreateAsset(new Material(sh) { name = "BaseLit" }, path);
            var unlit = Shader.Find("Sprites/Default");
            if (unlit != null && AssetDatabase.LoadAssetAtPath<Material>(MatDir + "/BaseUnlit.mat") == null)
                AssetDatabase.CreateAsset(new Material(unlit) { name = "BaseUnlit" }, MatDir + "/BaseUnlit.mat");
        }

        [MenuItem("Lumina/2. 로컬 저장 데이터 삭제")]
        public static void DeleteSave()
        {
            if (!EditorUtility.DisplayDialog("저장 삭제", "persistentDataPath 의 lumina_save.json(+백업)을 삭제합니다.", "삭제", "취소")) return;
            foreach (var f in new[] { "lumina_save.json", "lumina_save.json.bak", "lumina_save.json.tmp" })
            {
                string p = Path.Combine(Application.persistentDataPath, f);
                if (File.Exists(p)) File.Delete(p);
            }
            Debug.Log("[Lumina] 저장 데이터 삭제: " + Application.persistentDataPath);
        }

        [MenuItem("Lumina/3. 무기별 효과음 WAV 내보내기 (청음용)")]
        public static void ExportSfx()
        {
            string dir = Path.GetFullPath("SfxPreview");
            Directory.CreateDirectory(dir);
            int n = 0;
            foreach (WeaponType w in WeaponInfo.All)
            {
                var kinds = WeaponInfo.Category(w) == WeaponCategory.Melee
                    ? new[] { SfxKind.Swing, SfxKind.Impact, SfxKind.Tail }
                    : new[] { SfxKind.Cast, SfxKind.Projectile, SfxKind.Impact, SfxKind.Tail };
                foreach (var k in kinds)
                {
                    foreach (var t in new[] { SfxTier.Light, SfxTier.Finisher, SfxTier.Ultimate })
                    {
                        WavWriter.Write(Path.Combine(dir, $"{(int)w + 1:00}_{w}_{k}_{t}.wav"), SfxSynth.RenderWeapon(w, k, t, Element.None, 0, 0.3f));
                        n++;
                    }
                }
            }
            Debug.Log($"[Lumina] 효과음 {n}개 내보냄: {dir}  (전투 타이밍 그대로의 쇼케이스는 'dotnet run --project tools/Lumina.Sim -- sfx')");
            EditorUtility.RevealInFinder(dir);
        }
    }
}
