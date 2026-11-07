using System;
using Lumina.Core.Presentation;
using UnityEngine;
using UnityEngine.Rendering;

namespace Lumina.Runtime.Battle
{
    /// <summary>
    /// 베기 명중 이펙트 v3(4×4 가산 플립북) 재생기. VfxSystem.Impact 가 베기 계열 일반 공격일 때만 호출한다.
    /// 준비가 안 됐거나(텍스처·셰이더 없음, 메뉴로 끔) 실패하면 false 를 돌려주고 호출 측은 기존 Flash 를 그대로 쓴다.
    /// 예외를 밖으로 던지지 않으며, 한 번 실패하면 이번 실행(세션) 동안 계속 Flash 로 고정한다.
    /// 판정 로직(Gate·Pool·Playback·UV)은 Core(Lumina.Core.Presentation.Flipbook)에 있고 테스트로 검증된다.
    /// Color Space 는 가정하지 않는다(기본 밝기 1.0, 실제 값은 첫 확인 때 로그로 출력).
    /// </summary>
    public static class BladeImpactFlipbook
    {
        public const string TexturePath = "LuminaArt/Vfx/Impact/Impact_Blade_4x4";
        public const string MaterialPath = "LuminaMaterials/FxAdditive";

        public static readonly ImpactFlipbookGate Gate = new ImpactFlipbookGate();

        /// <summary>밝기 배율(RGB). 기본 1.0 = 텍스처 그대로. 에디터 메뉴 Lumina/5 로 1.27 비교.</summary>
        public static float Intensity { get; private set; } = 1f;

        private static Material _material;
        private static Mesh _quad;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetSession()
        {
            Gate.Reset();
            Intensity = 1f;
#if UNITY_EDITOR
            // 키는 Editor/LuminaVfxSetup 의 메뉴와 같다.
            Gate.Enabled = UnityEditor.EditorPrefs.GetBool("Lumina.BladeImpactV3.Enabled", true);
            Intensity = UnityEditor.EditorPrefs.GetBool("Lumina.BladeImpactV3.Bright127", false) ? 1.27f : 1f;
#endif
        }

        /// <summary>재생 시도. true = v3 재생 시작, false = 호출 측이 기존 Flash 를 쓴다. 예외를 던지지 않는다.</summary>
        public static bool TryPlay(Transform root, Vector3 pos, Color color, float size)
        {
            if (root == null || !Gate.Enabled || Gate.Failed) return false;
            try
            {
                if (!Gate.ResourcesChecked) CheckResources();
                if (!Gate.Ready) return false;
                var host = root.GetComponent<FlipbookHost>();
                if (host == null) host = root.gameObject.AddComponent<FlipbookHost>();
                host.Play(_material, Quad, pos, color, size, Intensity);
                return true;
            }
            catch (Exception e)
            {
                Gate.ReportFailure();
                Debug.LogWarning("[Lumina] 베기 명중 v3 재생 실패 → 이번 실행 동안 기존 Flash 사용: " + e.Message);
                return false;
            }
        }

        /// <summary>텍스처·셰이더를 한 번만 확인하고 결과를 Gate 에 기록한다. 에셋을 수정하지 않도록 재질은 런타임 사본을 쓴다.</summary>
        private static void CheckResources()
        {
            var tex = Resources.Load<Texture2D>(TexturePath);
            bool texOk = ImpactFlipbookGate.TextureValid(tex != null, tex != null ? tex.width : 0, tex != null ? tex.height : 0);

            var mat = Resources.Load<Material>(MaterialPath);
            Shader shader = mat != null ? mat.shader : Shader.Find(ImpactFlipbookGate.ShaderName);
            bool shaderOk = ImpactFlipbookGate.ShaderValid(shader != null, shader != null ? shader.name : null, shader != null && shader.isSupported);

            Gate.SetResources(texOk, shaderOk);
            if (Gate.ResourcesOk)
            {
                if (_material != null) UnityEngine.Object.Destroy(_material);
                _material = mat != null ? new Material(mat) : new Material(shader);
                _material.name = "FxAdditive (runtime)";
                _material.mainTexture = tex;
                _material.hideFlags = HideFlags.DontSave;
            }

            if (Debug.isDebugBuild)
            {
                Debug.Log("[Lumina] 베기 명중 v3 " + (Gate.ResourcesOk ? "사용" : "미사용 → 기존 Flash") +
                          " · 텍스처 " + (texOk ? "OK" : "없음/불량") + " · 셰이더 " + (shaderOk ? "OK" : "없음/미지원") +
                          " · Color Space " + QualitySettings.activeColorSpace + " · 밝기 배율 " + Intensity.ToString("0.##"));
            }
        }

