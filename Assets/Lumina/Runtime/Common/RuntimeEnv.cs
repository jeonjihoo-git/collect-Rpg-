using System;
using System.Collections.Generic;
using Lumina.Core.Data;
using UnityEngine;
using UnityEngine.Rendering;

namespace Lumina.Runtime
{
    /// <summary>테마 색상(잉크 네이비 · 청록 · 빈티지 골드)과 속성 색.</summary>
    public static class Theme
    {
        public static readonly Color Ink = Hex("121A2C");
        public static readonly Color Panel = Hex("1B2540");
        public static readonly Color PanelLight = Hex("26324F");
        public static readonly Color Teal = Hex("2BB5A6");
        public static readonly Color TealDark = Hex("1D7F75");
        public static readonly Color Gold = Hex("C9A55C");
        public static readonly Color Text = Hex("E8E6DF");
        public static readonly Color Dim = Hex("8A93A8");
        public static readonly Color Danger = Hex("E05A5A");
        public static readonly Color Ok = Hex("6BD49A");
        public static readonly Color Locked = Hex("3A4258");
        public static readonly Color EnemyWarn = Hex("FF4A3A");

        public static Color Hex(string hex)
        {
            ColorUtility.TryParseHtmlString("#" + hex, out var c);
            return c;
        }

        public static Color Element(Element e)
        {
            switch (e)
            {
                case Lumina.Core.Data.Element.Fire: return Hex("FF6A3D");
                case Lumina.Core.Data.Element.Water: return Hex("3DA5FF");
                case Lumina.Core.Data.Element.Grass: return Hex("6CCB4A");
                case Lumina.Core.Data.Element.Ice: return Hex("9FE6FF");
                case Lumina.Core.Data.Element.Rock: return Hex("C9A065");
                case Lumina.Core.Data.Element.Light: return Hex("FFE27A");
                case Lumina.Core.Data.Element.Dark: return Hex("8E62E8");
                case Lumina.Core.Data.Element.Wind: return Hex("7FF0C8");
                case Lumina.Core.Data.Element.Lightning: return Hex("C08CFF");
                default: return Color.white;
            }
        }

        public static Color WithAlpha(Color c, float a) => new Color(c.r, c.g, c.b, a);
    }

    /// <summary>
    /// 런타임 머티리얼/폰트/텍스처 공급. URP 가 설정되어 있으면 URP Lit, 아니면 Built-in Standard 로 자동 대체.
    /// 빌드 시 셰이더 누락 방지를 위해 Resources/LuminaMaterials/BaseLit.mat(에디터 설정 메뉴가 생성)을 우선 사용.
    /// </summary>
    public static class Mats
    {
        private static Shader _lit;
        private static Shader _unlit;
        private static Texture2D _softDot;
        private static readonly Dictionary<int, Material> LitCache = new Dictionary<int, Material>();
        private static readonly Dictionary<int, Material> UnlitCache = new Dictionary<int, Material>();
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        public static bool IsUrp => GraphicsSettings.currentRenderPipeline != null;

        public static Shader LitShader
        {
            get
            {
                if (_lit != null) return _lit;
                var baseMat = Resources.Load<Material>("LuminaMaterials/BaseLit");
                if (baseMat != null && baseMat.shader != null && baseMat.shader.isSupported) _lit = baseMat.shader;
                if (_lit == null && IsUrp) _lit = Shader.Find("Universal Render Pipeline/Lit");
                if (_lit == null) _lit = Shader.Find("Standard");
                if (_lit == null) _lit = Shader.Find("Universal Render Pipeline/Simple Lit");
                return _lit;
            }
        }

        public static Shader UnlitShader
        {
            get
            {
                if (_unlit == null) _unlit = Shader.Find("Sprites/Default");
                return _unlit;
            }
        }

        private static int Key(Color c) => ((int)(c.r * 255) << 24) ^ ((int)(c.g * 255) << 16) ^ ((int)(c.b * 255) << 8) ^ (int)(c.a * 255);

        /// <summary>색상별 공유 Lit 머티리얼(드로우콜 절약).</summary>
        public static Material Lit(Color c, float smoothness = 0.35f)
        {
            int k = Key(c) ^ ((int)(smoothness * 100) << 4);
            if (LitCache.TryGetValue(k, out var m) && m != null) return m;
            m = new Material(LitShader) { name = "LuminaLit" };
            SetColor(m, c);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smoothness);
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", smoothness);
            LitCache[k] = m;
            return m;
        }

