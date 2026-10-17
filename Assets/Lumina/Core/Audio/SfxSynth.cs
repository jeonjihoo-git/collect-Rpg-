using System;
using Lumina.Core.Data;
using Lumina.Core.Util;

namespace Lumina.Core.Audio
{
    /// <summary>사운드 이벤트 명명(GDD 06-2): swing / cast·projectile / impact / hitReaction / tail.</summary>
    public enum SfxKind
    {
        Swing,
        Cast,
        Projectile,
        Impact,
        HitReaction,
        Tail
    }

    public enum SystemSfx
    {
        Dodge,
        PerfectDodge,
        Tag,
        Break,
        PlayerHurt,
        EnemyTelegraph,
        EnemySwing,
        EnemyProjectile,
        EnemyDeath,
        UltReady,
        Heal,
        Buff,
        Shield,
        Reaction,
        Victory,
        Defeat,
        UiClick
    }

    /// <summary>
    /// 무기별 절차적 효과음 레시피. 16개 무기가 재질·거리·강도·잔향이 다른 레이어 조합을 사용하며,
    /// 공통 징/차임을 주타격음으로 쓰지 않는다. 정식 음원 확보 시 SfxBank 매핑을 교체하는 것이 목표.
    /// </summary>
    public static class SfxSynth
    {
        public static float TierScale(SfxTier t)
        {
            switch (t)
            {
                case SfxTier.Light: return 0.85f;
                case SfxTier.Medium: return 1f;
                case SfxTier.Heavy: return 1.15f;
                case SfxTier.Finisher: return 1.3f;
                case SfxTier.Skill: return 1.35f;
                case SfxTier.Ultimate: return 1.6f;
                default: return 1f;
            }
        }

        public static float TierPeak(SfxTier t)
        {
            switch (t)
            {
                case SfxTier.Light: return 0.55f;
                case SfxTier.Medium: return 0.68f;
                case SfxTier.Heavy: return 0.82f;
                case SfxTier.Finisher: return 0.9f;
                case SfxTier.Skill: return 0.9f;
                case SfxTier.Ultimate: return 0.98f;
                default: return 0.7f;
            }
        }

        private static DeterministicRng Rng(int a, int b, int c, int d, int variant) =>
            new DeterministicRng(DeterministicRng.Hash(a, b, c, d, variant, 0x51F));

        public static float[] RenderWeapon(WeaponType w, SfxKind kind, SfxTier tier, Element element, int variant, float castSeconds = 0.3f)
        {
            var rng = Rng((int)w, (int)kind, (int)tier, (int)element, variant);
            float k = TierScale(tier);
            bool big = tier == SfxTier.Finisher || tier == SfxTier.Skill || tier == SfxTier.Ultimate || tier == SfxTier.Heavy;
            float[] b = new float[1];
            switch (kind)
            {
                case SfxKind.Swing: b = Swing(b, w, k, big, rng); break;
                case SfxKind.Cast: b = Cast(b, w, k, Math.Clamp(castSeconds, 0.05f, 1.2f), element, rng); break;
                case SfxKind.Projectile: b = Release(b, w, k, element, rng); break;
                case SfxKind.Impact: b = Impact(b, w, k, big, rng); break;
                case SfxKind.Tail: b = Tail(b, w, k, rng); break;
                case SfxKind.HitReaction: b = Reaction(b, EnemyMaterial.Flesh, k, rng); break;
            }
            if ((kind == SfxKind.Impact || kind == SfxKind.Projectile) && element != Element.None &&
                (tier == SfxTier.Skill || tier == SfxTier.Ultimate || WeaponInfo.Category(w) == WeaponCategory.Magic))
            {
                b = ElementLayer(b, 0f, element, kind == SfxKind.Impact ? 0.55f * k : 0.3f * k, rng);
            }
            if (tier == SfxTier.Ultimate && kind == SfxKind.Impact)
            {
                b = Dsp.Thump(b, 0f, 90f, 38f, 0.05f, 0.16f, 0.6f);
                b = Dsp.Rumble(b, 0.01f, 0.6f, 260f, 0.01f, 0.25f, 0.35f, rng);
            }
            return Dsp.Finish(b, kind == SfxKind.Swing || kind == SfxKind.Cast ? TierPeak(tier) * 0.75f : TierPeak(tier));
        }

