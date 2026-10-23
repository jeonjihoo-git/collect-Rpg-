using System;

namespace Lumina.Core.Data
{
    /// <summary>값의 근거 수준. 확정 기획 / 원본·웹 시제품 기준값 / 이 저장소에서 둔 임시 가정값을 구분한다.</summary>
    public enum DataStatus
    {
        Confirmed,
        Prototype,
        Temp
    }

    /// <summary>9속성 [확정]. 공격 타입·역할과는 별도 필드.</summary>
    public enum Element
    {
        None = 0,
        Fire,
        Water,
        Grass,
        Ice,
        Rock,
        Light,
        Dark,
        Wind,
        Lightning
    }

    /// <summary>공격 종류 [확정]: 물리/마법. 속성과 별도.</summary>
    public enum AttackType
    {
        Physical,
        Magic
    }

    /// <summary>전투 역할 [확정]. 권장 구성이지 강제가 아님.</summary>
    public enum Role
    {
        MainDps,
        SubDps,
        Healer,
        Buffer
    }

    public enum Rarity
    {
        N,
        R,
        SR,
        SSR,
        UR
    }

    public enum WeaponCategory
    {
        Melee,
        Ranged,
        Magic
    }

    /// <summary>16개 무기 계열 [확정 목록]. 17번째 계열은 승인 전 추가 금지(C-01).</summary>
    public enum WeaponType
    {
        Longsword,
        Dagger,
        Greatsword,
        DualBlades,
        Spear,
        Gauntlet,
        Scythe,
        Axe,
        Bow,
        Crossbow,
        Gun,
        Launcher,
        Staff,
        Grimoire,
        Scroll,
        MagicRing
    }

    public enum Difficulty
    {
        Normal,
        Hard,
        Hell
    }

    public static class ElementInfo
    {
        public static readonly Element[] All =
        {
            Element.Fire, Element.Water, Element.Grass, Element.Ice, Element.Rock,
            Element.Light, Element.Dark, Element.Wind, Element.Lightning
        };

        public static string Ko(Element e)
        {
            switch (e)
            {
                case Element.Fire: return "불";
                case Element.Water: return "물";
                case Element.Grass: return "풀";
                case Element.Ice: return "얼음";
                case Element.Rock: return "바위";
                case Element.Light: return "빛";
                case Element.Dark: return "어둠";
                case Element.Wind: return "바람";
                case Element.Lightning: return "번개";
                default: return "무";
            }
        }

        /// <summary>속성 부착 상태명 [GDD 04-1 초기 부착 상태]. 지속·중첩·효과 수치는 미확정.</summary>
        public static string StatusKo(Element e)
        {
            switch (e)
            {
                case Element.Fire: return "화상";
                case Element.Water: return "침수";
                case Element.Grass: return "속박";
                case Element.Ice: return "빙결";
                case Element.Rock: return "파쇄";
                case Element.Light: return "심판";
                case Element.Dark: return "침식";
                case Element.Wind: return "절단";
                case Element.Lightning: return "감전";
                default: return "";
            }
        }

        public static Element FromKo(string ko)
        {
            foreach (Element e in All)
            {
                if (Ko(e) == ko) return e;
            }
            throw new ArgumentException("알 수 없는 속성: " + ko);
        }
    }

    public static class WeaponInfo
    {
        public static readonly WeaponType[] All = (WeaponType[])Enum.GetValues(typeof(WeaponType));

        public static string Ko(WeaponType w)
        {
            switch (w)
            {
                case WeaponType.Longsword: return "장검";
                case WeaponType.Dagger: return "단검";
                case WeaponType.Greatsword: return "대검";
                case WeaponType.DualBlades: return "쌍검";
                case WeaponType.Spear: return "창";
                case WeaponType.Gauntlet: return "건틀릿";
                case WeaponType.Scythe: return "낫";
                case WeaponType.Axe: return "도끼";
                case WeaponType.Bow: return "활";
                case WeaponType.Crossbow: return "석궁";
                case WeaponType.Gun: return "총";
                case WeaponType.Launcher: return "런처";
                case WeaponType.Staff: return "지팡이";
                case WeaponType.Grimoire: return "마법책";
                case WeaponType.Scroll: return "스크롤";
                case WeaponType.MagicRing: return "마법링";
                default: return w.ToString();
            }
        }

        public static WeaponCategory Category(WeaponType w)
        {
            switch (w)
            {
                case WeaponType.Bow:
                case WeaponType.Crossbow:
                case WeaponType.Gun:
                case WeaponType.Launcher:
                    return WeaponCategory.Ranged;
                case WeaponType.Staff:
                case WeaponType.Grimoire:
                case WeaponType.Scroll:
                case WeaponType.MagicRing:
                    return WeaponCategory.Magic;
                default:
                    return WeaponCategory.Melee;
            }
        }

        public static WeaponType FromKo(string ko)
        {
            foreach (WeaponType w in All)
            {
                if (Ko(w) == ko) return w;
            }
            throw new ArgumentException("알 수 없는 무기: " + ko);
        }
    }

    public static class RoleInfo
    {
        public static string Ko(Role r)
        {
            switch (r)
            {
                case Role.MainDps: return "메인 딜러";
                case Role.SubDps: return "서브 딜러";
                case Role.Healer: return "힐러";
                case Role.Buffer: return "버퍼";
                default: return r.ToString();
            }
        }
    }

    public static class AttackTypeInfo
    {
        public static string Ko(AttackType t) => t == AttackType.Physical ? "물리" : "마법";
    }
}