        private static Mesh Quad
        {
            get
            {
                if (_quad != null) return _quad;
                _quad = new Mesh { name = "BladeImpactV3Quad", hideFlags = HideFlags.DontSave };
                _quad.vertices = new[] { new Vector3(-0.5f, -0.5f, 0), new Vector3(0.5f, -0.5f, 0), new Vector3(0.5f, 0.5f, 0), new Vector3(-0.5f, 0.5f, 0) };
                _quad.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
                _quad.colors = new[] { Color.white, Color.white, Color.white, Color.white };
                _quad.triangles = new[] { 0, 2, 1, 0, 3, 2 };
                return _quad;
            }
        }
    }

    /// <summary>
    /// 전투 World 루트에 붙는 재생 슬롯 보관소(최대 24개, Core FlipbookPool 규칙). 전투가 끝나면 루트와 함께 파괴된다.
    /// 슬롯은 필요할 때 하나씩 만들고(한꺼번에 생성하는 부하 없음), 모두 사용 중이면 가장 오래 재생된 슬롯을 재사용한다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FlipbookHost : MonoBehaviour
    {
        private readonly FlipbookPool _pool = new FlipbookPool();
        private readonly FlipbookFx[] _slots = new FlipbookFx[FlipbookPool.DefaultCapacity];

        /// <summary>풀 소진으로 재생 중이던 이펙트를 덮어쓴 횟수(시각적으로 끊긴 횟수).</summary>
        public int StolenCount => _pool.StolenCount;

        public void Play(Material material, Mesh quad, Vector3 pos, Color color, float size, float intensity)
        {
            double now = Time.timeAsDouble;
            int slot = _pool.Acquire(now, out bool created, out _);
            if (created || _slots[slot] == null)
            {
                try
                {
                    _slots[slot] = CreateSlot(slot, material, quad);
                }
                catch
                {
                    if (created) _pool.CancelCreate(slot);
                    else _pool.Release(slot);
                    throw;
                }
            }
            _slots[slot].Play(now, pos, color, size, intensity);
        }

        internal void Release(int slot) => _pool.Release(slot);

        private FlipbookFx CreateSlot(int slot, Material material, Mesh quad)
        {
            var go = new GameObject("BladeImpactV3_" + slot);
            go.SetActive(false);
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = quad;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = material;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.lightProbeUsage = LightProbeUsage.Off;
            mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
            var fx = go.AddComponent<FlipbookFx>();
            fx.Init(this, slot, mr);
            return fx;
        }
    }

    /// <summary>
    /// 플립북 1회 재생. 시작한 프레임에 0번을 표시하고, 16/60초가 지나면 비활성화해 풀로 돌아간다.
    /// 기존 Flash 의 FadeFx(0.12초 후 Destroy)를 쓰지 않으며 크기·알파 보간도 하지 않는다(원본 타이밍 그대로).
    /// 시간은 Time.timeAsDouble 기준(기존 VFX 와 같은 시간축 — 히트스톱·일시정지 중에도 실시간 재생).
    /// </summary>
    public sealed class FlipbookFx : MonoBehaviour
    {
        private static readonly int MainTexStId = Shader.PropertyToID("_MainTex_ST");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int IntensityId = Shader.PropertyToID("_Intensity");

        private FlipbookHost _host;
        private int _slot;
        private Renderer _renderer;
        private MaterialPropertyBlock _mpb;
        private FlipbookPlayback _playback;
        private Color _color;
        private float _intensity = 1f;
        private int _shown = -1;

        internal void Init(FlipbookHost host, int slot, Renderer renderer)
        {
            _host = host;
            _slot = slot;
            _renderer = renderer;
            _mpb = new MaterialPropertyBlock();
        }

        internal void Play(double now, Vector3 pos, Color color, float size, float intensity)
        {
            transform.position = pos;
            transform.localScale = Vector3.one * size;
            _color = new Color(color.r, color.g, color.b, 1f);
            _intensity = intensity;
            _playback.Start(now);
            FaceCamera();
            Apply(0);
            if (!gameObject.activeSelf) gameObject.SetActive(true);
        }

        private void LateUpdate()
        {
            int frame = _playback.Frame(Time.timeAsDouble);
            if (frame < 0)
            {
                Finish();
                return;
            }
            FaceCamera();
            if (frame != _shown) Apply(frame);
        }

        private void Apply(int frame)
        {
            FlipbookSpec.Uv(frame, out float sx, out float sy, out float ox, out float oy);
            _mpb.SetVector(MainTexStId, new Vector4(sx, sy, ox, oy));
            _mpb.SetColor(ColorId, _color);
            _mpb.SetFloat(IntensityId, _intensity);
            _renderer.SetPropertyBlock(_mpb);
            _shown = frame;
        }

        /// <summary>카메라를 향하되 화면 기준 회전각은 0(십자 방향 고정).</summary>
        private void FaceCamera()
        {
            var cam = Camera.main;
            if (cam != null) transform.rotation = cam.transform.rotation;
        }

        private void Finish()
        {
            _playback.Stop();
            _shown = -1;
            gameObject.SetActive(false);
            if (_host != null) _host.Release(_slot);
        }
    }
}
