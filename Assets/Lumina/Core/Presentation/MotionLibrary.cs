using System;
using System.Collections.Generic;
using Lumina.Core.Audio;
using Lumina.Core.Combat;
using Lumina.Core.Data;

namespace Lumina.Core.Presentation
{
    /// <summary>엔진 비의존 3D 벡터(포즈·FK 계산용).</summary>
    public struct V3
    {
        public float x, y, z;

        public V3(float x, float y, float z)
        {
            this.x = x;
            this.y = y;
            this.z = z;
        }

        public static readonly V3 Zero = new V3(0f, 0f, 0f);
        public static V3 operator +(V3 a, V3 b) => new V3(a.x + b.x, a.y + b.y, a.z + b.z);
        public static V3 operator -(V3 a, V3 b) => new V3(a.x - b.x, a.y - b.y, a.z - b.z);
        public static V3 operator *(V3 a, float s) => new V3(a.x * s, a.y * s, a.z * s);
        public static V3 Lerp(V3 a, V3 b, float t) => new V3(a.x + (b.x - a.x) * t, a.y + (b.y - a.y) * t, a.z + (b.z - a.z) * t);
        public float Length => MathF.Sqrt(x * x + y * y + z * z);
        public V3 MirrorX => new V3(-x, y, z);
        /// <summary>오일러(피치, 요, 롤) 좌우 반전: 요·롤 부호 반전.</summary>
        public V3 MirrorEuler => new V3(x, -y, -z);
    }

    /// <summary>
    /// 아바타 한 프레임 포즈. Unity AvatarView 계층(모델 → 몸통 → 어깨 → 손 → 무기)에 그대로 적용된다.
    /// 오일러는 Unity Quaternion.Euler(피치, 요, 롤) 규약(도 단위).
    /// </summary>
    public struct Pose
    {
        public V3 modelPos;
        public V3 modelRot;
        public V3 torso;
        public V3 armR;
        public V3 armL;
        public V3 reachR;
        public V3 reachL;
        public float legR;
        public float legL;
        public float trail;

        public static Pose Lerp(Pose a, Pose b, float t)
        {
            return new Pose
            {
                modelPos = V3.Lerp(a.modelPos, b.modelPos, t),
                modelRot = V3.Lerp(a.modelRot, b.modelRot, t),
                torso = V3.Lerp(a.torso, b.torso, t),
                armR = V3.Lerp(a.armR, b.armR, t),
                armL = V3.Lerp(a.armL, b.armL, t),
                reachR = V3.Lerp(a.reachR, b.reachR, t),
                reachL = V3.Lerp(a.reachL, b.reachL, t),
                legR = a.legR + (b.legR - a.legR) * t,
                legL = a.legL + (b.legL - a.legL) * t,
                trail = t < 0.5f ? a.trail : b.trail
            };
        }

        /// <summary>좌우 반전(왼손 공격 등).</summary>
        public Pose Mirror()
        {
            return new Pose
            {
                modelPos = modelPos.MirrorX,
                modelRot = modelRot.MirrorEuler,
                torso = torso.MirrorEuler,
                armR = armL.MirrorEuler,
                armL = armR.MirrorEuler,
                reachR = reachL.MirrorX,
                reachL = reachR.MirrorX,
                legR = legL,
                legL = legR,
                trail = trail
            };
        }
    }

    public enum Ease
    {
        Linear,
        In,
        Out,
        InOut,
        /// <summary>사인 InOut(최고속 = 평균의 1.57배) — 예비동작·되돌리기용(3차 InOut 은 3배라 본타보다 빨라짐).</summary>
        Smooth,
        Hold
    }

    /// <summary>무기별 체급·파지·무기 끝 위치(손 기준). WeaponRig 모형과 같은 값.</summary>
    public struct WeaponProfile
    {
        public float weight;
        public bool twoHanded;
        public bool dual;
        public V3 tip;
        public bool tipInLeftHand;
    }

    /// <summary>
    /// 무기 16종 × 스윙 스타일 키프레임 모션. 각 명중 시각 H 를 '접촉 포즈'에 두고
    /// 예비동작(Out) → 가속하며 접촉(In) → 감속하며 관통(Out) → 복귀 순으로 보간해
    /// 무기 끝 속도가 명중 순간 최대가 되도록 한다(MotionTests 로 검증).
    /// </summary>
    public static class MotionLibrary
    {
        // ── 아바타 골격 상수(AvatarView 와 동일) ──
        public static readonly V3 TorsoPivot = new V3(0f, 0.9f, 0f);
        public static readonly V3 ShoulderR = new V3(0.36f, 0.48f, 0f);
        public static readonly V3 ShoulderL = new V3(-0.36f, 0.48f, 0f);
        public const float ArmLength = 0.58f;
        public static readonly V3 HipR = new V3(0.16f, 0.58f, 0f);
        public static readonly V3 HipL = new V3(-0.16f, 0.58f, 0f);
        public const float LegLength = 0.55f;

