using System;
using Lumina.Core.Data;

namespace Lumina.Core.Presentation
{
    /// <summary>
    /// 베기 명중 이펙트 v3 플립북 규격(에셋 사양 고정값). 4×4 = 16프레임, 60fps, 16/60초, 15번은 완전 투명.
    /// 원본 순서: 왼쪽 위 0번 → 오른쪽 → 다음 행, 오른쪽 아래 15번. 이 값들은 승인된 에셋 타이밍이므로 바꾸지 않는다.
    /// 순수 C#(Unity 비의존): Unity 런타임과 dotnet 테스트가 같은 판정 로직을 공유한다.
    /// </summary>
    public static class FlipbookSpec
    {
        public const int Columns = 4;
        public const int Rows = 4;
        public const int Frames = Columns * Rows;
        public const int Fps = 60;
        public const int LastFrame = Frames - 1;

        /// <summary>전체 재생 길이 = 16/60초(약 0.267초).</summary>
        public const double Duration = (double)Frames / Fps;

        /// <summary>부동소수 누적 오차(예: 15.9999…/60) 때문에 프레임이 한 칸 늦게 바뀌는 것을 막는 여유값(1µs 미만 영향).</summary>
        private const double Epsilon = 1e-6;

        /// <summary>
        /// 시작 후 경과 시간(초) → 표시할 프레임(0~15). 재생이 끝났으면 -1.
        /// 시간 기준이라 30fps 기기에서는 0, 2, 4… 가 보이고 전체 길이는 같다.
        /// </summary>
        public static int FrameAt(double elapsed)
        {
            if (double.IsNaN(elapsed) || elapsed <= 0.0) return 0;
            double f = Math.Floor(elapsed * Fps + Epsilon);
            return f >= Frames ? -1 : (int)f;
        }

        /// <summary>
        /// 프레임의 UV 스케일·오프셋(텍스처 UV 원점 = 왼쪽 아래 기준). 0번 = 텍스처 왼쪽 위 칸.
        /// 범위를 벗어난 값은 0~15로 고정한다.
        /// </summary>
        public static void Uv(int frame, out float scaleX, out float scaleY, out float offsetX, out float offsetY)
        {
            if (frame < 0) frame = 0;
            if (frame > LastFrame) frame = LastFrame;
            int col = frame % Columns;
            int row = frame / Columns;
            scaleX = 1f / Columns;
            scaleY = 1f / Rows;
            offsetX = (float)col / Columns;
            offsetY = (float)(Rows - 1 - row) / Rows;
        }
    }

    /// <summary>
    /// 재생 1회의 시계. 시작 시각을 기록하고 현재 시각으로 프레임을 고른다(시작한 그 프레임 = 0번).
    /// 기존 Flash(0.12초 후 제거)와 무관하게 16/60초가 지나야만 끝난다.
    /// </summary>
    public struct FlipbookPlayback
    {
        public double StartTime { get; private set; }
        public bool Playing { get; private set; }

        public void Start(double now)
        {
            StartTime = now;
            Playing = true;
        }

        /// <summary>현재 프레임(0~15). 끝났으면 -1을 돌려주고 재생 상태를 끈다.</summary>
        public int Frame(double now)
        {
            if (!Playing) return -1;
            int f = FlipbookSpec.FrameAt(now - StartTime);
            if (f < 0) Playing = false;
            return f;
        }

        public double Elapsed(double now) => Playing ? now - StartTime : FlipbookSpec.Duration;

        public void Stop() => Playing = false;
    }

    /// <summary>베기 명중 v3 사용 대상: 베기 계열(장검·단검·쌍검·낫)의 일반 공격 등급만. 스킬·궁극기는 항상 제외.</summary>
    public static class ImpactFlipbookPolicy
    {
        public static bool IsBladeWeapon(WeaponType w) =>
            w == WeaponType.Longsword || w == WeaponType.Dagger || w == WeaponType.DualBlades || w == WeaponType.Scythe;

        /// <summary>일반 공격 등급. 스킬(Skill)·궁극기(Ultimate)는 행동 종류에서 정해지므로 여기 포함되지 않는다.</summary>
        public static bool IsNormalAttackTier(SfxTier t) =>
            t == SfxTier.Light || t == SfxTier.Medium || t == SfxTier.Heavy || t == SfxTier.Finisher;

        public static bool UsesBladeFlipbook(WeaponType w, SfxTier t) => IsBladeWeapon(w) && IsNormalAttackTier(t);
    }

    /// <summary>
    /// v3 재생 가능 여부 판정. 하나라도 충족하지 않으면 호출 측이 기존 Flash를 쓴다.
    /// 리소스 확인 결과는 한 번만 기록하고, 풀/재생 실패가 한 번이라도 나면 이후 계속 Flash로 고정한다(세션 단위).
    /// </summary>
    public sealed class ImpactFlipbookGate
    {
        public const string ShaderName = "Lumina/FxAdditive";