        // ───────────── swing: 근접 휘두름(피크 = SfxTiming.SwingLead) ─────────────
        private static float[] Swing(float[] b, WeaponType w, float k, bool big, DeterministicRng rng)
        {
            float lead = SfxTiming.SwingLead(w);
            float Dur(float tail) => lead + tail * k;
            float Peak(float tail) => lead / Dur(tail);
            switch (w)
            {
                case WeaponType.Longsword:
                    b = Dsp.Whoosh(b, 0f, Dur(0.10f), Peak(0.10f), 1100f, 3400f, 2.2f, 0.9f, rng);
                    if (big) b = Dsp.Whoosh(b, 0f, Dur(0.16f), Peak(0.16f), 500f, 1300f, 1.4f, 0.5f, rng);
                    break;
                case WeaponType.Dagger:
                    b = Dsp.Whoosh(b, 0f, Dur(0.05f), Peak(0.05f), 2600f, 5200f, 3f, 0.8f, rng);
                    break;
                case WeaponType.Greatsword:
                    b = Dsp.Whoosh(b, 0f, Dur(0.22f), Peak(0.22f), 220f, 850f, 1.2f, 1f, rng);
                    b = Dsp.Whoosh(b, 0.03f, Dur(0.18f) - 0.03f, Peak(0.18f), 600f, 1500f, 1.6f, 0.45f, rng);
                    b = Dsp.Rumble(b, 0f, Dur(0.2f), 160f, lead * 0.9f, 0.08f, 0.35f, rng);
                    break;
                case WeaponType.DualBlades:
                    b = Dsp.Whoosh(b, 0f, Dur(0.06f), Peak(0.06f), 2000f, 4300f, 2.6f, 0.85f, rng);
                    break;
                case WeaponType.Spear:
                    b = Dsp.Whoosh(b, 0f, Dur(0.06f), Peak(0.06f), 900f, 2700f, 4f, 0.9f, rng);
                    b = Dsp.NoiseHit(b, lead * 0.6f, 0.01f, 0.02f, 3000f, 7000f, 0.15f, rng);
                    break;
                case WeaponType.Gauntlet:
                    b = Dsp.Whoosh(b, 0f, Dur(0.05f), Peak(0.05f), 480f, 1400f, 1.5f, 0.9f, rng);
                    b = Dsp.Rustle(b, 0f, lead, 2500f, 220f, 0.15f, rng);
                    break;
                case WeaponType.Scythe:
                    b = Dsp.Whoosh(b, 0f, Dur(0.26f), Peak(0.26f), 700f, 900f, 1.8f, 0.9f, rng, 2600f);
                    b = Dsp.NoiseHit(b, lead, 0.02f, 0.12f, 4500f, 11000f, 0.18f, rng);
                    break;
                case WeaponType.Axe:
                    b = Dsp.Whoosh(b, 0f, Dur(0.14f), Peak(0.14f), 330f, 1100f, 1.4f, 1f, rng);
                    if (big) b = Dsp.Rumble(b, 0f, Dur(0.12f), 200f, lead, 0.05f, 0.25f, rng);
                    break;
                default:
                    b = Dsp.Whoosh(b, 0f, 0.15f, 0.5f, 800f, 2000f, 1.5f, 0.5f, rng);
                    break;
            }
            return b;
        }

        // ───────────── cast: 원거리 장전/당김, 마법 충전 ─────────────
        private static float[] Cast(float[] b, WeaponType w, float k, float dur, Element el, DeterministicRng rng)
        {
            switch (w)
            {
                case WeaponType.Bow:
                    b = Dsp.Rustle(b, 0f, dur, 600f, 70f, 0.25f, rng);
                    b = Dsp.Tone(b, 0f, dur, 120f, 165f, Dsp.Wave.Saw, 7f, 0.04f, dur * 0.6f, 0.02f, 0.12f, 500f, 18f, 0.6f);
                    break;
                case WeaponType.Crossbow:
                    for (int i = 0; i < 3; i++)
                    {
                        float s = i * MathF.Min(0.035f, dur / 4f);
                        b = Dsp.Click(b, s, 2600f, 0.002f, 0.5f);
                        b = Dsp.Click(b, s, 420f, 0.006f, 0.35f);
                    }
                    b = Dsp.NoiseHit(b, MathF.Min(0.12f, dur * 0.8f), 0.001f, 0.012f, 900f, 2400f, 0.6f, rng);
                    b = Dsp.Click(b, MathF.Min(0.12f, dur * 0.8f), 1700f, 0.004f, 0.5f);
                    break;
                case WeaponType.Gun:
                    b = Dsp.Click(b, 0f, 4200f, 0.0015f, 0.35f);
                    b = Dsp.Click(b, 0.025f, 2100f, 0.002f, 0.3f);
                    break;
                case WeaponType.Launcher:
                    b = Dsp.Tone(b, 0f, dur, 260f, 820f, Dsp.Wave.Square, 0f, 0f, 0.03f, 0.03f, 0.18f, 1400f);
                    b = Dsp.Thump(b, MathF.Max(0f, dur - 0.05f), 260f, 140f, 0.01f, 0.025f, 0.5f);
                    b = Dsp.Click(b, MathF.Max(0f, dur - 0.05f), 1500f, 0.004f, 0.4f);
                    break;
                case WeaponType.Staff:
                    b = Dsp.Tone(b, 0f, dur, 200f, 600f, Dsp.Wave.Sine, 6f, 0.03f, dur * 0.7f, 0.03f, 0.35f);
                    b = Dsp.Whoosh(b, 0f, dur, 0.95f, 900f, 4200f, 3f, 0.4f, rng);
                    b = ElementLayer(b, dur * 0.5f, el, 0.2f, rng);
                    break;
                case WeaponType.Grimoire:
                    b = Dsp.Rustle(b, 0f, dur * 0.6f, 3200f, 45f, 0.6f, rng);
                    b = Dsp.Tone(b, 0f, dur, 110f, 140f, Dsp.Wave.Triangle, 0f, 0f, 0.05f, 0.05f, 0.3f, 0f, 9f, 0.7f);
                    break;
                case WeaponType.Scroll:
                    b = Dsp.Whoosh(b, 0f, dur * 0.85f, 0.7f, 1800f, 5200f, 1.8f, 0.55f, rng);
                    b = Dsp.Rustle(b, 0f, dur * 0.85f, 2800f, 110f, 0.35f, rng);
                    b = Dsp.NoiseHit(b, dur * 0.85f, 0.001f, 0.01f, 1500f, 6000f, 0.6f, rng);
                    break;
                case WeaponType.MagicRing:
                    b = Dsp.Tone(b, 0f, dur, 330f, 990f, Dsp.Wave.Saw, 0f, 0f, dur * 0.6f, 0.03f, 0.3f, 1800f, 14f, 0.8f);
                    b = Dsp.Whoosh(b, 0f, dur, 0.9f, 700f, 2800f, 2.5f, 0.35f, rng);
                    break;
                default:
                    b = Dsp.Whoosh(b, 0f, dur, 0.8f, 600f, 1600f, 1.5f, 0.4f, rng);
                    break;
            }
            return b;
        }