        public static WeaponProfile Profile(WeaponType w)
        {
            switch (w)
            {
                case WeaponType.Longsword: return P(1.0f, false, false, 0f, 0f, 1.32f);
                case WeaponType.Dagger: return P(0.6f, false, false, 0f, 0f, 0.6f);
                case WeaponType.Greatsword: return P(1.6f, true, false, 0f, 0f, 2.1f);
                case WeaponType.DualBlades: return P(0.7f, false, true, 0f, 0f, 0.88f);
                case WeaponType.Spear: return P(1.1f, true, false, 0f, 0f, 2.05f);
                case WeaponType.Gauntlet: return P(0.8f, false, true, 0f, 0f, 0.28f);
                case WeaponType.Scythe: return P(1.3f, true, false, 0.75f, 0f, 1.6f);
                case WeaponType.Axe: return P(1.4f, true, false, 0f, 0.38f, 0.95f);
                case WeaponType.Bow: return new WeaponProfile { weight = 0.8f, tip = new V3(0f, 0f, 0.1f), tipInLeftHand = true };
                case WeaponType.Crossbow: return P(1.0f, true, false, 0f, 0.06f, 0.7f);
                case WeaponType.Gun: return P(0.7f, true, false, 0f, 0.05f, 0.75f);
                case WeaponType.Launcher: return P(1.4f, true, false, 0f, 0.12f, 1.0f);
                case WeaponType.Staff: return P(0.9f, false, false, 0f, 0f, 1.6f);
                case WeaponType.Grimoire: return new WeaponProfile { weight = 0.8f, tip = new V3(0f, 0.12f, 0.22f), tipInLeftHand = true };
                case WeaponType.Scroll: return P(0.8f, true, false, -0.05f, -0.05f, 0.05f);
                case WeaponType.MagicRing: return P(0.7f, false, false, 0f, 0f, 0.35f);
                default: return P(1f, false, false, 0f, 0f, 1f);
            }
        }

        private static WeaponProfile P(float weight, bool two, bool dual, float tx, float ty, float tz) =>
            new WeaponProfile { weight = weight, twoHanded = two, dual = dual, tip = new V3(tx, ty, tz) };

        /// <summary>무기를 쥔 대기 자세.</summary>
        public static Pose Rest(WeaponType w)
        {
            var p = new Pose { armR = new V3(55f, 12f, 0f), armL = new V3(55f, -12f, 0f) };
            switch (w)
            {
                case WeaponType.Greatsword:
                case WeaponType.Axe:
                case WeaponType.Scythe:
                    p.armR = new V3(32f, -22f, 0f);
                    p.armL = new V3(38f, 12f, 0f);
                    p.torso = new V3(0f, -8f, 0f);
                    p.legR = 8f;
                    p.legL = -8f;
                    break;
                case WeaponType.Spear:
                    p.armR = new V3(20f, 5f, 0f);
                    p.armL = new V3(18f, 22f, 0f);
                    p.reachL = new V3(0.05f, 0f, 0.1f);
                    break;
                case WeaponType.Gauntlet:
                    p.armR = new V3(40f, -25f, 0f);
                    p.armL = new V3(40f, 25f, 0f);
                    p.legR = 10f;
                    p.legL = -10f;
                    break;
                case WeaponType.DualBlades:
                    p.armR = new V3(45f, 25f, 0f);
                    p.armL = new V3(45f, -25f, 0f);
                    break;
                case WeaponType.Bow:
                    // 옆으로 선 궁수 자세: 몸통을 틀고 활 팔은 그만큼 되돌려 과녁(정면)을 겨눈다
                    p.armL = new V3(5f, 18f, 0f);
                    p.armR = new V3(5f, -2f, 0f);
                    p.reachR = new V3(0f, 0f, -0.05f);
                    p.torso = new V3(0f, -18f, 0f);
                    break;
                case WeaponType.Crossbow:
                case WeaponType.Gun:
                    p.armR = new V3(12f, 6f, 0f);
                    p.armL = new V3(22f, 30f, 0f);
                    p.torso = new V3(0f, -8f, 0f);
                    break;
                case WeaponType.Launcher:
                    p.armR = new V3(0f, 0f, 0f);
                    p.reachR = new V3(-0.05f, 0.12f, -0.25f);
                    p.armL = new V3(20f, 25f, 0f);
                    break;
                case WeaponType.Staff:
                    p.armR = new V3(-30f, 10f, 0f);
                    break;
                case WeaponType.Grimoire:
                    p.armL = new V3(20f, 15f, 0f);
                    break;
                case WeaponType.Scroll:
                    p.armR = new V3(20f, -10f, 0f);
                    p.armL = new V3(30f, 20f, 0f);
                    break;
                case WeaponType.MagicRing:
                    p.armR = new V3(15f, 0f, 0f);
                    break;
            }
            return p;
        }

