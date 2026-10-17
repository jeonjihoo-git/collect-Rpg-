using System.Collections.Generic;
using Lumina.Core.Data;
using UnityEngine;

namespace Lumina.Runtime.Battle
{
    /// <summary>
    /// 무기 16종의 임시 모형(프리미티브 조합)과 무기 고유 부가 연출(활 시위, 스크롤 전개, 마법링 회전 등).
    /// 정식 3D 모델/애니메이션이 들어오면 이 클래스를 모델 프리팹 바인딩으로 교체한다.
    /// </summary>
    public sealed class WeaponRig
    {
        public WeaponType type;
        public readonly List<Transform> tips = new List<Transform>();
        public readonly List<TrailRenderer> trails = new List<TrailRenderer>();
        public Transform muzzle;
        public Transform spinner;
        public Transform scrollPaper;
        public Transform orb;
        public Transform bookRune;
        public LineRenderer bowString;
        public Transform bowTop;
        public Transform bowBottom;
        public Transform stringHand;

        private static readonly Color Steel = new Color(0.78f, 0.8f, 0.86f);
        private static readonly Color DarkSteel = new Color(0.32f, 0.34f, 0.4f);
        private static readonly Color Wood = new Color(0.45f, 0.3f, 0.18f);
        private static readonly Color Leather = new Color(0.3f, 0.2f, 0.14f);
        private static readonly Color GoldTrim = new Color(0.79f, 0.65f, 0.36f);