        // ───────────── projectile: 발사/방출 ─────────────
        private static float[] Release(float[] b, WeaponType w, float k, Element el, DeterministicRng rng)
        {
            switch (w)
            {
                case WeaponType.Bow:
                    b = Dsp.Pluck(b, 0f, 175f, 0.16f * k, 0.985f, 0.7f, rng);
                    b = Dsp.NoiseHit(b, 0f, 0.0005f, 0.006f, 2000f, 9000f, 0.5f, rng);
                    b = Dsp.Whoosh(b, 0.02f, 0.26f, 0.3f, 3200f, 2300f, 7f, 0.35f, rng);
                    break;
                case WeaponType.Crossbow:
                    b = Dsp.Click(b, 0f, 1800f, 0.003f, 0.8f);
                    b = Dsp.Pluck(b, 0f, 260f, 0.07f * k, 0.97f, 0.55f, rng);
                    b = Dsp.Metal(b, 0.004f, 900f, new[] { 1f, 1.57f }, new[] { 0.03f, 0.02f }, 0.35f);
                    b = Dsp.Thump(b, 0f, 140f, 90f, 0.01f, 0.03f, 0.6f);
                    break;
                case WeaponType.Gun:
                    b = Dsp.NoiseHit(b, 0f, 0.0004f, 0.01f * k, 1000f, 12000f, 1.2f, rng);
                    b = Dsp.Thump(b, 0f, 170f, 55f, 0.015f, 0.06f * k, 0.9f);
                    b = Dsp.Rumble(b, 0.003f, 0.25f * k, 900f, 0.002f, 0.06f * k, 0.4f, rng);
                    b = Dsp.Click(b, 0.045f, 3000f, 0.002f, 0.25f);
                    break;
                case WeaponType.Launcher:
                    b = Dsp.Thump(b, 0f, 95f, 45f, 0.03f, 0.09f * k, 1f);
                    b = Dsp.NoiseHit(b, 0f, 0.002f, 0.03f, 300f, 3000f, 0.5f, rng);
                    b = Dsp.Whoosh(b, 0.02f, 0.4f, 0.25f, 1300f, 1700f, 1.2f, 0.55f, rng);
                    break;
                case WeaponType.Staff:
                    b = Dsp.Whoosh(b, 0f, 0.2f * k, 0.25f, 420f, 1700f, 2f, 0.7f, rng);
                    b = Dsp.Thump(b, 0f, 180f, 120f, 0.02f, 0.04f, 0.35f);
                    break;
                case WeaponType.Grimoire:
                    b = Dsp.Tone(b, 0f, 0.13f, 300f, 900f, Dsp.Wave.Saw, 0f, 0f, 0.005f, 0.05f, 0.35f, 1600f);
                    b = Dsp.Rustle(b, 0f, 0.08f, 3500f, 120f, 0.35f, rng);
                    break;
                case WeaponType.Scroll:
                    b = Dsp.Thump(b, 0f, 210f, 120f, 0.01f, 0.03f, 0.8f);
                    b = Dsp.NoiseHit(b, 0f, 0.0008f, 0.02f, 1800f, 4500f, 0.6f, rng);
                    b = Dsp.Crackle(b, 0.01f, 0.1f, 300f, 0.05f, 2500f, 0.3f, rng);
                    break;
                case WeaponType.MagicRing:
                    b = Dsp.Whoosh(b, 0f, 0.32f * k, 0.3f, 500f, 2600f, 2.2f, 0.6f, rng);
                    b = Dsp.Tone(b, 0f, 0.28f * k, 230f, 110f, Dsp.Wave.Saw, 0f, 0f, 0.005f, 0.1f, 0.35f, 1200f, 16f, 0.6f);
                    break;
                default:
                    b = Dsp.Whoosh(b, 0f, 0.2f, 0.3f, 600f, 1500f, 1.5f, 0.5f, rng);
                    break;
            }
            return b;
        }