        /// <summary>달리기 주기(phase 라디안).</summary>
        public static Pose Run(WeaponType w, float phase, float intensity = 1f)
        {
            var p = Rest(w);
            float s = MathF.Sin(phase) * intensity;
            p.modelPos.y = MathF.Abs(MathF.Sin(phase)) * 0.08f * intensity;
            p.modelRot.x = 8f * intensity;
            p.armR.x += s * 22f;
            p.armL.x -= s * 22f;
            p.legR = s * 32f;
            p.legL = -s * 32f;
            p.torso.y += s * 6f;
            return p;
        }

        /// <summary>근접 접근 돌진.</summary>
        public static Pose Dash(WeaponType w, float phase)
        {
            var p = Run(w, phase, 1.2f);
            p.modelRot.x = 24f;
            p.armR = new V3(70f, 30f, 0f);
            p.armL = new V3(70f, -30f, 0f);
            return p;
        }

        public static Pose Dodge(WeaponType w, float u, float localX, float localZ)
        {
            var p = Rest(w);
            float k = MathF.Sin(u * MathF.PI);
            p.modelRot = new V3(localZ * 25f * k, 0f, -localX * 25f * k);
            p.modelPos.y = -0.18f * k;
            p.legR = 30f * k;
            p.legL = -20f * k;
            return p;
        }

        public static Pose HitStun(WeaponType w, float u)
        {
            var p = Rest(w);
            float k = 1f - u;
            p.modelRot.x = -16f * k;
            p.torso.x = -10f * k;
            p.armR.x -= 20f * k;
            p.armL.x -= 20f * k;
            return p;
        }

        // ───────────────────────── 공격 ─────────────────────────

        private struct Key
        {
            public float t;
            public Pose p;
            public Ease ease;
        }

        private static readonly Dictionary<(AttackStepDef, WeaponType, ActionKind, int), Key[]> Cache = new Dictionary<(AttackStepDef, WeaponType, ActionKind, int), Key[]>();

        /// <summary>스윙 반폭(명중 전후 가속/감속 구간): 근접은 SwingLead, 그 외 0.06초. 다단히트는 간격의 30% 이내.</summary>
        public static float StrikeHalf(WeaponType w, AttackStepDef def)
        {
            float h = SfxTiming.SwingLead(w);
            if (h <= 0f) h = 0.06f;
            h = Math.Clamp(h, 0.04f, 0.2f);
            // 다단히트: 접촉 구간을 간격의 30% 로 제한 → 되돌리기(나머지 40%)가 본타보다 빠르지 않게
            if (def.SafeHits > 1 && def.hitInterval > 0.0005f) h = MathF.Min(h, def.hitInterval * 0.3f);
            return MathF.Min(h, MathF.Max(0.02f, def.startup));
        }

        /// <summary>공격 포즈. t = 행동 시작 후 경과(초). charge/sinceRelease 는 무기 부가 연출용.</summary>
        public static Pose Attack(WeaponType w, ActionKind kind, int step, AttackStepDef def, float t, out float charge, out float sinceRelease)
        {
            charge = Math.Clamp(t / MathF.Max(0.01f, def.startup), 0f, 1f);
            sinceRelease = t - def.startup;
            var key = (def, w, kind, step);
            if (!Cache.TryGetValue(key, out var keys))
            {
                keys = BuildKeys(w, kind, step, def);
                Cache[key] = keys;
            }
            return Sample(keys, t);
        }

        private static Pose Sample(Key[] keys, float t)
        {
            if (t <= keys[0].t) return keys[0].p;
            for (int i = 1; i < keys.Length; i++)
            {
                if (t <= keys[i].t)
                {
                    float span = keys[i].t - keys[i - 1].t;
                    float u = span > 1e-6f ? (t - keys[i - 1].t) / span : 1f;
                    return Pose.Lerp(keys[i - 1].p, keys[i].p, Apply(keys[i].ease, u));
                }
            }
            return keys[keys.Length - 1].p;
        }

        public static float Apply(Ease e, float u)
        {
            u = Math.Clamp(u, 0f, 1f);
            switch (e)
            {
                case Ease.In: return u * u * u;
                case Ease.Out: return 1f - (1f - u) * (1f - u) * (1f - u);
                case Ease.InOut: return u < 0.5f ? 4f * u * u * u : 1f - MathF.Pow(-2f * u + 2f, 3f) / 2f;
                case Ease.Smooth: return 0.5f - 0.5f * MathF.Cos(u * MathF.PI);
                case Ease.Hold: return 0f;
                default: return u;
            }
        }

        private static Key K(float t, Pose p, Ease e) => new Key { t = t, p = p, ease = e };

