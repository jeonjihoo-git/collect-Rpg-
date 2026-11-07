using System.IO;
using UnityEditor;
using UnityEngine;

namespace Lumina.EditorTools
{
    /// <summary>
    /// 베기 명중 v3 플립북용 에디터 설정.
    /// ① LuminaArt/Vfx 텍스처 임포트 규칙(저장소가 .meta 를 커밋하지 않으므로 어느 PC 에서 열어도 같은 설정이 되게 함)
    /// ② 가산 재질 Resources/LuminaMaterials/FxAdditive.mat 이 없을 때만 생성(빌드에서 셰이더가 빠지지 않도록 참조 유지)
    /// ③ A·B 비교 메뉴(EditorPrefs, 다음 Play 부터 적용). 게임 UI 는 바꾸지 않는다.
    /// 기존 자동 설정(LuminaProjectSetup)과 독립적으로 동작한다.
    /// </summary>
    [InitializeOnLoad]
    public static class LuminaVfxSetup
    {
        public const string MaterialPath = "Assets/Lumina/Resources/LuminaMaterials/FxAdditive.mat";
        public const string ShaderName = "Lumina/FxAdditive";

        // 런타임(BladeImpactFlipbook)이 같은 키를 읽는다.
        public const string PrefEnabled = "Lumina.BladeImpactV3.Enabled";
        public const string PrefBright = "Lumina.BladeImpactV3.Bright127";

        private const string MenuEnabled = "Lumina/4. 베기 명중 v3 사용 (A·B 비교)";
        private const string MenuBright = "Lumina/5. 베기 명중 밝기 1.27배 (비교)";

        static LuminaVfxSetup()
        {
            EditorApplication.delayCall += EnsureMaterial;
            EditorApplication.playModeStateChanged += s =>
            {
                if (s == PlayModeStateChange.ExitingEditMode) EnsureMaterial();
            };
        }

        /// <summary>가산 재질이 없으면 만든다(있으면 셰이더만 맞춘다). 셰이더를 못 찾으면 아무것도 하지 않는다 → 런타임은 기존 Flash 사용.</summary>
        public static void EnsureMaterial()
        {
            if (EditorApplication.isPlaying) return;
            var shader = Shader.Find(ShaderName);
            if (shader == null) return;
            var existing = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (existing != null)
            {
                if (existing.shader != shader)
                {
                    existing.shader = shader;
                    EditorUtility.SetDirty(existing);
                    AssetDatabase.SaveAssets();
                }
                return;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(MaterialPath));
            AssetDatabase.CreateAsset(new Material(shader) { name = "FxAdditive" }, MaterialPath);
            AssetDatabase.SaveAssets();
            Debug.Log("[Lumina] 명중 이펙트 가산 재질 생성: " + MaterialPath);
        }

        [MenuItem(MenuEnabled, false, 40)]
        private static void ToggleEnabled()
        {
            bool v = !EditorPrefs.GetBool(PrefEnabled, true);
            EditorPrefs.SetBool(PrefEnabled, v);
            Debug.Log("[Lumina] 베기 명중 v3 " + (v ? "켜짐" : "꺼짐(기존 Flash)") + " — 다음 Play 부터 적용");
        }

        [MenuItem(MenuEnabled, true)]
        private static bool ToggleEnabledValidate()
        {
            Menu.SetChecked(MenuEnabled, EditorPrefs.GetBool(PrefEnabled, true));
            return true;
        }

        [MenuItem(MenuBright, false, 41)]
        private static void ToggleBright()
        {
            bool v = !EditorPrefs.GetBool(PrefBright, false);
            EditorPrefs.SetBool(PrefBright, v);
            Debug.Log("[Lumina] 베기 명중 밝기 배율 " + (v ? "1.27" : "1.0") + " — 다음 Play 부터 적용");
        }

        [MenuItem(MenuBright, true)]
        private static bool ToggleBrightValidate()
        {
            Menu.SetChecked(MenuBright, EditorPrefs.GetBool(PrefBright, false));
            return true;
        }
    }

    /// <summary>
    /// Assets/Lumina/Resources/LuminaArt/Vfx/ 아래 텍스처 임포트 규칙(GPT Unity 테스트 검증값과 동일).
    /// 스프라이트 분할을 쓰지 않고 UV 로 칸을 고르므로 Texture Type 은 Default.
    /// </summary>
    public sealed class LuminaVfxTextureImport : AssetPostprocessor
    {
        private const string VfxDir = "Assets/Lumina/Resources/LuminaArt/Vfx/";

        private void OnPreprocessTexture()
        {
            if (!assetPath.Replace('\\', '/').StartsWith(VfxDir)) return;
            var ti = (TextureImporter)assetImporter;
            ti.textureType = TextureImporterType.Default;
            ti.sRGBTexture = true;
            ti.alphaSource = TextureImporterAlphaSource.FromInput;
            ti.alphaIsTransparency = true;
            ti.wrapMode = TextureWrapMode.Clamp;
            ti.filterMode = FilterMode.Bilinear;
            ti.mipmapEnabled = true;
            ti.maxTextureSize = 1024;

            var android = ti.GetPlatformTextureSettings("Android");
            android.overridden = true;
            android.maxTextureSize = 1024;
            android.format = TextureImporterFormat.ASTC_6x6;
            android.compressionQuality = 100;
            ti.SetPlatformTextureSettings(android);
        }
    }
}