        /// <summary>반투명 무광(이펙트·장판·예고). 정점색/알파 지원.</summary>
        public static Material Unlit(Color c)
        {
            int k = Key(c);
            if (UnlitCache.TryGetValue(k, out var m) && m != null) return m;
            m = new Material(UnlitShader) { name = "LuminaUnlit" };
            m.color = c;
            UnlitCache[k] = m;
            return m;
        }

        /// <summary>개별 인스턴스(색 애니메이션용).</summary>
        public static Material UnlitInstance(Color c)
        {
            var m = new Material(UnlitShader) { name = "LuminaUnlitInst" };
            m.color = c;
            return m;
        }

        public static Material ParticleMaterial(Color c)
        {
            var m = UnlitInstance(c);
            m.mainTexture = SoftDot;
            return m;
        }

        public static void SetColor(Material m, Color c)
        {
            if (m.HasProperty(BaseColorId)) m.SetColor(BaseColorId, c);
            if (m.HasProperty(ColorId)) m.SetColor(ColorId, c);
        }

        public static void SetBlockColor(MaterialPropertyBlock block, Color c)
        {
            block.SetColor(BaseColorId, c);
            block.SetColor(ColorId, c);
        }

        public static Texture2D SoftDot
        {
            get
            {
                if (_softDot != null) return _softDot;
                const int n = 64;
                _softDot = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "SoftDot" };
                var px = new Color32[n * n];
                for (int y = 0; y < n; y++)
                {
                    for (int x = 0; x < n; x++)
                    {
                        float dx = (x + 0.5f) / n * 2f - 1f;
                        float dy = (y + 0.5f) / n * 2f - 1f;
                        float d = Mathf.Sqrt(dx * dx + dy * dy);
                        float a = Mathf.Clamp01(1f - d);
                        a = a * a * (3f - 2f * a);
                        px[y * n + x] = new Color32(255, 255, 255, (byte)(a * 255));
                    }
                }
                _softDot.SetPixels32(px);
                _softDot.Apply();
                return _softDot;
            }
        }
    }

    /// <summary>한글 표시용 OS 폰트(Windows 맑은 고딕, Android Noto CJK 등). 실패 시 Unity 기본 폰트.</summary>
    public static class Fonts
    {
        private static Font _main;

        public static Font Main
        {
            get
            {
                if (_main != null) return _main;
                var preferred = new List<string> { "Malgun Gothic", "맑은 고딕", "Apple SD Gothic Neo", "Noto Sans CJK KR", "Noto Sans KR", "NanumGothic", "Droid Sans Fallback" };
                try
                {
                    foreach (var n in Font.GetOSInstalledFontNames())
                    {
                        string l = n.ToLowerInvariant();
                        if ((l.Contains("cjk") || l.Contains("kr") || l.Contains("korean") || l.Contains("gothic")) && !preferred.Contains(n)) preferred.Add(n);
                    }
                }
                catch (Exception)
                {
                    // 일부 플랫폼은 OS 폰트 목록을 제공하지 않음
                }
                preferred.Add("Arial");
                try
                {
                    _main = Font.CreateDynamicFontFromOSFont(preferred.ToArray(), 32);
                }
                catch (Exception)
                {
                    _main = null;
                }
                if (_main == null)
                {
                    foreach (var builtin in new[] { "LegacyRuntime.ttf", "Arial.ttf" })
                    {
                        try
                        {
                            _main = Resources.GetBuiltinResource<Font>(builtin);
                        }
                        catch (Exception)
                        {
                            _main = null;
                        }
                        if (_main != null) break;
                    }
                }
                return _main;
            }
        }
    }

    public static class V
    {
        /// <summary>코어 평면 좌표 → 월드(Y 높이).</summary>
        public static Vector3 W(Lumina.Core.Util.Vec2 v, float y = 0f) => new Vector3(v.x, y, v.y);

        public static Quaternion Facing(Lumina.Core.Util.Vec2 dir)
        {
            if (dir.SqrLength < 1e-6f) return Quaternion.identity;
            return Quaternion.LookRotation(new Vector3(dir.x, 0f, dir.y), Vector3.up);
        }
    }
}