        private static Key[] BuildKeys(WeaponType w, ActionKind kind, int step, AttackStepDef def)
        {
            var prof = Profile(w);
            var rest = Rest(w);
            float amp = kind == ActionKind.Ultimate ? 1.35f : kind == ActionKind.Skill ? 1.2f : 1f;
            float heavy = Math.Clamp((prof.weight - 0.6f) / 1.0f, 0f, 1f);
            float h = StrikeHalf(w, def);
            float S = def.startup;
            float end = MathF.Max(def.TotalDuration, def.LastHitTime + h + 0.05f);
            var keys = new List<Key> { K(0f, rest, Ease.Linear) };
            int hits = def.SafeHits;
            bool ranged = WeaponInfo.Category(w) != WeaponCategory.Melee;

            if (def.swing == SwingStyle.Spin)
            {
                // 회전 베기: 반대 방향으로 살짝 감았다가 회전, 명중마다 무기가 정면(요 0)을 지난다.
                // 쌍검은 양손 ±90° → 반 바퀴마다 좌/우 칼날이 번갈아 정면, 그 외는 한 바퀴마다.
                float armYaw = prof.dual ? 90f : 80f;
                float perHit = prof.dual ? 180f : 360f;
                var a = rest;
                a.armR = new V3(15f, armYaw, 0f);
                a.armL = prof.dual ? new V3(15f, -armYaw, 0f) : prof.twoHanded ? new V3(21f, armYaw + 14f, 0f) : rest.armL;
                a.reachL = prof.twoHanded ? new V3(0.05f, 0f, 0.05f) : rest.reachL;
                a.modelRot.y = 30f;
                a.modelPos.y = -0.06f;
                a.legR = 16f;
                a.legL = -16f;
                keys.Add(K(MathF.Max(0.01f, S - h), a, Ease.Out));
                float first = 360f - armYaw;
                for (int i = 0; i < hits; i++)
                {
                    var c = a;
                    c.modelRot.y = first + perHit * i;
                    c.trail = 1f;
                    keys.Add(K(S + i * def.hitInterval, c, i == 0 ? Ease.In : Ease.Linear));
                }
                var f = a;
                f.modelRot.y = first + perHit * (hits - 1) + 60f;
                f.trail = 0f;
                keys.Add(K(def.LastHitTime + h, f, Ease.Out));
                var back = rest;
                back.modelRot.y = 360f * MathF.Ceiling(f.modelRot.y / 360f);
                keys.Add(K(end, back, Ease.InOut));
                return Normalize(keys);
            }

            if (def.swing == SwingStyle.Slam)
            {
                float jump = (0.35f + 0.25f * heavy) * amp;
                var up = rest;
                up.modelPos.y = jump;
                up.armR = new V3(-120f, 0f, 0f);
                up.armL = prof.twoHanded ? new V3(-115f, 8f, 0f) : rest.armL;
                up.torso.x = -10f;
                up.legR = -20f;
                up.legL = 15f;
                var top = up;
                top.modelPos.y = jump * 0.75f;
                top.armR = new V3(-160f, 0f, 0f);
                top.armL = prof.twoHanded ? new V3(-155f, 8f, 0f) : rest.armL;
                top.torso.x = -14f;
                var c = rest;
                c.armR = new V3(35f, 0f, 0f);
                c.armL = prof.twoHanded ? new V3(40f, 8f, 0f) : rest.armL;
                c.modelRot.x = 14f;
                c.torso.x = 16f;
                c.modelPos.z = 0.2f;
                c.trail = 1f;
                var f = c;
                f.modelPos.y = -0.24f * amp;
                f.armR = new V3(72f, 0f, 0f);
                f.armL = prof.twoHanded ? new V3(76f, 8f, 0f) : rest.armL;
                f.modelRot.x = 20f;
                f.legR = 35f;
                f.legL = -30f;
                f.trail = 0f;
                float wind = MathF.Max(0.02f, S - h);
                keys.Add(K(wind * 0.55f, up, Ease.Smooth));
                keys.Add(K(wind, top, Ease.Smooth));
                for (int i = 0; i < hits; i++)
                {
                    float H = S + i * def.hitInterval;
                    if (i > 0)
                    {
                        // 연속 내려찍기: 반쯤만 다시 들어 올림(점프 없음)
                        var re = Pose.Lerp(f, top, 0.55f);
                        re.modelPos.y = jump * 0.15f;
                        keys.Add(K(H - h, re, Ease.Smooth));
                    }
                    keys.Add(K(H, c, Ease.In));
                    keys.Add(K(H + h, f, Ease.Out));
                }
                keys.Add(K(def.LastHitTime + h + 0.06f * (1f + heavy), f, Ease.Hold));
                keys.Add(K(end, rest, Ease.InOut));
                return Normalize(keys);
            }

            if (ranged)
            {
                BuildRanged(keys, w, prof, rest, def, S, h, end, amp);
                return Normalize(keys);
            }

            // 근접 일반형: 명중마다 (예비 → 접촉 → 관통). 접촉 구간(In→Out)에서 무기 끝 속도가 최대.
            V3 holdArmR = rest.armR, holdArmL = rest.armL, holdReachR = rest.reachR, holdReachL = rest.reachL;
            for (int i = 0; i < hits; i++)
            {
                float H = S + i * def.hitInterval;
                bool leftHand = prof.dual && ((step + i) % 2 == 1);
                var style = def.swing;
                // 한 손/양손 무기 연속 가로베기는 왕복(→ ←)으로 이어 친다
                if (!prof.dual && i % 2 == 1 && (style == SwingStyle.HorizontalLR || style == SwingStyle.HorizontalRL))
                    style = style == SwingStyle.HorizontalLR ? SwingStyle.HorizontalRL : SwingStyle.HorizontalLR;
                StylePoses(w, prof, rest, style, amp, heavy, out var A, out var C, out var F);
                // 연속 찌르기/주먹(같은 손)은 절반만 당겼다가 다시 뻗는다
                if (i > 0 && !prof.dual && (style == SwingStyle.Thrust || style == SwingStyle.Punch)) A = Pose.Lerp(C, A, 0.45f);
                var chamber = A;
                if (leftHand)
                {
                    A = A.Mirror();
                    C = C.Mirror();
                    F = F.Mirror();
                }
                if (prof.dual)
                {
                    // 쉬는 손: 주먹·찌르기는 다음 타격을 위해 장전(자기 쪽 예비동작), 베기는 끝난 자리 유지(첫 타는 장전)
                    bool chamberNow = i == 0 || style == SwingStyle.Punch || style == SwingStyle.Thrust;
                    var other = leftHand ? chamber : chamber.Mirror();
                    if (leftHand)
                    {
                        A.armR = C.armR = F.armR = chamberNow ? other.armR : holdArmR;
                        A.reachR = C.reachR = F.reachR = chamberNow ? other.reachR : holdReachR;
                    }
                    else
                    {
                        A.armL = C.armL = F.armL = chamberNow ? other.armL : holdArmL;
                        A.reachL = C.reachL = F.reachL = chamberNow ? other.reachL : holdReachL;
                    }
                }
                holdArmR = F.armR;
                holdArmL = F.armL;
                holdReachR = F.reachR;
                holdReachL = F.reachL;

                if (i == 0)
                {
                    float wind = S - h;
                    float hold = 0.05f * heavy * MathF.Min(1f, S / 0.3f);
                    // 예비동작 시간이 스윙 반폭의 절반보다 짧으면(감는 동작이 본타보다 빨라짐) 예비 자세에서 시작
                    if (wind < MathF.Max(0.06f, h * 0.55f))
                    {
                        // 선딜이 짧은 빠른 무기: 예비 자세에서 시작(진입은 AvatarView 가 짧게 블렌딩)
                        keys[0] = K(0f, A, Ease.Linear);
                        if (wind > 0.005f) keys.Add(K(wind, A, Ease.Hold));
                    }
                    else if (hold > 0f && wind - hold >= h * 0.6f)
                    {
                        keys.Add(K(wind - hold, A, Ease.Smooth));
                        keys.Add(K(wind, Pose.Lerp(A, C, -0.06f), Ease.Smooth));
                    }
                    else
                    {
                        keys.Add(K(wind, A, Ease.Smooth));
                    }
                }
                else
                {
                    keys.Add(K(H - h, A, Ease.Smooth));
                }
                C.trail = 1f;
                keys.Add(K(H, C, Ease.In));
                F.trail = 1f;
                keys.Add(K(H + h, F, Ease.Out));
                if (i + 1 == hits)
                {
                    var settle = F;
                    settle.trail = 0f;
                    keys.Add(K(H + h + 0.04f + 0.08f * heavy, settle, Ease.Hold));
                }
            }
            keys.Add(K(end, rest, Ease.InOut));
            return Normalize(keys);
        }

