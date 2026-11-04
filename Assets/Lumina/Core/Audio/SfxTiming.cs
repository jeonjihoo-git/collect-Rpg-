using Lumina.Core.Data;

namespace Lumina.Core.Audio
{
    /// <summary>
    /// 휘두름(swing) 효과음의 피크가 명중 프레임에 맞도록 하는 선행 시간.
    /// 전투 코어가 이 값으로 Swing 큐 틱을 계산하고, 합성기는 같은 값으로 휘두름 소리의 피크 위치를 만든다.
    /// [Temp] 실제 애니메이션 확정 시 재조정.
    /// </summary>
    public static class SfxTiming
    {
        public static float SwingLead(WeaponType w)
        {
            switch (w)
            {
                case WeaponType.Longsword: return 0.09f;
                case WeaponType.Dagger: return 0.05f;
                case WeaponType.Greatsword: return 0.20f;
                case WeaponType.DualBlades: return 0.06f;
                case WeaponType.Spear: return 0.08f;
                case WeaponType.Gauntlet: return 0.05f;
                case WeaponType.Scythe: return 0.14f;
                case WeaponType.Axe: return 0.16f;
                default: return 0f;
            }
        }

        /// <summary>다단히트 간격이 이보다 짧으면 첫 타에만 휘두름 큐를 낸다(소리 뭉개짐 방지).</summary>
        public const float MinSwingCueInterval = 0.06f;
    }
}