        public static WeaponRig Build(WeaponType type, Transform handR, Transform handL, Color element)
        {
            var r = new WeaponRig { type = type };
            var steel = Mats.Lit(Steel, 0.8f);
            var dark = Mats.Lit(DarkSteel, 0.6f);
            var wood = Mats.Lit(Wood, 0.2f);
            var leather = Mats.Lit(Leather, 0.1f);
            var trim = Mats.Lit(GoldTrim, 0.7f);
            var glow = Mats.Unlit(new Color(element.r, element.g, element.b, 0.95f));

            switch (type)
            {
                case WeaponType.Longsword:
                    Box(handR, "Grip", 0f, 0f, 0.05f, 0.06f, 0.06f, 0.24f, leather);
                    Box(handR, "Guard", 0f, 0f, 0.2f, 0.32f, 0.05f, 0.06f, trim);
                    Box(handR, "Blade", 0f, 0f, 0.78f, 0.09f, 0.025f, 1.1f, steel);
                    r.AddTip(handR, 1.32f, Color.white, 0.22f);
                    break;
                case WeaponType.Dagger:
                    Box(handR, "Grip", 0f, 0f, 0.04f, 0.05f, 0.05f, 0.16f, leather);
                    Box(handR, "Blade", 0f, 0f, 0.36f, 0.06f, 0.02f, 0.48f, steel);
                    r.AddTip(handR, 0.6f, Color.white, 0.12f);
                    break;
                case WeaponType.Greatsword:
                    Box(handR, "Grip", 0f, 0f, 0.05f, 0.08f, 0.08f, 0.4f, leather);
                    Box(handR, "Guard", 0f, 0f, 0.28f, 0.5f, 0.08f, 0.1f, dark);
                    Box(handR, "Blade", 0f, 0f, 1.2f, 0.26f, 0.05f, 1.8f, steel);
                    r.AddTip(handR, 2.1f, new Color(1f, 0.95f, 0.85f), 0.45f);
                    break;
                case WeaponType.DualBlades:
                    foreach (var h in new[] { handR, handL })
                    {
                        Box(h, "Grip", 0f, 0f, 0.04f, 0.05f, 0.05f, 0.16f, leather);
                        Box(h, "Blade", 0f, 0f, 0.5f, 0.07f, 0.02f, 0.75f, steel);
                        r.AddTip(h, 0.88f, new Color(0.8f, 1f, 0.95f), 0.14f);
                    }
                    break;
                case WeaponType.Spear:
                    Cyl(handR, "Shaft", 0f, 0f, 0.55f, 0.06f, 2.3f, wood);
                    Box(handR, "Head", 0f, 0f, 1.85f, 0.14f, 0.025f, 0.4f, steel);
                    r.AddTip(handR, 2.05f, Color.white, 0.12f);
                    break;
                case WeaponType.Gauntlet:
                    foreach (var h in new[] { handR, handL })
                    {
                        Box(h, "Fist", 0f, 0f, 0.08f, 0.24f, 0.22f, 0.3f, dark);
                        Box(h, "Knuckle", 0f, 0.0f, 0.24f, 0.26f, 0.08f, 0.06f, trim);
                        r.AddTip(h, 0.28f, new Color(1f, 0.85f, 0.6f), 0.18f);
                    }
                    break;
                case WeaponType.Scythe:
                {
                    Cyl(handR, "Pole", 0f, 0f, 0.7f, 0.06f, 2.2f, wood);
                    // 끝에서 오른쪽(+X)으로 휘어지는 날
                    var blade = new GameObject("Blade").transform;
                    blade.SetParent(handR, false);
                    blade.localPosition = new Vector3(0f, 0f, 1.75f);
                    for (int i = 0; i < 5; i++)
                    {
                        float a = i * 18f;
                        var seg = ProcMesh.Prim(PrimitiveType.Cube, "Seg" + i, blade,
                            new Vector3(Mathf.Sin(a * Mathf.Deg2Rad) * 0.55f + 0.1f, 0f, -Mathf.Cos(a * Mathf.Deg2Rad) * 0.25f + 0.25f - i * 0.06f),
                            new Vector3(0.32f, 0.025f, 0.12f - i * 0.012f), steel, Quaternion.Euler(0f, 90f - a, 0f));
                        seg.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                    }
                    var tip = new GameObject("Tip").transform;
                    tip.SetParent(blade, false);
                    tip.localPosition = new Vector3(0.75f, 0f, -0.15f);
                    r.tips.Add(tip);
                    r.trails.Add(Trail(tip, new Color(0.85f, 0.75f, 1f), 0.3f));
                    break;
                }
                case WeaponType.Axe:
                    Cyl(handR, "Handle", 0f, 0f, 0.4f, 0.06f, 1.1f, wood);
                    Box(handR, "Head", 0f, 0.16f, 0.9f, 0.07f, 0.42f, 0.36f, dark);
                    Box(handR, "Edge", 0f, 0.39f, 0.9f, 0.075f, 0.06f, 0.4f, steel);
                    r.AddTip(handR, 0.95f, new Color(1f, 0.9f, 0.75f), 0.35f, 0.38f);
                    break;
                case WeaponType.Bow:
                {
                    var grip = new GameObject("BowGrip").transform;
                    grip.SetParent(handL, false);
                    grip.localPosition = new Vector3(0f, 0f, 0.1f);
                    for (int i = -3; i <= 3; i++)
                    {
                        float y = i * 0.17f;
                        float z = -Mathf.Abs(i) * Mathf.Abs(i) * 0.018f;
                        ProcMesh.Prim(PrimitiveType.Cube, "Limb" + i, grip, new Vector3(0f, y, z), new Vector3(0.05f, 0.19f, 0.05f), wood,
                            Quaternion.Euler(-i * 6f, 0f, 0f));
                    }
                    r.bowTop = new GameObject("Top").transform;
                    r.bowTop.SetParent(grip, false);
                    r.bowTop.localPosition = new Vector3(0f, 0.6f, -0.16f);
                    r.bowBottom = new GameObject("Bottom").transform;
                    r.bowBottom.SetParent(grip, false);
                    r.bowBottom.localPosition = new Vector3(0f, -0.6f, -0.16f);
                    r.stringHand = handR;
                    r.bowString = Line(grip.gameObject, new Color(0.95f, 0.95f, 0.9f), 0.015f, 3);
                    r.muzzle = grip;
                    break;
                }
                case WeaponType.Crossbow:
                    Box(handR, "Stock", 0f, 0f, 0.3f, 0.12f, 0.1f, 0.7f, wood);
                    Box(handR, "Prod", 0f, 0.02f, 0.6f, 0.85f, 0.05f, 0.07f, dark);
                    Box(handR, "Rail", 0f, 0.06f, 0.4f, 0.05f, 0.03f, 0.5f, steel);
                    r.muzzle = Empty(handR, "Muzzle", 0f, 0.06f, 0.7f);
                    break;
                case WeaponType.Gun:
                    Box(handR, "Body", 0f, 0.02f, 0.18f, 0.1f, 0.16f, 0.42f, dark);
                    Cyl(handR, "Barrel", 0f, 0.05f, 0.5f, 0.05f, 0.45f, steel);
                    Box(handR, "Grip", 0f, -0.1f, 0.05f, 0.08f, 0.2f, 0.1f, leather);
                    r.muzzle = Empty(handR, "Muzzle", 0f, 0.05f, 0.75f);
                    break;
                case WeaponType.Launcher:
                    Cyl(handR, "Tube", 0f, 0.12f, 0.35f, 0.24f, 1.3f, Mats.Lit(new Color(0.3f, 0.36f, 0.3f), 0.3f));
                    Box(handR, "Sight", 0.1f, 0.28f, 0.3f, 0.06f, 0.1f, 0.15f, dark);
                    r.muzzle = Empty(handR, "Muzzle", 0f, 0.12f, 1.0f);
                    break;
                case WeaponType.Staff:
                    Cyl(handR, "Shaft", 0f, 0f, 0.55f, 0.06f, 1.9f, wood);
                    Box(handR, "Crown", 0f, 0f, 1.45f, 0.18f, 0.18f, 0.06f, trim);
                    r.orb = ProcMesh.Prim(PrimitiveType.Sphere, "Orb", handR, new Vector3(0f, 0f, 1.6f), Vector3.one * 0.26f, glow).transform;
                    r.muzzle = r.orb;
                    break;
                case WeaponType.Grimoire:
                {
                    Box(handL, "Book", 0f, 0f, 0.22f, 0.36f, 0.09f, 0.46f, Mats.Lit(new Color(0.35f, 0.18f, 0.22f), 0.3f));
                    Box(handL, "Pages", 0f, 0.05f, 0.22f, 0.32f, 0.02f, 0.42f, Mats.Lit(new Color(0.93f, 0.9f, 0.8f), 0.1f));
                    var rune = ProcMesh.Flat("Rune", handL, ProcMesh.Ring(0.8f), Mats.Unlit(new Color(element.r, element.g, element.b, 0.85f)), 0.12f);
                    rune.transform.localPosition = new Vector3(0f, 0.12f, 0.22f);
                    rune.transform.localScale = Vector3.zero;
                    r.bookRune = rune.transform;
                    r.muzzle = rune.transform;
                    break;
                }
                case WeaponType.Scroll:
                {
                    var rod = Cyl(handR, "Rod", -0.05f, 0f, 0.05f, 0.06f, 0.55f, trim);
                    rod.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
                    var paper = new GameObject("Paper").transform;
                    paper.SetParent(handR, false);
                    paper.localPosition = new Vector3(-0.05f, -0.05f, 0.05f);
                    ProcMesh.Prim(PrimitiveType.Cube, "Sheet", paper, new Vector3(0f, -0.4f, 0f), new Vector3(0.5f, 0.8f, 0.01f), Mats.Lit(new Color(0.95f, 0.9f, 0.75f), 0.1f));
                    ProcMesh.Prim(PrimitiveType.Cube, "Glyph", paper, new Vector3(0f, -0.4f, 0.012f), new Vector3(0.3f, 0.5f, 0.005f), glow);
                    paper.localScale = new Vector3(1f, 0.05f, 1f);
                    r.scrollPaper = paper;
                    r.muzzle = paper;
                    break;
                }
                case WeaponType.MagicRing:
                {
                    var spin = new GameObject("RingSpin").transform;
                    spin.SetParent(handR, false);
                    spin.localPosition = new Vector3(0f, 0f, 0.35f);
                    for (int i = 0; i < 12; i++)
                    {
                        float a = i * 30f * Mathf.Deg2Rad;
                        ProcMesh.Prim(PrimitiveType.Sphere, "Bead" + i, spin, new Vector3(Mathf.Cos(a) * 0.32f, Mathf.Sin(a) * 0.32f, 0f), Vector3.one * (i % 3 == 0 ? 0.11f : 0.07f), i % 3 == 0 ? glow : trim);
                    }
                    r.spinner = spin;
                    r.muzzle = spin;
                    break;
                }
            }
            if (r.muzzle == null && r.tips.Count > 0) r.muzzle = r.tips[0];
            if (r.muzzle == null) r.muzzle = handR;
            SetTrails(r, false);
            return r;
        }