        // ───────────── impact: 무기별 명중음 ─────────────
        private static float[] Impact(float[] b, WeaponType w, float k, bool big, DeterministicRng rng)
        {
            switch (w)
            {
                case WeaponType.Longsword:
                    b = Dsp.NoiseHit(b, 0f, 0.0005f, 0.012f * k, 2500f, 9000f, 0.9f, rng);
                    b = Dsp.Thump(b, 0f, 180f, 90f, 0.02f, 0.035f * k, 0.55f);
                    b = Dsp.Metal(b, 0.001f, 2300f, new[] { 1f, 2.76f, 5.4f }, new[] { 0.04f, 0.025f, 0.015f }, 0.16f);
                    if (big) b = Dsp.Thump(b, 0.004f, 120f, 60f, 0.03f, 0.08f, 0.6f);
                    break;
                case WeaponType.Dagger:
                    b = Dsp.NoiseHit(b, 0f, 0.0003f, 0.006f * k, 3500f, 11000f, 0.8f, rng);
                    b = Dsp.Thump(b, 0f, 240f, 140f, 0.01f, 0.018f * k, 0.35f);
                    b = Dsp.Click(b, 0.001f, 5200f, 0.0015f, 0.2f);
                    break;
                case WeaponType.Greatsword:
                    b = Dsp.Thump(b, 0f, 110f, 45f, 0.04f, 0.11f * k, 1f);
                    b = Dsp.NoiseHit(b, 0f, 0.001f, 0.04f * k, 400f, 4000f, 0.7f, rng);
                    b = Dsp.Metal(b, 0.002f, 420f, new[] { 1f, 1.58f, 2.31f, 3.4f }, new[] { 0.16f, 0.12f, 0.09f, 0.06f }, 0.28f);
                    b = Dsp.Rumble(b, 0.005f, 0.6f, 300f, 0.01f, 0.2f * k, 0.4f, rng);
                    break;
                case WeaponType.DualBlades:
                    b = Dsp.NoiseHit(b, 0f, 0.0004f, 0.008f, 2800f, 10000f, 0.7f, rng);
                    b = Dsp.NoiseHit(b, 0.022f, 0.0004f, 0.008f, 3200f, 11000f, 0.6f, rng);
                    b = Dsp.Thump(b, 0f, 200f, 110f, 0.015f, 0.025f * k, 0.4f);
                    b = Dsp.Metal(b, 0.023f, 3100f, new[] { 1f, 2.4f }, new[] { 0.02f, 0.012f }, 0.12f);
                    break;
                case WeaponType.Spear:
                    b = Dsp.NoiseHit(b, 0f, 0.0006f, 0.015f * k, 1200f, 2200f, 1f, rng);
                    b = Dsp.Thump(b, 0f, 150f, 80f, 0.02f, 0.045f * k, 0.6f);
                    b = Dsp.Whoosh(b, 0.06f, 0.12f, 0.2f, 3000f, 1500f, 3f, 0.25f, rng);
                    break;
                case WeaponType.Gauntlet:
                    b = Dsp.Thump(b, 0f, 120f, 55f, 0.025f, 0.07f * k, 1f);
                    b = Dsp.NoiseHit(b, 0f, 0.0008f, 0.009f, 300f, 3000f, 0.75f, rng);
                    b = Dsp.Click(b, 0f, 2200f, 0.003f, 0.3f);
                    b = Dsp.Metal(b, 0.001f, 900f, new[] { 1f, 2.2f }, new[] { 0.03f, 0.02f }, 0.1f);
                    break;
                case WeaponType.Scythe:
                    b = Dsp.NoiseHit(b, 0f, 0.0006f, 0.018f * k, 2000f, 9000f, 0.85f, rng);
                    b = Dsp.NoiseHit(b, 0.01f, 0.03f, 0.22f * k, 4000f, 12000f, 0.3f, rng);
                    b = Dsp.Thump(b, 0f, 160f, 90f, 0.02f, 0.04f * k, 0.5f);
                    break;
                case WeaponType.Axe:
                    b = Dsp.Thump(b, 0f, 140f, 70f, 0.02f, 0.06f * k, 1f);
                    b = Dsp.Thump(b, 0f, 320f, 300f, 0.5f, 0.02f, 0.45f);
                    b = Dsp.NoiseHit(b, 0f, 0.0008f, 0.025f * k, 600f, 5000f, 0.75f, rng);
                    b = Dsp.Whoosh(b, 0.09f, 0.1f, 0.3f, 1200f, 500f, 2f, 0.25f, rng);
                    break;
                case WeaponType.Bow:
                    b = Dsp.Thump(b, 0f, 220f, 120f, 0.012f, 0.03f * k, 0.8f);
                    b = Dsp.Thump(b, 0f, 700f, 690f, 1f, 0.012f, 0.3f);
                    b = Dsp.Thump(b, 0.002f, 1100f, 1090f, 1f, 0.008f, 0.2f);
                    b = Dsp.NoiseHit(b, 0f, 0.0006f, 0.012f, 400f, 2500f, 0.5f, rng);
                    break;
                case WeaponType.Crossbow:
                    b = Dsp.Thump(b, 0f, 180f, 90f, 0.015f, 0.045f * k, 0.9f);
                    b = Dsp.NoiseHit(b, 0f, 0.0005f, 0.018f * k, 800f, 6000f, 0.75f, rng);
                    b = Dsp.Metal(b, 0f, 1800f, new[] { 1f, 2.7f }, new[] { 0.02f, 0.012f }, 0.2f);
                    b = Dsp.Crackle(b, 0.005f, 0.05f, 900f, 0.02f, 1500f, 0.3f, rng);
                    break;
                case WeaponType.Gun:
                    b = Dsp.NoiseHit(b, 0f, 0.0002f, 0.005f, 3000f, 12000f, 0.85f, rng);
                    b = Dsp.Thump(b, 0f, 300f, 150f, 0.008f, 0.018f * k, 0.5f);
                    b = Dsp.Crackle(b, 0.004f, 0.06f, 1200f, 0.025f, 2500f, 0.25f, rng);
                    break;
                case WeaponType.Launcher:
                    b = Dsp.Thump(b, 0f, 70f, 30f, 0.06f, 0.22f * k, 1f);
                    b = Dsp.Rumble(b, 0f, 1.1f * k, 420f, 0.004f, 0.45f * k, 0.9f, rng);
                    b = Dsp.Crackle(b, 0.02f, 0.6f, 500f, 0.25f, 1500f, 0.35f, rng);
                    b = Dsp.NoiseHit(b, 0f, 0.001f, 0.06f, 200f, 6000f, 0.8f, rng);
                    break;
                case WeaponType.Staff:
                    // 마력 구체 파열: 공기 '퍽' + 부드러운 하강 톤 + 흩어지는 미세 입자(금속성 없음)
                    b = Dsp.NoiseHit(b, 0f, 0.004f, 0.03f * k, 1200f, 6000f, 0.7f, rng);
                    b = Dsp.Thump(b, 0f, 150f, 70f, 0.03f, 0.05f * k, 0.45f);
                    b = Dsp.Tone(b, 0f, 0.16f * k, 520f, 140f, Dsp.Wave.Sine, 0f, 0f, 0.003f, 0.1f, 0.3f);
                    b = Dsp.NoiseHit(b, 0.015f, 0.03f, 0.16f * k, 5000f, 12000f, 0.3f, rng);
                    break;
                case WeaponType.Grimoire:
                    for (int i = 0; i < 4; i++)
                    {
                        float s = i * rng.Range(0.012f, 0.03f);
                        b = Dsp.NoiseHit(b, s, 0.0006f, 0.01f, 1000f, 5000f, 0.6f, rng);
                    }
                    b = Dsp.Thump(b, 0f, 180f, 90f, 0.02f, 0.04f * k, 0.6f);
                    break;
                case WeaponType.Scroll:
                    b = Dsp.NoiseHit(b, 0f, 0.0005f, 0.01f, 1500f, 9000f, 0.85f, rng);
                    b = Dsp.Thump(b, 0f, 130f, 60f, 0.03f, 0.07f * k, 0.8f);
                    b = Dsp.Crackle(b, 0.01f, 0.15f, 600f, 0.07f, 3000f, 0.35f, rng);
                    break;
                case WeaponType.MagicRing:
                    b = Dsp.Tone(b, 0f, 0.18f * k, 180f, 90f, Dsp.Wave.Saw, 0f, 0f, 0.002f, 0.12f, 0.5f, 900f);
                    b = Dsp.NoiseHit(b, 0f, 0.001f, 0.02f, 1000f, 4000f, 0.6f, rng);
                    b = Dsp.Whoosh(b, 0f, 0.14f, 0.2f, 1500f, 600f, 2f, 0.3f, rng);
                    break;
            }
            return b;
        }