        /// <summary>키 시간을 단조 증가로 정리(짧은 선딜에서 겹치는 키 보정).</summary>
        private static Key[] Normalize(List<Key> keys)
        {
            for (int i = 1; i < keys.Count; i++)
            {
                if (keys[i].t <= keys[i - 1].t)
                {
                    var k = keys[i];
                    k.t = keys[i - 1].t + 0.001f;
                    keys[i] = k;
                }
            }
            return keys.ToArray();
        }

        /// <summary>근접 스윙 스타일별 예비(A)·접촉(C)·관통(F) 포즈. 오른손 기준.</summary>
        private static void StylePoses(WeaponType w, WeaponProfile prof, Pose rest, SwingStyle style, float amp, float heavy, out Pose A, out Pose C, out Pose F)
        {
            float twist = (32f + 30f * heavy) * amp;
            A = rest;
            C = rest;
            F = rest;
            switch (style)
            {
                case SwingStyle.HorizontalLR:
                case SwingStyle.HorizontalRL:
                {
                    float d = style == SwingStyle.HorizontalLR ? 1f : -1f;
                    A.torso = new V3(0f, -twist * d, 0f);
                    A.armR = new V3(8f, -100f * d, 0f);
                    A.modelRot = new V3(0f, -8f * d, 0f);
                    A.modelPos = new V3(0f, -0.04f - 0.04f * heavy, -0.04f);
                    A.legR = 14f;
                    A.legL = -14f;
                    C.torso = new V3(4f, 0f, 0f);
                    C.armR = new V3(12f, 0f, 0f);
                    C.modelRot = new V3(8f, 0f, 0f);
                    C.modelPos = new V3(0f, -0.06f * heavy, 0.1f);
                    C.legR = -18f;
                    C.legL = 18f;
                    F.torso = new V3(8f, twist * 0.9f * d, 0f);
                    F.armR = new V3(20f, 92f * d, 0f);
                    F.modelRot = new V3(10f, 10f * d, 0f);
                    F.modelPos = new V3(0f, -0.05f - 0.08f * heavy, 0.12f);
                    F.legR = -20f;
                    F.legL = 22f;
                    break;
                }
                case SwingStyle.Overhead:
                    A.torso = new V3(-12f - 6f * heavy, -10f, 0f);
                    A.armR = new V3(-150f - 15f * heavy, 0f, 0f);
                    A.modelRot = new V3(-6f, 0f, 0f);
                    A.modelPos = new V3(0f, 0.03f + 0.06f * heavy, -0.05f);
                    A.legR = 10f;
                    A.legL = -16f;
                    C.torso = new V3(12f, 0f, 0f);
                    C.armR = new V3(8f, 0f, 0f);
                    C.modelRot = new V3(10f, 0f, 0f);
                    C.modelPos = new V3(0f, -0.02f, 0.14f);
                    C.legR = -22f;
                    C.legL = 20f;
                    F.torso = new V3(22f + 8f * heavy, 4f, 0f);
                    F.armR = new V3(72f, 0f, 0f);
                    F.modelRot = new V3(16f, 0f, 0f);
                    F.modelPos = new V3(0f, -0.12f - 0.1f * heavy, 0.16f);
                    F.legR = -26f;
                    F.legL = 26f;
                    break;
                case SwingStyle.Thrust:
                    A.reachR = new V3(-0.05f, 0f, -0.3f);
                    A.armR = new V3(8f, -6f, 0f);
                    A.torso = new V3(0f, -28f * amp, 0f);
                    A.modelPos = new V3(0f, -0.03f, -0.08f);
                    A.legR = 16f;
                    A.legL = -10f;
                    C.reachR = new V3(0.05f, 0f, 0.42f);
                    C.armR = new V3(4f, 0f, 0f);
                    C.torso = new V3(8f, 16f, 0f);
                    C.modelRot = new V3(10f, 0f, 0f);
                    C.modelPos = new V3(0f, -0.04f, 0.2f);
                    C.legR = -24f;
                    C.legL = 22f;
                    F = C;
                    F.reachR = new V3(0.05f, 0f, 0.5f);
                    F.torso = new V3(10f, 22f, 0f);
                    F.modelPos = new V3(0f, -0.05f, 0.24f);
                    break;
                case SwingStyle.Punch:
                    A.reachR = new V3(-0.1f, 0f, -0.18f);
                    A.armR = new V3(6f, -8f, 0f);
                    A.torso = new V3(0f, -24f * amp, 0f);
                    A.modelRot = new V3(0f, -6f, 0f);
                    A.legR = 14f;
                    A.legL = -12f;
                    C.reachR = new V3(-0.12f, 0f, 0.45f);
                    C.armR = new V3(2f, -4f, 0f);
                    C.torso = new V3(6f, 26f, 0f);
                    C.modelRot = new V3(6f, 4f, 0f);
                    C.modelPos = new V3(0f, -0.03f, 0.12f);
                    C.legR = -20f;
                    C.legL = 18f;
                    F = C;
                    F.reachR = new V3(-0.12f, 0f, 0.5f);
                    F.torso = new V3(6f, 30f, 0f);
                    break;
                case SwingStyle.Uppercut:
                    A.modelPos = new V3(0f, -0.2f, 0f);
                    A.armR = new V3(85f, -5f, 0f);
                    A.torso = new V3(16f, -16f, 0f);
                    A.legR = 26f;
                    A.legL = -24f;
                    C.modelPos = new V3(0f, 0f, 0.1f);
                    C.armR = new V3(-12f, -5f, 0f);
                    C.torso = new V3(-6f, 10f, 0f);
                    C.legR = -10f;
                    C.legL = 12f;
                    F.modelPos = new V3(0f, 0.14f, 0.12f);
                    F.armR = new V3(-118f, -5f, 0f);
                    F.torso = new V3(-14f, 14f, 0f);
                    F.modelRot = new V3(-8f, 0f, 0f);
                    break;
                default:
                    C.armR = new V3(10f, 0f, 0f);
                    F.armR = new V3(30f, 40f, 0f);
                    break;
            }

            // 양손 무기: 왼팔이 같은 궤적으로 손잡이를 받친다
            if (prof.twoHanded)
            {
                A.armL = A.armR + new V3(6f, 14f, 0f);
                C.armL = C.armR + new V3(6f, 14f, 0f);
                F.armL = F.armR + new V3(6f, 14f, 0f);
                A.reachL = A.reachR + new V3(0.05f, 0f, 0.05f);
                C.reachL = C.reachR + new V3(0.05f, 0f, 0.05f);
                F.reachL = F.reachR + new V3(0.05f, 0f, 0.05f);
            }
            else if (w == WeaponType.Gauntlet || w == WeaponType.DualBlades)
            {
                // 반대 손은 가드 자세 유지
                A.armL = C.armL = F.armL = rest.armL + new V3(-10f, 0f, 0f);
            }
        }