        private bool? _resourcesOk;

        /// <summary>에디터 A·B 비교 메뉴 등으로 끌 수 있다. 기본 켜짐.</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>풀 생성·재생 중 예외 등 실패가 있었는지(세션 동안 유지).</summary>
        public bool Failed { get; private set; }

        public bool ResourcesChecked => _resourcesOk.HasValue;

        public bool ResourcesOk => _resourcesOk == true;

        /// <summary>정책을 제외한 준비 상태(켜짐·실패 없음·리소스 정상).</summary>
        public bool Ready => Enabled && !Failed && _resourcesOk == true;

        /// <summary>텍스처 유효: 로드 성공, 정사각형, 4의 배수, 최소 64px.</summary>
        public static bool TextureValid(bool loaded, int width, int height) =>
            loaded && width == height && width >= 64 && width % FlipbookSpec.Columns == 0 && height % FlipbookSpec.Rows == 0;

        /// <summary>셰이더 유효: 재질 존재, 셰이더 이름 일치(빌드에서 빠져 오류 셰이더로 바뀐 경우 차단), 기기 지원.</summary>
        public static bool ShaderValid(bool materialLoaded, string shaderName, bool supported) =>
            materialLoaded && shaderName == ShaderName && supported;

        public void SetResources(bool textureValid, bool shaderValid) => _resourcesOk = textureValid && shaderValid;

        public void ReportFailure() => Failed = true;

        /// <summary>새 재생 세션(Play 진입 등) 시작 시 초기화.</summary>
        public void Reset()
        {
            _resourcesOk = null;
            Failed = false;
            Enabled = true;
        }

        /// <summary>최종 판정: 정책(베기 일반 공격) + 준비 상태.</summary>
        public bool UseFlipbook(WeaponType w, SfxTier t) => ImpactFlipbookPolicy.UsesBladeFlipbook(w, t) && Ready;
    }

    /// <summary>
    /// 고정 크기 재생 슬롯 풀(기본 24). 빈 슬롯 → 새 슬롯(용량 안에서) → 가장 오래 재생된 슬롯 재사용 순.
    /// 용량을 넘겨 늘어나지 않으며 예외를 던지지 않는다. 전투 판정과 무관한 표현 계층 전용.
    /// </summary>
    public sealed class FlipbookPool
    {
        public const int DefaultCapacity = 24;

        private readonly bool[] _active;
        private readonly double[] _start;

        public FlipbookPool(int capacity = DefaultCapacity)
        {
            Capacity = Math.Max(1, capacity);
            _active = new bool[Capacity];
            _start = new double[Capacity];
        }

        public int Capacity { get; }

        /// <summary>지금까지 만든 슬롯 수(0~Capacity).</summary>
        public int Created { get; private set; }

        public int ActiveCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < Created; i++) if (_active[i]) n++;
                return n;
            }
        }

        /// <summary>재사용(가장 오래된 것 덮어쓰기)이 일어난 누적 횟수 — 시각적으로 재생이 끊긴 횟수.</summary>
        public int StolenCount { get; private set; }

        public bool IsActive(int slot) => slot >= 0 && slot < Created && _active[slot];

        /// <summary>
        /// 재생 슬롯을 받는다. created = 이번에 새로 만든 슬롯(호출 측이 오브젝트 생성), stolen = 재생 중이던 가장 오래된 슬롯을 재사용.
        /// </summary>
        public int Acquire(double now, out bool created, out bool stolen)
        {
            created = false;
            stolen = false;
            for (int i = 0; i < Created; i++)
            {
                if (_active[i]) continue;
                return Take(i, now);
            }
            if (Created < Capacity)
            {
                created = true;
                return Take(Created++, now);
            }
            int oldest = 0;
            for (int i = 1; i < Created; i++)
                if (_start[i] < _start[oldest]) oldest = i;
            stolen = true;
            StolenCount++;
            return Take(oldest, now);
        }

        public void Release(int slot)
        {
            if (slot >= 0 && slot < Created) _active[slot] = false;
        }

        /// <summary>새로 만든 슬롯의 오브젝트 생성이 실패했을 때 되돌린다(마지막으로 만든 슬롯만).</summary>
        public void CancelCreate(int slot)
        {
            if (slot == Created - 1)
            {
                _active[slot] = false;
                Created--;
            }
            else
            {
                Release(slot);
            }
        }

        private int Take(int i, double now)
        {
            _active[i] = true;
            _start[i] = now;
            return i;
        }
    }
}