        // ───────────── tail: 강한 타격의 잔향(무기별) ─────────────
        private static float[] Tail(float[] b, WeaponType w, float k, DeterministicRng rng)
        {
            switch (w)
            {
                case WeaponType.Longsword:
                case WeaponType.DualBlades:
                    b = Dsp.NoiseHit(b, 0f, 0.01f, 0.12f * k, 6000f, 14000f, 0.25f, rng);
                    b = Dsp.Rumble(b, 0f, 0.4f, 1800f, 0.01f, 0.15f, 0.12f, rng);
                    break;
                case WeaponType.Dagger:
                    b = Dsp.NoiseHit(b, 0f, 0.005f, 0.06f, 5000f, 12000f, 0.2f, rng);
                    break;
                case WeaponType.Greatsword:
                case WeaponType.Axe:
                    b = Dsp.Rumble(b, 0f, 0.9f * k, 170f, 0.02f, 0.35f * k, 0.6f, rng);
                    b = Dsp.Crackle(b, 0.02f, 0.35f, 250f, 0.15f, 1200f, 0.2f, rng);
                    break;
                case WeaponType.Spear:
                    b = Dsp.Whoosh(b, 0f, 0.18f, 0.2f, 2800f, 1200f, 3f, 0.3f, rng);
                    break;
                case WeaponType.Gauntlet:
                    b = Dsp.Rumble(b, 0f, 0.5f, 250f, 0.01f, 0.22f, 0.45f, rng);
                    break;
                case WeaponType.Scythe:
                    b = Dsp.NoiseHit(b, 0f, 0.04f, 0.32f * k, 3500f, 11000f, 0.3f, rng);
                    break;
                case WeaponType.Bow:
                    b = Dsp.Tone(b, 0f, 0.25f, 95f, 92f, Dsp.Wave.Sine, 0f, 0f, 0.002f, 0.2f, 0.3f, 0f, 26f, 0.9f);
                    break;
                case WeaponType.Crossbow:
                    b = Dsp.Crackle(b, 0f, 0.2f, 500f, 0.08f, 2000f, 0.3f, rng);
                    break;
                case WeaponType.Gun:
                    b = Dsp.Rumble(b, 0.03f, 0.6f, 700f, 0.02f, 0.3f, 0.35f, rng);
                    break;
                case WeaponType.Launcher:
                    b = Dsp.Rumble(b, 0f, 1.6f * k, 200f, 0.05f, 0.7f * k, 0.6f, rng);
                    b = Dsp.Crackle(b, 0.05f, 0.8f, 160f, 0.35f, 900f, 0.25f, rng);
                    break;
                default:
                    b = Dsp.Crackle(b, 0f, 0.25f, 400f, 0.1f, 3000f, 0.2f, rng);
                    b = Dsp.Rumble(b, 0f, 0.4f, 600f, 0.01f, 0.15f, 0.2f, rng);
                    break;
            }
            return b;
        }