        private static void BuildRanged(List<Key> keys, WeaponType w, WeaponProfile prof, Pose rest, AttackStepDef def, float S, float h, float end, float amp)
        {
            int hits = def.SafeHits;
            Pose charge = rest, release = rest, follow = rest;
            float kick = prof.weight;
            switch (def.swing)
            {
                case SwingStyle.Draw:
                    charge.reachR = new V3(0f, 0f, -0.46f);
                    charge.armR = new V3(0f, 4f, 0f);
                    charge.armL = new V3(0f, 26f, 0f);
                    charge.torso = new V3(0f, -26f, 0f);
                    charge.legR = 12f;
                    charge.legL = -12f;
                    release = charge;
                    release.reachR = new V3(0f, 0f, 0.05f);
                    release.armR = new V3(4f, -8f, 0f);
                    release.modelRot = new V3(-4f, 0f, 0f);
                    follow = rest;
                    break;
                case SwingStyle.Fire:
                    charge.armR = rest.armR + new V3(-4f, 0f, 0f);
                    charge.torso = rest.torso + new V3(2f, 0f, 0f);
                    charge.legR = 10f;
                    charge.legL = -10f;
                    release = charge;
                    release.armR = charge.armR + new V3(-24f * kick * amp, 0f, 0f);
                    release.armL = charge.armL + new V3(-12f * kick, 0f, 0f);
                    release.reachR = charge.reachR + new V3(0f, 0f, -0.16f * kick);
                    release.modelPos = new V3(0f, 0f, -0.07f * kick);
                    release.modelRot = new V3(-5f * kick, 0f, 0f);
                    follow = charge;
                    break;
                case SwingStyle.Cast:
                    charge.armR = w == WeaponType.Grimoire ? new V3(-40f, -20f, 0f) : new V3(-75f, 6f, 0f);
                    charge.armL = w == WeaponType.Grimoire ? new V3(-10f, 15f, 0f) : rest.armL;
                    charge.torso = new V3(-8f, -10f, 0f);
                    charge.modelPos = new V3(0f, 0.04f, -0.03f);
                    release = charge;
                    release.armR = new V3(2f, w == WeaponType.Grimoire ? -10f : 4f, 0f);
                    release.reachR = new V3(0f, 0f, 0.28f);
                    release.torso = new V3(10f, 8f, 0f);
                    release.modelPos = new V3(0f, -0.02f, 0.1f);
                    release.legR = -16f;
                    release.legL = 16f;
                    follow = release;
                    follow.reachR = new V3(0f, 0f, 0.1f);
                    follow.armR = release.armR + new V3(12f, 0f, 0f);
                    break;
                case SwingStyle.Unfurl:
                    charge.armR = new V3(0f, -28f, 0f);
                    charge.armL = new V3(0f, 28f, 0f);
                    charge.torso = new V3(-5f, 0f, 0f);
                    release = charge;
                    release.reachR = new V3(0f, 0f, 0.22f);
                    release.reachL = new V3(0f, 0f, 0.22f);
                    release.torso = new V3(10f, 0f, 0f);
                    release.modelPos = new V3(0f, -0.04f, 0.1f);
                    follow = charge;
                    break;
                case SwingStyle.RingSpin:
                    charge.armR = new V3(-22f, 0f, 0f);
                    charge.torso = new V3(-6f, -14f, 0f);
                    release = charge;
                    release.armR = new V3(0f, 0f, 0f);
                    release.reachR = new V3(0f, 0f, 0.3f);
                    release.torso = new V3(6f, 12f, 0f);
                    release.modelPos = new V3(0f, 0f, 0.08f);
                    follow = release;
                    follow.reachR = new V3(0f, 0f, 0.12f);
                    break;
                default:
                    release.armR = rest.armR + new V3(-10f, 0f, 0f);
                    break;
            }
            keys.Add(K(MathF.Max(0.01f, S * 0.92f), charge, Ease.InOut));
            for (int i = 0; i < hits; i++)
            {
                float H = S + i * def.hitInterval;
                if (i > 0) keys.Add(K(MathF.Max(keys[keys.Count - 1].t + 0.001f, H - 0.001f), charge, Ease.Out));
                keys.Add(K(H + 0.02f, release, Ease.Out));
                float next = i + 1 < hits ? S + (i + 1) * def.hitInterval - 0.002f : H + 0.12f + 0.08f * prof.weight;
                keys.Add(K(MathF.Max(H + 0.03f, next), follow, Ease.Out));
            }
            keys.Add(K(end, rest, Ease.InOut));
        }