        private void AddTip(Transform hand, float z, Color trailColor, float width, float y = 0f)
        {
            var tip = Empty(hand, "Tip", 0f, y, z);
            tips.Add(tip);
            trails.Add(Trail(tip, trailColor, width));
        }

        public static void SetTrails(WeaponRig r, bool on)
        {
            foreach (var t in r.trails)
            {
                if (t != null) t.emitting = on;
            }
        }

        /// <summary>무기 고유 부가 연출. charge = 선딜 진행도(0~1), sinceRelease = 발동 후 경과(초, 음수면 발동 전).</summary>
        public void Animate(float charge, float sinceRelease, bool casting, float dt)
        {
            if (spinner != null)
            {
                float speed = casting ? Mathf.Lerp(180f, 1440f, charge) : 120f;
                spinner.Rotate(0f, 0f, speed * dt, Space.Self);
                float pulse = casting && sinceRelease >= 0f && sinceRelease < 0.15f ? 1.4f - sinceRelease * 2.6f : 1f;
                spinner.localScale = Vector3.one * pulse;
            }
            if (scrollPaper != null)
            {
                float open = casting ? Mathf.SmoothStep(0.05f, 1f, Mathf.Clamp01(charge * 1.2f)) : 0.05f;
                if (casting && sinceRelease > 0.1f) open = Mathf.Max(0.05f, 1f - (sinceRelease - 0.1f) * 4f);
                scrollPaper.localScale = new Vector3(1f, open, 1f);
            }
            if (orb != null)
            {
                float s = casting ? Mathf.Lerp(0.26f, 0.5f, charge) : 0.26f;
                if (casting && sinceRelease >= 0f) s = Mathf.Lerp(0.5f, 0.26f, Mathf.Clamp01(sinceRelease * 5f));
                orb.localScale = Vector3.one * s;
            }
            if (bookRune != null)
            {
                float s = casting ? Mathf.Lerp(0f, 1.1f, charge) : 0f;
                if (casting && sinceRelease >= 0f) s = Mathf.Lerp(1.1f, 0f, Mathf.Clamp01(sinceRelease * 4f));
                bookRune.localScale = new Vector3(s, 1f, s);
                bookRune.Rotate(0f, 240f * dt, 0f, Space.Self);
            }
            if (bowString != null && bowTop != null)
            {
                Vector3 nock = bowString.transform.InverseTransformPoint(stringHand.position);
                Vector3 mid = casting && (sinceRelease < 0f) ? Vector3.Lerp(new Vector3(0f, 0f, -0.16f), nock, charge) : new Vector3(0f, 0f, -0.16f);
                bowString.SetPosition(0, bowTop.localPosition);
                bowString.SetPosition(1, mid);
                bowString.SetPosition(2, bowBottom.localPosition);
            }
        }