        // ───────────── hitReaction: 피격 대상 재질 반응 ─────────────
        public static float[] RenderHitReaction(EnemyMaterial m, SfxTier tier, int variant)
        {
            var rng = Rng(900, (int)m, (int)tier, 0, variant);
            float k = TierScale(tier);
            var b = Reaction(new float[1], m, k, rng);
            return Dsp.Finish(b, TierPeak(tier) * 0.55f);
        }

        private static float[] Reaction(float[] b, EnemyMaterial m, float k, DeterministicRng rng)
        {
            switch (m)
            {
                case EnemyMaterial.Armor:
                    b = Dsp.Metal(b, 0f, 520f, new[] { 1f, 1.48f, 2.1f, 2.9f }, new[] { 0.07f, 0.05f, 0.035f, 0.02f }, 0.35f);
                    b = Dsp.Thump(b, 0f, 120f, 70f, 0.02f, 0.04f * k, 0.6f);
                    break;
                case EnemyMaterial.Phantom:
                    b = Dsp.NoiseHit(b, 0f, 0.004f, 0.06f * k, 500f, 1100f, 0.7f, rng);
                    b = Dsp.Tone(b, 0f, 0.14f * k, 260f, 170f, Dsp.Wave.Sine, 5f, 0.05f, 0.004f, 0.08f, 0.3f);
                    break;
                default:
                    b = Dsp.Thump(b, 0f, 140f, 80f, 0.02f, 0.04f * k, 0.7f);
                    b = Dsp.NoiseHit(b, 0f, 0.002f, 0.025f, 80f, 800f, 0.5f, rng);
                    break;
            }
            return b;
        }