        // ───────────────────────── FK(테스트·미리보기용) ─────────────────────────

        /// <summary>Unity Quaternion.Euler(x,y,z) 와 같은 규약의 회전(Z→X→Y 순, 좌수 좌표계).</summary>
        public static V3 Rotate(V3 euler, V3 v)
        {
            float rx = euler.x * (MathF.PI / 180f), ry = euler.y * (MathF.PI / 180f), rz = euler.z * (MathF.PI / 180f);
            float cz = MathF.Cos(rz), sz = MathF.Sin(rz);
            var a = new V3(cz * v.x - sz * v.y, sz * v.x + cz * v.y, v.z);
            float cx = MathF.Cos(rx), sx = MathF.Sin(rx);
            var b = new V3(a.x, cx * a.y - sx * a.z, sx * a.y + cx * a.z);
            float cy = MathF.Cos(ry), sy = MathF.Sin(ry);
            return new V3(cy * b.x + sy * b.z, b.y, -sy * b.x + cy * b.z);
        }

        private static V3 Chain(V3[] eulers, V3[] offsets, V3 local)
        {
            // offsets[i] = 부모 기준 위치, eulers[i] = 그 노드의 로컬 회전
            V3 p = local;
            for (int i = eulers.Length - 1; i >= 0; i--) p = Rotate(eulers[i], p) + offsets[i];
            return p;
        }