        private static GameObject Box(Transform parent, string name, float x, float y, float z, float sx, float sy, float sz, Material m)
        {
            return ProcMesh.Prim(PrimitiveType.Cube, name, parent, new Vector3(x, y, z), new Vector3(sx, sy, sz), m);
        }

        /// <summary>Z 축 방향으로 누운 원기둥(길이 len).</summary>
        private static GameObject Cyl(Transform parent, string name, float x, float y, float z, float dia, float len, Material m)
        {
            return ProcMesh.Prim(PrimitiveType.Cylinder, name, parent, new Vector3(x, y, z), new Vector3(dia, len * 0.5f, dia), m, Quaternion.Euler(90f, 0f, 0f));
        }

        private static Transform Empty(Transform parent, string name, float x, float y, float z)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.localPosition = new Vector3(x, y, z);
            return t;
        }

        private static TrailRenderer Trail(Transform at, Color c, float width)
        {
            var tr = at.gameObject.AddComponent<TrailRenderer>();
            tr.time = 0.12f;
            tr.minVertexDistance = 0.03f;
            tr.startWidth = width;
            tr.endWidth = 0f;
            tr.material = Mats.UnlitInstance(Color.white);
            tr.startColor = new Color(c.r, c.g, c.b, 0.85f);
            tr.endColor = new Color(c.r, c.g, c.b, 0f);
            tr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            tr.receiveShadows = false;
            tr.emitting = false;
            return tr;
        }

        private static LineRenderer Line(GameObject go, Color c, float width, int points)
        {
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = false;
            lr.positionCount = points;
            lr.startWidth = width;
            lr.endWidth = width;
            lr.material = Mats.UnlitInstance(c);
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return lr;
        }
    }
}