        // ───────────── 속성 레이어 ─────────────
        public static float[] ElementLayer(float[] b, float start, Element el, float gain, DeterministicRng rng)
        {
            switch (el)
            {
                case Element.Fire:
                    b = Dsp.Crackle(b, start, 0.3f, 350f, 0.12f, 2000f, gain * 0.8f, rng);
                    b = Dsp.Rumble(b, start, 0.35f, 800f, 0.02f, 0.12f, gain * 0.6f, rng);
                    break;
                case Element.Water:
                    b = Dsp.Splash(b, start, 0.2f, gain, rng);
                    break;
                case Element.Grass:
                    b = Dsp.Rustle(b, start, 0.15f, 3000f, 160f, gain * 0.8f, rng);
                    b = Dsp.Thump(b, start, 420f, 380f, 0.5f, 0.02f, gain * 0.5f);
                    break;
                case Element.Ice:
                    b = Dsp.NoiseHit(b, start, 0.001f, 0.025f, 4000f, 14000f, gain * 0.9f, rng);
                    b = Dsp.Crackle(b, start, 0.12f, 900f, 0.05f, 5000f, gain * 0.6f, rng);
                    break;
                case Element.Rock:
                    b = Dsp.Thump(b, start, 90f, 50f, 0.03f, 0.09f, gain);
                    b = Dsp.Crackle(b, start, 0.2f, 500f, 0.08f, 800f, gain * 0.6f, rng);
                    break;
                case Element.Light:
                    b = Dsp.NoiseHit(b, start, 0.02f, 0.12f, 5000f, 9000f, gain * 0.5f, rng);
                    b = Dsp.Tone(b, start, 0.12f, 800f, 2400f, Dsp.Wave.Sine, 0f, 0f, 0.01f, 0.06f, gain * 0.25f);
                    break;
                case Element.Dark:
                    b = Dsp.Rumble(b, start, 0.35f, 220f, 0.02f, 0.15f, gain * 0.8f, rng);
                    b = Dsp.Tone(b, start, 0.3f, 58f, 50f, Dsp.Wave.Saw, 0f, 0f, 0.01f, 0.15f, gain * 0.4f, 400f, 30f, 0.8f);
                    break;
                case Element.Wind:
                    b = Dsp.Whoosh(b, start, 0.25f, 0.35f, 800f, 3000f, 1.5f, gain * 0.8f, rng);
                    break;
                case Element.Lightning:
                    b = Dsp.Zap(b, start, 0.15f, 120f, gain * 0.8f, rng);
                    b = Dsp.NoiseHit(b, start, 0.0003f, 0.006f, 2000f, 12000f, gain * 0.7f, rng);
                    break;
            }
            return b;
        }