        /// <summary>모델 공간(발 기준, +Z 정면) 관절 위치.</summary>
        public struct Joints
        {
            public V3 hipR, hipL, footR, footL, waist, neck, head, shoulderR, shoulderL, handR, handL, tip, grip;
        }

        public static Joints Solve(WeaponType w, Pose p)
        {
            var prof = Profile(w);
            var j = new Joints();
            V3 model = p.modelPos;
            V3 mr = p.modelRot;
            V3 Mw(V3 local) => Rotate(mr, local) + model;
            j.hipR = Mw(HipR);
            j.hipL = Mw(HipL);
            j.footR = Mw(HipR + Rotate(new V3(p.legR, 0f, 0f), new V3(0f, -LegLength, 0f)));
            j.footL = Mw(HipL + Rotate(new V3(p.legL, 0f, 0f), new V3(0f, -LegLength, 0f)));
            V3 Tw(V3 local) => Mw(TorsoPivot + Rotate(p.torso, local));
            j.waist = Tw(V3.Zero);
            j.neck = Tw(new V3(0f, 0.62f, 0f));
            j.head = Tw(new V3(0f, 0.88f, 0f));
            V3 shR = ShoulderR + p.reachR;
            V3 shL = ShoulderL + p.reachL;
            j.shoulderR = Tw(shR);
            j.shoulderL = Tw(shL);
            j.handR = Tw(shR + Rotate(p.armR, new V3(0f, 0f, ArmLength)));
            j.handL = Tw(shL + Rotate(p.armL, new V3(0f, 0f, ArmLength)));
            if (prof.tipInLeftHand)
            {
                j.grip = j.handL;
                j.tip = Tw(shL + Rotate(p.armL, new V3(0f, 0f, ArmLength) + prof.tip));
            }
            else
            {
                j.grip = j.handR;
                j.tip = Tw(shR + Rotate(p.armR, new V3(0f, 0f, ArmLength) + prof.tip));
            }
            return j;
        }

        /// <summary>좌우 반전 포즈에서 왼손 무기 끝(쌍검·건틀릿 교대 타격 검증용).</summary>
        public static V3 TipLeft(WeaponType w, Pose p)
        {
            var prof = Profile(w);
            V3 shL = ShoulderL + p.reachL;
            V3 local = TorsoPivot + Rotate(p.torso, shL + Rotate(p.armL, new V3(0f, 0f, ArmLength) + prof.tip));
            return Rotate(p.modelRot, local) + p.modelPos;
        }
    }
}