        // ───────────── 시스템/공용 ─────────────
        public static float[] RenderSystem(SystemSfx s, Element a, Element b2, int variant)
        {
            var rng = Rng(1000, (int)s, (int)a, (int)b2, variant);
            float[] b = new float[1];
            float peak = 0.7f;
            switch (s)
            {
                case SystemSfx.Dodge:
                    b = Dsp.Whoosh(b, 0f, 0.22f, 0.35f, 600f, 2200f, 1.5f, 0.8f, rng);
                    b = Dsp.Rustle(b, 0f, 0.12f, 2200f, 150f, 0.2f, rng);
                    peak = 0.5f;
                    break;
                case SystemSfx.PerfectDodge:
                    b = Dsp.Whoosh(b, 0f, 0.35f, 0.9f, 300f, 3200f, 1.8f, 0.8f, rng);
                    b = Dsp.Tone(b, 0.05f, 0.3f, 600f, 1800f, Dsp.Wave.Sine, 0f, 0f, 0.2f, 0.08f, 0.25f);
                    b = Dsp.Thump(b, 0.33f, 120f, 60f, 0.02f, 0.06f, 0.6f);
                    peak = 0.8f;
                    break;
                case SystemSfx.Tag:
                    b = Dsp.Whoosh(b, 0f, 0.16f, 0.4f, 1000f, 3000f, 1.6f, 0.7f, rng);
                    b = Dsp.Thump(b, 0.1f, 140f, 80f, 0.02f, 0.05f, 0.6f);
                    b = Dsp.NoiseHit(b, 0.1f, 0.01f, 0.08f, 5000f, 10000f, 0.2f, rng);
                    peak = 0.6f;
                    break;
                case SystemSfx.Break:
                    b = Dsp.NoiseHit(b, 0f, 0.0005f, 0.04f, 2000f, 12000f, 0.9f, rng);
                    b = Dsp.Crackle(b, 0.005f, 0.35f, 1500f, 0.12f, 4000f, 0.6f, rng);
                    b = Dsp.Thump(b, 0f, 100f, 45f, 0.04f, 0.12f, 1f);
                    peak = 0.95f;
                    break;
                case SystemSfx.PlayerHurt:
                    b = Dsp.Thump(b, 0f, 160f, 90f, 0.02f, 0.05f, 0.9f);
                    b = Dsp.NoiseHit(b, 0f, 0.001f, 0.03f, 200f, 1500f, 0.6f, rng);
                    peak = 0.75f;
                    break;
                case SystemSfx.EnemyTelegraph:
                    b = Dsp.Tone(b, 0f, 0.32f, 80f, 170f, Dsp.Wave.Saw, 0f, 0f, 0.2f, 0.06f, 0.6f, 700f);
                    b = Dsp.Whoosh(b, 0f, 0.32f, 0.8f, 300f, 900f, 1.2f, 0.4f, rng);
                    peak = 0.5f;
                    break;
                case SystemSfx.EnemySwing:
                    b = Dsp.Whoosh(b, 0f, 0.3f, 0.5f, 280f, 900f, 1.3f, 0.9f, rng);
                    peak = 0.55f;
                    break;
                case SystemSfx.EnemyProjectile:
                    b = Dsp.Whoosh(b, 0f, 0.25f, 0.2f, 800f, 1200f, 6f, 0.6f, rng);
                    b = Dsp.Tone(b, 0f, 0.2f, 300f, 200f, Dsp.Wave.Triangle, 9f, 0.05f, 0.01f, 0.1f, 0.3f);
                    peak = 0.5f;
                    break;
                case SystemSfx.EnemyDeath:
                    b = Dsp.Whoosh(b, 0f, 0.5f, 0.15f, 1600f, 300f, 1.4f, 0.6f, rng);
                    b = Dsp.Tone(b, 0f, 0.45f, 300f, 90f, Dsp.Wave.Triangle, 0f, 0f, 0.01f, 0.3f, 0.3f);
                    peak = 0.6f;
                    break;
                case SystemSfx.UltReady:
                    b = Dsp.Whoosh(b, 0f, 0.45f, 0.85f, 1500f, 6000f, 2f, 0.4f, rng);
                    b = Dsp.Tone(b, 0f, 0.45f, 400f, 800f, Dsp.Wave.Triangle, 0f, 0f, 0.3f, 0.12f, 0.25f);
                    peak = 0.45f;
                    break;
                case SystemSfx.Heal:
                    b = Dsp.Tone(b, 0f, 0.5f, 440f, 660f, Dsp.Wave.Sine, 0f, 0f, 0.15f, 0.25f, 0.3f);
                    b = Dsp.Tone(b, 0.05f, 0.5f, 554f, 830f, Dsp.Wave.Sine, 0f, 0f, 0.15f, 0.25f, 0.2f);
                    b = Dsp.NoiseHit(b, 0f, 0.1f, 0.15f, 5000f, 10000f, 0.15f, rng);
                    peak = 0.45f;
                    break;
                case SystemSfx.Buff:
                    b = Dsp.Tone(b, 0f, 0.4f, 220f, 440f, Dsp.Wave.Saw, 0f, 0f, 0.1f, 0.15f, 0.3f, 1800f);
                    b = Dsp.Tone(b, 0f, 0.4f, 330f, 660f, Dsp.Wave.Saw, 0f, 0f, 0.1f, 0.15f, 0.2f, 1800f);
                    peak = 0.45f;
                    break;
                case SystemSfx.Shield:
                    b = Dsp.NoiseHit(b, 0f, 0.002f, 0.04f, 3000f, 12000f, 0.7f, rng);
                    b = Dsp.Crackle(b, 0f, 0.15f, 800f, 0.06f, 4500f, 0.4f, rng);
                    b = Dsp.Tone(b, 0f, 0.4f, 110f, 120f, Dsp.Wave.Triangle, 0f, 0f, 0.05f, 0.2f, 0.4f);
                    peak = 0.6f;
                    break;
                case SystemSfx.Reaction:
                    b = Dsp.Thump(b, 0f, 120f, 55f, 0.03f, 0.08f, 0.7f);
                    b = ElementLayer(b, 0f, a, 0.8f, rng);
                    b = ElementLayer(b, 0.02f, b2, 0.8f, rng);
                    peak = 0.85f;
                    break;
                case SystemSfx.Victory:
                {
                    float[] notes = { 523.25f, 659.25f, 783.99f, 1046.5f };
                    for (int i = 0; i < notes.Length; i++)
                        b = Dsp.Tone(b, i * 0.12f, i == notes.Length - 1 ? 0.6f : 0.2f, notes[i], notes[i], Dsp.Wave.Triangle, 5f, 0.004f, 0.01f, 0.12f, 0.35f);
                    peak = 0.55f;
                    break;
                }
                case SystemSfx.Defeat:
                {
                    float[] notes = { 392f, 349.23f, 311.13f, 261.63f };
                    for (int i = 0; i < notes.Length; i++)
                        b = Dsp.Tone(b, i * 0.18f, i == notes.Length - 1 ? 0.7f : 0.25f, notes[i], notes[i], Dsp.Wave.Triangle, 0f, 0f, 0.02f, 0.15f, 0.35f);
                    peak = 0.5f;
                    break;
                }
                case SystemSfx.UiClick:
                    b = Dsp.Click(b, 0f, 1900f, 0.004f, 0.6f);
                    b = Dsp.NoiseHit(b, 0f, 0.0005f, 0.004f, 3000f, 9000f, 0.2f, rng);
                    peak = 0.35f;
                    break;
            }
            return Dsp.Finish(b, peak);
        }
    }
}
